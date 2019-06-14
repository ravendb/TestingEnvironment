using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

        public StrategySet(string orchestratorUrl, string testName, int round, string dbIndex) : base(orchestratorUrl, testName, "TestRunner", round)
        {
            Round = round;
            DbIndex = dbIndex;
        }

        public override void RunActualTest()
        {
            var success = SetStrategy("FirstClusterRandomDatabaseSelector", DbIndex);            
            Round = SetRound(Round);
            ReportInfo($"Round set to {Round}");
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

                    --ravendbUrl=<url>
                         Embedded RavenDB url in orchestrator
                                                                                     
                     --output-to-file=<filepath>                                     
                         Redirect output to a file. If option not specified, StdOut  
                         will be used.                                               
                     --round=<round number>                                          
                         Set or increase round number. If not set, no change done.   
                         Round number above 0 overrides current round number,        
                         setting to 0 will increase current round by one.     
                    --dbIndex=<dbIndex>
                         Optionally for FirstClusterStrategy, select specific database from config file
";

            var defaults = new TestRunnerArgs {Round = "-1" };
            var options = new HandleArgs<TestRunnerArgs>().ProcessArgs(args, helpText, defaults);
            TextWriter stdOut = options.StdOut == null ? Console.Out : File.CreateText(options.StdOut);
            if (options.OrchestratorUrl == null || options.RavendbUrl == null)
            {
                Console.WriteLine("--orchestratorUrl and --ravendbUrl must be specified");
                Console.WriteLine();
                Console.WriteLine(helpText);
                Environment.Exit(1);
            }

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
                stdOut.WriteLine($"OrcestratorUrl: {options.OrchestratorUrl}");
                stdOut.WriteLine($"Round: {options.Round}");
                stdOut.WriteLine($"DbIndex: {options.DbIndex}");
                stdOut.WriteLine();
                stdOut.Flush();

                stdOut.WriteLine("Setting Strategy: FirstClusterRandomDatabaseStrategy");
                int roundResult;
                using (var client = new StrategySet(options.OrchestratorUrl, "StrategySet", int.Parse(options.Round), options.DbIndex))
                {
                    client.Initialize();
                    client.RunTest();
                    roundResult = client.Round;
                }

                stdOut.Flush();

                Console.WriteLine("Setting round: " + options.Round);


                stdOut.Write("Loading Tests: ");

                var tests = new[]
                {
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
                    // typeof(Subscriptions.FilterAndProjection),
                    typeof(BackupAndRestore.BackupAndRestore)
                };

                var ctorTypes = new[] {typeof(string), typeof(string), typeof(int)};
                var testsList = new List<BaseTest>();
                foreach (var test in tests)
                {
                    if (tests[0] != test)
                        stdOut.Write(", ");
                    var testName = test.Name;
                    var testclass = test.GetConstructor(ctorTypes);
                    stdOut.Write(testName);
                    var instance = (BaseTest) testclass.Invoke(new object[]
                        {options.OrchestratorUrl, testName, roundResult});
                    if (instance == null)
                    {
                        stdOut.WriteLine($"Internal Error: no appropriate Ctor for {testName}");
                        Environment.Exit(1);
                    }

                    testsList.Add(instance);
                }

                stdOut.WriteLine();

                stdOut.WriteLine();
                stdOut.WriteLine("Runing Tests:");
                int num = 1;
                while (true)
                {
                    foreach (var test in testsList)
                    {
                        var testDisposed = false;
                        try
                        {
                            var sp = Stopwatch.StartNew();
                            stdOut.Write($"{num++}: {DateTime.Now} {test.TestName}: Initialize...");
                            test.Initialize();
                            stdOut.Write($" RunTest...");
                            test.RunTest();
                            stdOut.Write($" Dispose...");
                            testDisposed = true;
                            test.Dispose();
                            stdOut.Write($" Done @ {sp.Elapsed}");
                            using (var store = new DocumentStore
                            {
                                Urls = new[] {options.RavendbUrl},
                                Database = "Orchestrator"
                            }.Initialize())
                            {                                
                                using (var session = store.OpenAsyncSession())
                                {
                                    stdOut.Write(" Reading index results...");
                                    var roundResults = session.LoadAsync<StaticInfo>("staticInfo/1").Result;
                                    var round = roundResults.Round;
                                    var copyRound = round;
                                    var results = session
                                        .Query<TestInfo, FailTests>()
                                        .Where(x => x.Round == copyRound, true)
                                        .Customize(y => y.WaitForNonStaleResults(TimeSpan.FromSeconds(15)))
                                        .ToListAsync().Result;
                                    Console.WriteLine($" Total={results.Count} / Round={round}");
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
        public string OrchestratorUrl { get; set; }
        public string RavendbUrl { get; set; }
        public string Round { get; set; }
        public string StdOut  { get; set; }
        public string DbIndex { get; set; }
    }
}
