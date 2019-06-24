using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Castle.MicroKernel.Registration;
using Castle.Windsor;
using Castle.Windsor.Installer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using Raven.Client.Documents;
using Raven.Client.Extensions;
using Raven.Client.ServerWide;
using Raven.Client.ServerWide.Commands;
using Raven.Client.ServerWide.Operations;
using Raven.Embedded;
using Sparrow.Json;
using TestingEnvironment.Client;
using TestingEnvironment.Common;
using TestingEnvironment.Common.OrchestratorReporting;

namespace TestingEnvironment.Orchestrator
{
    public class Orchestrator
    {
        private const string OrchestratorDatabaseName = "Orchestrator";

        // ReSharper disable once InconsistentNaming
        private static readonly Lazy<Orchestrator> _instance = new Lazy<Orchestrator>(() => new Orchestrator());
        public static Orchestrator Instance => _instance.Value;

        //we need this - perhaps we would need to monitor server statuses? create/delete additional databases?
        private readonly Dictionary<ClusterInfo, IDocumentStore> _clusterDocumentStores = new Dictionary<ClusterInfo, IDocumentStore>();
        private readonly WindsorContainer _container = new WindsorContainer();

        private readonly IDocumentStore _reportingDocumentStore;

        private readonly ITestConfigSelectorStrategy[] _configSelectorStrategies;
        private ITestConfigSelectorStrategy _currentConfigSelectorStrategy;

        // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
        private readonly OrchestratorConfiguration _config;

        static Orchestrator()
        {
            var _ = _instance.Value;
        }

        protected Orchestrator()
        {
            var configProvider = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            _config = new OrchestratorConfiguration();
            ConfigurationBinder.Bind(configProvider, _config);

            if (_config.Databases?.Length == 0)
            {
                throw new InvalidOperationException("Must be at least one database configured!");
            }

            Console.WriteLine($"EmbeddedServerUrl:{_config.EmbeddedServerUrl}");
            Console.WriteLine($"OrchestratorUrl:{_config.OrchestratorUrl}");
            Console.Write("Starting Embedded RavenDB... ");


            foreach (var serverInfo in _config.LocalRavenServers ?? Enumerable.Empty<ServerInfo>())
            {
                RaiseServer(serverInfo);
            }

            EmbeddedServer.Instance.StartServer(new ServerOptions
            {
                ServerUrl = _config.EmbeddedServerUrl,
                CommandLineArgs = new List<string> { " --Security.UnsecuredAccessAllowed=PublicNetwork ", " --Setup.Mode=None ", $" --PublicServerUrl={_config.EmbeddedServerUrl}" }
            });
            _reportingDocumentStore = EmbeddedServer.Instance.GetDocumentStore(new DatabaseOptions(OrchestratorDatabaseName));
            _reportingDocumentStore.Initialize();
            new LatestTestByName().Execute(_reportingDocumentStore);
            new FailTests().Execute(_reportingDocumentStore);

            Console.WriteLine("Done.");

            if (_config.Clusters == null || _config.Clusters.Length == 0)
            {
                throw new InvalidOperationException("Must be at least one RavenDB cluster info configured!");
            }

            _container.Register(Classes.FromAssembly(typeof(Orchestrator).Assembly)
                .BasedOn<ITestConfigSelectorStrategy>()
                .WithServiceAllInterfaces()
                .LifestyleSingleton());

            _configSelectorStrategies = _container.ResolveAll<ITestConfigSelectorStrategy>();
            if (_configSelectorStrategies.Length == 0)
                throw new InvalidOperationException("Something really bad happened... there is no config selector strategies implemented!");

            foreach (var strategy in _configSelectorStrategies)
                strategy.Initialize(_config);

            //TODO: make this choice persistent? (via the embedded RavenDB instance)
            _currentConfigSelectorStrategy = _configSelectorStrategies[0];

            foreach (var clusterInfo in _config.Clusters ?? Enumerable.Empty<ClusterInfo>())
            {
                var cert = clusterInfo.PfxFilePath == null || clusterInfo.PfxFilePath.Equals("") ? null : new System.Security.Cryptography.X509Certificates.X509Certificate2(clusterInfo.PfxFilePath);
                var store = new DocumentStore
                {
                    Database = _config.Databases?[0],
                    Urls = clusterInfo.Urls,
                    Certificate = cert
                };
                store.Initialize();
                _clusterDocumentStores.Add(clusterInfo, store);

                foreach (var database in _config.Databases ?? Enumerable.Empty<string>())
                    EnsureDatabaseExists(database, store);
            }
        }

