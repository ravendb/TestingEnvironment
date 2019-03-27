using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
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
                .AddJsonFile("appsettings.json", optional:false, reloadOnChange:true)
                .Build();
            
            _config = new OrchestratorConfiguration();
            ConfigurationBinder.Bind(configProvider, _config);

            if (_config.Databases?.Length == 0)
            {
                throw new InvalidOperationException("Must be at least one database configured!");
            }

            foreach (var serverInfo in _config.LocalRavenServers ?? Enumerable.Empty<ServerInfo>())
            {
                RaiseServer(serverInfo);
            }
            
            EmbeddedServer.Instance.StartServer(new ServerOptions
            {  
                ServerUrl = "http://127.0.0.1:8090"
            });
            _reportingDocumentStore = EmbeddedServer.Instance.GetDocumentStore(new DatabaseOptions(OrchestratorDatabaseName));
            _reportingDocumentStore.Initialize();
            new LatestTestByName().Execute(_reportingDocumentStore);

            if (_config.Clusters == null || _config.Clusters.Length == 0)
            {
                throw new InvalidOperationException("Must be at least one RavenDB cluster info configured!");
            }

            _container.Register(Classes.FromAssembly(typeof(Orchestrator).Assembly)
                .BasedOn<ITestConfigSelectorStrategy>()
                .WithServiceAllInterfaces()
                .LifestyleSingleton());

            _configSelectorStrategies = _container.ResolveAll<ITestConfigSelectorStrategy>();
            if(_configSelectorStrategies.Length == 0)
                throw new InvalidOperationException("Something really bad happened... there is no config selector strategies implemented!");

            foreach(var strategy in _configSelectorStrategies)
                strategy.Initialize(_config);

            //TODO: make this choice persistent? (via the embedded RavenDB instance)
            _currentConfigSelectorStrategy = _configSelectorStrategies[0];

            foreach (var clusterInfo in _config.Clusters ?? Enumerable.Empty<ClusterInfo>())
            {
                clusterInfo.Urls = clusterInfo.Urls.Select(PrepareUrlForDocumentStore).ToArray();

                var store = new DocumentStore
                {
                    Database = _config.Databases?[0],
                    Urls = clusterInfo.Urls,
                    //Certificate =  TODO: finish this
                };
                store.Initialize();
                _clusterDocumentStores.Add(clusterInfo, store);

                foreach(var database in _config.Databases ?? Enumerable.Empty<string>())
                    EnsureDatabaseExists(database, store);
            }
        }

        private static string PrepareUrlForDocumentStore(string url)
        {
            return $"http://{url.Replace("http://", string.Empty).Replace("https://", string.Empty)}";
        }

        public bool TrySetConfigSelectorStrategy(string strategyName)
        {
            var strategy = _configSelectorStrategies.FirstOrDefault(x =>
                x.Name.Equals(strategyName, StringComparison.InvariantCultureIgnoreCase));

            if (strategy == null)
                return false;

            _currentConfigSelectorStrategy = strategy;
            return true;
        }

        public ITestConfigSelectorStrategy[] ConfigSelectorStrategies => _configSelectorStrategies;

        public TestConfig RegisterTest(string testName, string testClassName, string author)
        {
            //decide which servers/database the test will get
            if(_currentConfigSelectorStrategy == null)
                throw new InvalidOperationException("Something really bad happened... the config selector strategy appears to be null!");

            var testConfig = _currentConfigSelectorStrategy.GetNextTestConfig();

            using (var session = _reportingDocumentStore.OpenSession(OrchestratorDatabaseName))
            {
                var now = DateTime.UtcNow;
                session.Store(new TestInfo
                {
                    Name = testName,
                    ExtendedName = $"{testName} ({now})",
                    TestClassName = testClassName,
                    Author = author,
                    Start = now,
                    Events = new List<EventInfo>(),
                    Config = testConfig //record what servers we are working with in this particular test
                });
                session.SaveChanges();
            }

            return testConfig;
        }

        //mostly needed to detect if some client is stuck/hang out    
        public void UnregisterTest(string testName)
        {
            using (var session = _reportingDocumentStore.OpenSession(OrchestratorDatabaseName))
            {
                var latestTestInfo = session.Query<TestInfo, LatestTestByName>().FirstOrDefault(x => x.Name == testName);
                if (latestTestInfo != null)
                {
                    latestTestInfo.End = DateTime.UtcNow;
                    session.Store(latestTestInfo);
                    session.SaveChanges();
                }
            }
        }

        public TestInfo GetLastTestByName(string testName)
        {
            using (var session = _reportingDocumentStore.OpenSession(OrchestratorDatabaseName))
            {
                return session.Query<TestInfo>().OrderByDescending(x => x.Start).FirstOrDefault(x => x.Name == testName);;
            }
        }

        public EventResponse ReportEvent(string testName, EventInfo @event)
        {
            using (var session = _reportingDocumentStore.OpenSession(OrchestratorDatabaseName))
            {
                var latestTest = session.Query<TestInfo>().OrderByDescending(x => x.Start).FirstOrDefault(x => x.Name == testName);
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
            
            var process = Process.Start(processStartInfo);

            string url = null;
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

        private static string ReadOutput(StreamReader output, Func<string, StringBuilder, Task<bool>> onLine)
        {
            var sb = new StringBuilder();

            var startupDuration = Stopwatch.StartNew();

            Task<string> readLineTask = null;
            while (true)
            {
                if (readLineTask == null)
                    readLineTask = output.ReadLineAsync();

                var hasResult = readLineTask.WaitWithTimeout(TimeSpan.FromSeconds(5)).Result;

                if (startupDuration.Elapsed > TimeSpan.FromSeconds(30))
                    return null;

                if (hasResult == false)
                    continue;

                var line = readLineTask.Result;

                readLineTask = null;

                if (line != null)
                    sb.AppendLine(line);

                var shouldStop = false;
                if (onLine != null)
                    shouldStop = onLine(line, sb).Result;

                if (shouldStop)
                    break;

                if (line == null)
                    break;
            }

            return sb.ToString();
        }

        private void ShutdownServerProcess(Process process)
        {
            if (process == null || process.HasExited)
                return;

            lock (process)
            {
                if (process.HasExited)
                    return;

                try
                {
                    Console.WriteLine($"Try shutdown server PID {process.Id} gracefully.");

                    using (var inputStream = process.StandardInput)
                    {
                        inputStream.Write($"q{Environment.NewLine}y{Environment.NewLine}");
                    }

                    if (process.WaitForExit((int) 30000))
                        return;
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Failed to shutdown server PID {process.Id} gracefully in 30Secs", e);
                }

                try
                {
                    Console.WriteLine($"Killing global server PID {process.Id}.");

                    process.Kill();
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Failed to kill process {process.Id}", e);
                }
            }
}        

        #endregion        
    }
}
