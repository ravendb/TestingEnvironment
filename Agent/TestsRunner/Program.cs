using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Raven.Client.Documents;
using TestingEnvironment.Client;
using TestingEnvironment.Common.OrchestratorReporting;

namespace TestsRunner
{
    public class StrategySet : BaseTest
    {
        public int Round;
        public string DbIndex;
        public string DocId; // staticInfo/??.. 
        public string RavendbVersion;
        public bool Archive;

        public StrategySet(string ravendbVersion, string docid, string orchestratorUrl, string testName, int round, string dbIndex, string testid, bool archive) : base(orchestratorUrl, testName, "TestRunner", round, testid)
        {
            Round = round;
            DbIndex = dbIndex;
            DocId = docid;
            RavendbVersion = ravendbVersion ?? "N/A";
            Archive = archive;
        }

        public override void RunActualTest()
        {
            var success = SetStrategy("FirstClusterRandomDatabaseSelector", DbIndex);
            Round = SetRound(DocId, Round, RavendbVersion, Archive);
            ReportInfo($"Round set to {DocId} :: {Round}");
            if (success)
                ReportSuccess("Finished successfully");
            else
                ReportFailure("Finished with failures", null);        
        }
    }

    public class Program
    {
        public static void Main(string[] args)
        {
            var helpText =
                @"Usage: TestsRunner --orchestratorUrl=<url> [OPTION =<value>]...
                                                                                     
                 OPTIONS:                                                            
                     --orchestratorUrl=<url>                                         
                         Orchestrator listening url (as defined in orchestrator      
                         appsettings.json/OrchestratorUrl).    
                    --ravendbPort=<port>
                         Embedded RavenDB url in orchestrator. Default is 8090
                    --runnerId=<id>
                         Round number document name (must be unique if multiple TestsRunner instances are running)
                         Default is 'staticInfo/1' - used by SlackNotifier
                    --stdOut=<filepath>                                     
                         Redirect output to a file. If option not specified, StdOut  
                         will be used.                                               
                    --round=<round number>                                          
                         Set or increase round number. If not set, no change done.   
                         Round number above 0 overrides current round number,        
                         setting to 0 will increase current round by one.     
                    --dbIndex=<dbIndex>
                         Optionally for FirstClusterStrategy, select specific database from config file
                    --cleanLastRunning=<round>
                         Delete not finished entries on a specific round.
                         Setting round to 0 will use current round.
                         Utility will exit upon completing deletion.
                    --setAllArchived=set
                         Set all tests in round as archived before starting the round.
                         The archived tests will not appear in indexes.
                    --ravendbVersion=<string>
                         Tested version tag.
                         If staticInfo/1 chosen as runnerId, it will be displayed in slack message.
                    --excludeTests=<string, ...>
                         Comma separated (insensitive case) test names to exclude.
";

            var defaults = new TestRunnerArgs { Round = "-1" };
            var options = new HandleArgs<TestRunnerArgs>().ProcessArgs(args, helpText, defaults);
            TextWriter stdOut = options.StdOut == null ? Console.Out : File.CreateText(options.StdOut);
            if (options.OrchestratorUrl == null)
            {
                Console.WriteLine("--orchestratorUrl must be specified");
                Console.WriteLine();
                Console.WriteLine(helpText);
                Environment.Exit(1);
            }

            if (options.RavendbPort == null)
                options.RavendbPort = "8090";

            var ravenurls = options.OrchestratorUrl.Split(":");
            if (ravenurls.Length != 3)
            {
                Console.WriteLine("orchestrator url must consist port number");
                Console.WriteLine();
                Environment.Exit(1);
            }

            var ravenurl = $"{ravenurls[0]}:{ravenurls[1]}:{options.RavendbPort}";

            if (options.CleanLastRunning != null)
            {
                if (int.TryParse(options.CleanLastRunning, out var cleanInRound) == false)
                {
                    Console.WriteLine("--cleanLastRunning must have a valid round value");
                    Console.WriteLine();
                    Console.WriteLine(helpText);
                    Environment.Exit(1);
                }

                using (var cleanInstance = new CustomOrchestratorCommands.CustomOrchestratorCommands(
                    options.OrchestratorUrl, "CleanLastRound", int.Parse(options.Round), Guid.NewGuid().ToString()))
                {
                    cleanInstance.Cmd = TestingEnvironment.Common.Command.RemoveLastRunningTestInfo;
                    cleanInstance.CmdData = cleanInRound.ToString();
                    cleanInstance.Initialize();
                    cleanInstance.RunActualTest();
                }

                Console.WriteLine($"CleanLastRound sent for round {cleanInRound}");
                Environment.Exit(1);
            }            

            if (options.RunnerId == null)
                options.RunnerId = "staticInfo/1";

            var excludeTests = new HashSet<string>();

            try
            {

                stdOut.WriteLine("Tests Runner v4.2");
                stdOut.WriteLine("=================");
                stdOut.WriteLine();
                stdOut.WriteLine(
                    $"OS / Framework: {RuntimeInformation.OSDescription} / {RuntimeInformation.FrameworkDescription}");
                stdOut.WriteLine(
                    $"Arch / Process / Cores: {Environment.ProcessorCount} / {RuntimeInformation.OSArchitecture} / {RuntimeInformation.ProcessArchitecture}");
                stdOut.WriteLine();
                stdOut.WriteLine($"OrchestratorUrl: {options.OrchestratorUrl}");
                stdOut.WriteLine($"Round: {options.Round}");
                stdOut.WriteLine($"DbIndex: {options.DbIndex}");
                stdOut.WriteLine();
                stdOut.Flush();

                stdOut.WriteLine("Setting Strategy: FirstClusterRandomDatabaseStrategy");
                var archive = false;
                if (options.SetAllArchived != null && options.SetAllArchived.ToLower().Equals("set"))
                {
                    Console.WriteLine($"Also archiving all old tests in {options.Round}");
                    archive = true;
                }
                int roundResult;
                using (var client = new StrategySet(options.RavendbVersion, options.RunnerId, options.OrchestratorUrl, "StrategySet", int.Parse(options.Round), options.DbIndex, Guid.NewGuid().ToString(), archive))
                {
                    client.Initialize();
                    client.RunTest();
                    roundResult = client.Round;
                }

                stdOut.Flush();

                Console.WriteLine("Setting round: " + options.Round);


                stdOut.WriteLine("Loading Tests...");

                var tests = new[]
                {
                    typeof(CleanerTask.CleanerTask),
                    typeof(BlogComment.Program.PutCommentsTest),
                    typeof(Counters.PutCommentsTest),
                    typeof(Counters.PutCountersOnCommentsBasedOnTopic),
                    typeof(Counters.PutCountersOnCommentsRandomly),
                    typeof(Counters.QueryBlogCommentsByTag),
                    typeof(AuthorizationBundle.HospitalTest),
                    typeof(CorruptedCasino.Casino),
                    typeof(Counters.PatchCommentRatingsBasedOnCounters),
                    typeof(Counters.QueryBlogCommentsAndIncludeCounters),
                    typeof(BackupTaskCleaner.BackupTaskCleaner),
                    typeof(Counters.LoadBlogCommentsAndIncludeCounters),
                    typeof(Counters.IncrementCountersByPatch),
                    typeof(Counters.SubscribeToCounterChanges),
                    typeof(Counters.IndexQueryOnCounterNames),
                    typeof(Counters.CounterRevisions),
                    typeof(MarineResearch.MarineResearchTest),
                    typeof(Subscriptions.FilterAndProjection),
                    typeof(BackupAndRestore.BackupAndRestore)
                };

                if (options.ExcludeTests != null)
                {
                    var testsToExclude = options.ExcludeTests.Split(",");
                    foreach (var specifiedTestToExclude in testsToExclude)
                    {
                        foreach (var test in tests)
                        {
                            if (test.Name.Equals(specifiedTestToExclude))
                            {
                                excludeTests.Add(test.Name);
                                Console.WriteLine($"Excluding test: {test.Name}");
                            }
                        }
                    }
                }

                var ctorTypes = new[] { typeof(string), typeof(string), typeof(int), typeof(string) };
                stdOut.WriteLine();

                stdOut.WriteLine();
                stdOut.WriteLine("Running Tests:");
                var spBackup = Stopwatch.StartNew();
                var spCleanup = Stopwatch.StartNew();
                int num = 1;
                while (true)
                {
                    var testsList = new List<BaseTest>();
                    foreach (var test in tests)
                    {
                        if (test.Name.Equals("CleanerTask"))
                        {
                            if (spCleanup.Elapsed > TimeSpan.FromDays(1))
                                spCleanup.Restart();
                            else
                                continue;
                        }

                        switch (test.Name)
                        {
                            case "BackupTaskCleaner":
                            case "BackupAndRestore":
                                {
                                    if (spBackup.Elapsed > TimeSpan.FromHours(1))
                                        spBackup.Restart();
                                    else
                                        continue;
                                }
                                break;
                        }

                        if (test.Name.Equals("BackupAndRestore") &&
                            (Environment.GetEnvironmentVariable("RAVEN_CLOUD_MACHINE") ?? "N").Contains("Y", StringComparison.InvariantCultureIgnoreCase))
                            continue;


                        if (excludeTests.Contains(test.Name))
                            continue;

                        if (tests[0] != test)
                            stdOut.Write(", ");
                        var testName = test.Name;
                        var testid = Guid.NewGuid().ToString();
                        var testclass = test.GetConstructor(ctorTypes);
                        stdOut.Write(testName);
                        var instance = (BaseTest)testclass.Invoke(new object[]
                            {options.OrchestratorUrl, testName, roundResult, testid});
                        if (instance == null)
                        {
                            stdOut.WriteLine($"Internal Error: no appropriate Ctor for {testName}");
                            Environment.Exit(1);
                        }

                        testsList.Add(instance);
                    }

                    Console.WriteLine();

                    foreach (var test in testsList)
                    {
                        var testDisposed = false;
                        try
                        {
                            using (var store = new DocumentStore
                            {
                                Urls = new[] { ravenurl },
                                Database = "Orchestrator"
                            }.Initialize())
                            {
                                var sp = Stopwatch.StartNew();
                                stdOut.Write($"{num++}: {DateTime.Now} {test.TestName}: Initialize...");
                                test.Initialize();

                                using (var session = store.OpenSession())
                                {
                                    // search if test is running in parallel in other rounds:
                                    var runningTests = session
                                        .Query<TestInfo, FailTests>()
                                        .Where(x => x.Name == test.TestName && x.Finished == false, false)
                                        .Customize(y => y.WaitForNonStaleResults(TimeSpan.FromSeconds(15)))
                                        .ToList();
                                    if (runningTests.Count > 1)
                                    {
                                        stdOut.WriteLine(
                                            $" Skipping.. There are {runningTests.Count - 1} other instances of this test running in parallel");
                                        test.CancelTest(); // dispose must be called now
                                        stdOut.Write($" Dispose...");
                                        testDisposed = true;
                                        test.Dispose();
                                        continue;
                                    }
                                }

                                stdOut.Write($" RunTest...");
                                test.RunTest();
                                stdOut.Write($" Dispose...");
                                testDisposed = true;
                                test.Dispose();
                                stdOut.Write($" Done @ {sp.Elapsed}");


                                using (var asyncSession = store.OpenAsyncSession())
                                {
                                    stdOut.Write(" Reading index results...");
                                    var roundResults = asyncSession.LoadAsync<StaticInfo>($"{options.RunnerId}")
                                        .Result;
                                    var round = roundResults.Round;
                                    var copyRound = round;
                                    var results = asyncSession
                                        .Query<TestInfo, FailTests>()
                                        .Where(x => x.Round == copyRound, false)
                                        .Customize(y => y.WaitForNonStaleResults(TimeSpan.FromSeconds(15)))
                                        .ToListAsync().Result;
                                    stdOut.WriteLine($" Total={results.Count} / Round={round}");
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            stdOut.WriteLine(Environment.NewLine + "Exception in Test: " + e);
                            if (testDisposed == false)
                            {
                                try
                                {
                                    test.Dispose();
                                }
                                catch (Exception ex)
                                {
                                    stdOut.WriteLine("Unable to dispose test after exception: " + ex);
                                }
                            }
                        }
                    }

                    stdOut.WriteLine();
                }
            }
            finally
            {
                stdOut.Flush();
                stdOut.Dispose();

                Console.WriteLine("Press any key..");
                Console.ReadKey();
            }
        }
    }


    public class TestRunnerArgs
    {
        public string RavendbVersion { get; set; }        
        public string OrchestratorUrl { get; set; }
        public string RavendbPort { get; set; }
        public string RunnerId { get; set; }
        public string Round { get; set; }
        public string StdOut { get; set; }
        public string DbIndex { get; set; }
        public string CleanLastRunning { get; set; }
        public string ExcludeTests { get; set; }
        public string SetAllArchived { get; set; }
    }
}