        public RoundResults GetRoundResults(string roundStr)
        {
            var rc = new RoundResults();

            if (int.TryParse(roundStr, out rc.Round) == false)
            {
                throw new InvalidDataException("Invalid round number specified");
            }

            using (var session = _reportingDocumentStore.OpenAsyncSession())
            {

                var results = session.Query<TestInfo, FailTests>().Where(x => x.Round == rc.Round, true).ToListAsync().Result;
                var fails = new Dictionary<string, int>();
                var notFinished = new Dictionary<string, int>();

                int k = 0;
                rc.FailTestInfoDetails = new TestInfo[results.Count];
                foreach (var item in results)
                {
                    if (item.Finished == true)
                    {
                        if (fails.ContainsKey(item.Name))
                            fails[item.Name] = fails[item.Name] + 1;
                        else
                            fails[item.Name] = 1;
                        ++rc.TotalFailures;
                    }
                    else
                    {
                        if (notFinished.ContainsKey(item.Name))
                            notFinished[item.Name] = notFinished[item.Name] + 1;
                        else
                            notFinished[item.Name] = 1;
                        ++rc.TotalStillRunning;
                    }
                    rc.FailTestInfoDetails[k] = item;
                    k++;
                }

                rc.TotalTestsInRound = session.Query<TestInfo>().Where(x => x.Author != "TestRunner" && x.Round == rc.Round, true).CountAsync().Result;
                rc.UniqueFailCount = fails.Count;

                var i = 0;
                rc.UniqueTestsDetailsInfo = new UniqueTestsDetails[fails.Count];
                foreach (var item in fails)
                {
                    rc.UniqueTestsDetailsInfo[i] = new UniqueTestsDetails
                    {
                        TestName = item.Key,
                        FailCount = item.Value
                    };
                    ++i;
                }

                rc.RoundStatus = "good"; // green
                if (fails.Count > 0)
                    rc.RoundStatus = "danger"; // red
                else if (rc.TotalTestsInRound == 0 ||
                    notFinished.Count > 1)
                    rc.RoundStatus = "warning"; // yellow                        

                return rc;
            }
        }

        public static OrchestratorConfiguration GetOrchestratorConfigurationCopy(OrchestratorConfiguration config)
        {
            var orchestratorConfig = new OrchestratorConfiguration
            {
                OrchestratorUrl = config.OrchestratorUrl,
                EmbeddedServerUrl = config.EmbeddedServerUrl,
                Databases = new string[config.Databases.Length]
            };

            var i = 0;
            foreach (var db in config.Databases)
                orchestratorConfig.Databases[i++] = db;

            i = 0;
            orchestratorConfig.LocalRavenServers = new ServerInfo[config.LocalRavenServers?.Length ?? 0];
            foreach (var local in config.LocalRavenServers ?? orchestratorConfig.LocalRavenServers)
            {
                orchestratorConfig.LocalRavenServers[i] = new ServerInfo
                {
                    Path = local.Path,
                    Port = local.Port
                };
                orchestratorConfig.LocalRavenServers[i++].Url = local.Url;
            }

            i = 0;
            orchestratorConfig.Clusters = new ClusterInfo[config.Clusters.Length];
            foreach (var clusterInfo in config.Clusters)
            {
                orchestratorConfig.Clusters[i] = new ClusterInfo
                {
                    HasAuthentication = clusterInfo.HasAuthentication,
                    Name = clusterInfo.Name,
                    PfxFilePath = clusterInfo.PfxFilePath
                };
                var j = 0;
                orchestratorConfig.Clusters[i].Urls = new string[clusterInfo.Urls.Length];
                foreach (var url in clusterInfo.Urls)
                    orchestratorConfig.Clusters[i].Urls[j++] = url;
                i++;
            }

            orchestratorConfig.NotifierConfig = new SlackNotifier.NotifierConfig
            {
                Uri = config.NotifierConfig.Uri,
                UserEmail = config.NotifierConfig.UserEmail,
                UserName = config.NotifierConfig.UserName
            };

            return orchestratorConfig;
        }
        


        public bool TrySetConfigSelectorStrategy(string strategyName, string dbIndexStr)
        {
            var strategy = _configSelectorStrategies.FirstOrDefault(x =>
                x.Name.Equals(strategyName, StringComparison.InvariantCultureIgnoreCase));

            if (strategy == null)
                return false;

            if (dbIndexStr != null &&
                dbIndexStr.Equals("") == false && 
                int.TryParse(dbIndexStr, out var dbIndex))
            {
                // re-init strategy with single database
                var newConfig = GetOrchestratorConfigurationCopy(_config);
                newConfig.Databases = new[] {_config.Databases[dbIndex]};
                strategy.Initialize(newConfig);
            }

            _currentConfigSelectorStrategy = strategy;
            return true;
        }

        public ITestConfigSelectorStrategy[] ConfigSelectorStrategies => _configSelectorStrategies;

        public TestConfig RegisterTest(string testName, string testClassName, string author, string round)
        {
            if (int.TryParse(round, out var roundInt) == false)
                roundInt = -1;
            //decide which servers/database the test will get
            if (_currentConfigSelectorStrategy == null)
                throw new InvalidOperationException("Something really bad happened... the config selector strategy appears to be null!");

            var testConfig = _currentConfigSelectorStrategy.GetNextTestConfig();

            using (var session = _reportingDocumentStore.OpenSession(OrchestratorDatabaseName))
            {
                session.Advanced.UseOptimisticConcurrency = true;
                var now = DateTime.UtcNow;
                var testInfo = new TestInfo
                {
                    Name = testName,
                    ExtendedName = $"{testName} ({now})",
                    TestClassName = testClassName,
                    Finished = false,
                    Round = roundInt,
                    Author = author,
                    Start = now,
                    Events = new List<EventInfoWithExceptionAsString>(),
                    Config = testConfig //record what servers we are working with in this particular test
                };

                session.Store(testInfo);
                session.SaveChanges();
            }

            return testConfig;
        }

        //mostly needed to detect if some client is stuck/hang out    
        public void UnregisterTest(string testName, string roundStr)
        {
            TestInfo latestTestInfo = null;
            var sp = Stopwatch.StartNew();
            do
            {
                try
                {
                    var round = int.Parse(roundStr);
                    using (var session = _reportingDocumentStore.OpenSession(OrchestratorDatabaseName))
                    {
                        session.Advanced.UseOptimisticConcurrency = true;
                        var latestTestInfos = session.Query<TestInfo>().Where(x => x.Name == testName && x.Round == round).ToList();

                        var biggest = 0;
                        foreach (var item in latestTestInfos)
                        {
                            var num = int.Parse(item.Id.Split("/")[1].Split("-")[0]);
                            if (num > biggest)
                            {
                                latestTestInfo = item;
                                biggest = num;
                            }
                        }

                        if (latestTestInfo != null)
                        {
                            latestTestInfo.Finished = true;
                            latestTestInfo.End = DateTime.UtcNow;
                            session.Store(latestTestInfo);
                            session.SaveChanges();
                        }
                    }
                    break;
                }
                catch (Exception e)
                {
                    Console.Write($"-*{latestTestInfo?.Id} / {e.Message}*-");
                    if (sp.Elapsed.TotalSeconds > 30)
                        throw;
                }
                Thread.Sleep(3000);
            } while (true);
        }

        public TestInfo GetLastTestByName(string testName, string roundStr)
        {
            var round = int.Parse(roundStr);
            using (var session = _reportingDocumentStore.OpenSession(OrchestratorDatabaseName))
            {
                session.Advanced.UseOptimisticConcurrency = true;
                return session.Query<TestInfo>().OrderByDescending(x => x.Start).FirstOrDefault(x => x.Name == testName && x.Round == round);
            }
        }

        public int GetRound()
        {
            using (var session = _reportingDocumentStore.OpenSession(OrchestratorDatabaseName))
            {
                var doc = session.Load<StaticInfo>("staticInfo/1");
                if (doc == null)
                {
                    var newStaticInfo = new StaticInfo
                    {
                        Round = 1
                    };

                    session.Store(newStaticInfo, "staticInfo/1");
                    session.SaveChanges();
                    return 1;
                }

                return doc.Round;
            }
        }

        public int SetRound(string roundStr)
        {
            var round = int.Parse(roundStr);
            using (var session = _reportingDocumentStore.OpenSession(OrchestratorDatabaseName))
            {
                var newStaticInfo = new StaticInfo
                {
                    Round = round
                };

                session.Store(newStaticInfo, "staticInfo/1");
                session.SaveChanges();
            }
            return round;
        }

        public EventResponse ReportEvent(string testName, string round, EventInfoWithExceptionAsString @event)
        {
            var num = int.Parse(round);
            using (var session = _reportingDocumentStore.OpenSession(OrchestratorDatabaseName))
            {
                session.Advanced.UseOptimisticConcurrency = true;
                var latestTest = session.Query<TestInfo>().Customize(x => x.WaitForNonStaleResults(TimeSpan.FromSeconds(30))).OrderByDescending(x => x.Start).FirstOrDefault(x => x.Name == testName && x.Round == num);
                if (latestTest != null)
                {
                    latestTest.Events.Add(@event);
                    session.SaveChanges();
                }

                //if returning EventResponse.ResponseType.Abort -> opportunity to request the test client to abort test...
                return new EventResponse
                {
                    Type = EventResponse.ResponseType.Ok
                };
            }
        }

        private void EnsureDatabaseExists(string databaseName, IDocumentStore documentStore, bool truncateExisting = false)
        {
            var databaseNames = documentStore.Maintenance.Server.Send(new GetDatabaseNamesOperation(0, int.MaxValue));
            if (truncateExisting && databaseNames.Contains(databaseName))
            {
                //                var result = documentStore.Maintenance.Server.Send(new DeleteDatabasesOperation(databaseName, true));
                //                if (result.PendingDeletes.Length > 0)
                //                {
                //                    using (var ctx = JsonOperationContext.ShortTermSingleUse())
                //                        documentStore.GetRequestExecutor()
                //                            .Execute(new WaitForRaftIndexCommand(result.RaftCommandIndex), ctx);
                //                }
                //
                //                var doc = new DatabaseRecord(databaseName);
                //                documentStore.Maintenance.Server.Send(new CreateDatabaseOperation(doc, documentStore.Urls.Length));

            }
            else if (!databaseNames.Contains(databaseName))
            {
                var doc = new DatabaseRecord(databaseName);
                documentStore.Maintenance.Server.Send(new CreateDatabaseOperation(doc, documentStore.Urls.Length));
            }
        }

        #region Raven Instance Activation Methods

        public void RaiseServer(ServerInfo server)
        {
            var args = new StringBuilder($@" --ServerUrl=http://0.0.0.0:{server.Port}");
            args.Append($@" --PublicServerUrl=http://{server.Url}:{server.Port}");
            args.Append($@" --License.Eula.Accepted=True");
            args.Append($@" --Security.UnsecuredAccessAllowed=PublicNetwork");
            args.Append($@" --Setup.Mode=None");

            var processStartInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(server.Path, "Raven.Server.exe"),
                WorkingDirectory = server.Path,
                Arguments = args.ToString(),
                // CreateNoWindow = true,
                // RedirectStandardOutput = true,
                // RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
            };

            var _ = Process.Start(processStartInfo);

            //string url = null;
            //var outputString = ReadOutput(process.StandardOutput, async (line, builder) =>
            //{
            //    if (line == null)
            //    {
            //        var errorString = ReadOutput(process.StandardError, null);

            //        ShutdownServerProcess(process);

            //        throw new InvalidOperationException($"Failed to RaiseServer {server.Url}");
            //    }

            //    const string prefix = "Server available on: ";
            //    if (line.StartsWith(prefix))
            //    {
            //        url = line.Substring(prefix.Length);
            //        return true;
            //    }

            //    return false;
            //});
        }

        //private static string ReadOutput(StreamReader output, Func<string, StringBuilder, Task<bool>> onLine)
        //{
        //    var sb = new StringBuilder();

        //    var startupDuration = Stopwatch.StartNew();

        //    Task<string> readLineTask = null;
        //    while (true)
        //    {
        //        if (readLineTask == null)
        //            readLineTask = output.ReadLineAsync();

        //        var hasResult = readLineTask.WaitWithTimeout(TimeSpan.FromSeconds(5)).Result;

        //        if (startupDuration.Elapsed > TimeSpan.FromSeconds(30))
        //            return null;

        //        if (hasResult == false)
        //            continue;

        //        var line = readLineTask.Result;

        //        readLineTask = null;

        //        if (line != null)
        //            sb.AppendLine(line);

        //        var shouldStop = false;
        //        if (onLine != null)
        //            shouldStop = onLine(line, sb).Result;

        //        if (shouldStop)
        //            break;

        //        if (line == null)
        //            break;
        //    }

        //    return sb.ToString();
        //}

        //private void ShutdownServerProcess(Process process)
        //{
        //    if (process == null || process.HasExited)
        //        return;

        //    lock (process)
        //    {
        //        if (process.HasExited)
        //            return;

        //        try
        //        {
        //            Console.WriteLine($"Try shutdown server PID {process.Id} gracefully.");

        //            using (var inputStream = process.StandardInput)
        //            {
        //                inputStream.Write($"q{Environment.NewLine}y{Environment.NewLine}");
        //            }

        //            if (process.WaitForExit((int) 30000))
        //                return;
        //        }
        //        catch (Exception e)
        //        {
        //            Console.WriteLine($"Failed to shutdown server PID {process.Id} gracefully in 30Secs", e);
        //        }

        //        try
        //        {
        //            Console.WriteLine($"Killing global server PID {process.Id}.");

        //            process.Kill();
        //        }
        //        catch (Exception e)
        //        {
        //            Console.WriteLine($"Failed to kill process {process.Id}", e);
        //        }
        //    }
        // }        

        #endregion

        public List<TestInfo> GetFailingTests()
        {
            using (var session = _reportingDocumentStore.OpenSession(OrchestratorDatabaseName))
            {
                session.Advanced.UseOptimisticConcurrency = true;
                return session.Query<TestInfo, FailTests>().OrderByDescending(x => x.Start).ToList();
            }
        }
    }
}
