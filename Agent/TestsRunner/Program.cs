using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using TestingEnvironment.Client;

namespace TestsRunner
{
    public class StrategySet : BaseTest
    {
        public int Round;
        public StrategySet(string orchestratorUrl, string testName, int round) : base(orchestratorUrl, testName, "TestRunner", round)
        {
            Round = round;
        }

        public override void RunActualTest()
        {
            var success = SetStrategy("FirstClusterRandomDatabaseSelector");
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
                                                                                     
                     --output-to-file=<filepath>                                     
                         Redirect output to a file. If option not specified, StdOut  
                         will be used.                                               
                     --round=<round number>                                          
                         Set or increase round number. If not set, no change done.   
                         Round number above 0 overrides current round number,        
                         setting to 0 will increase current round by one.             ";

            var options = new HandleArgs<TestRunnerArgs>().ProcessArgs(args, helpText);
            TextWriter stdOut = options.StdOut == null ? Console.Out : File.CreateText(options.StdOut);
            if (options.OrchestratorUrl == null)
            {
                Console.WriteLine("--orchestratorUrl must be specified");
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
                stdOut.WriteLine();
                stdOut.Flush();

                stdOut.WriteLine("Setting Strategy: FirstClusterRandomDatabaseStrategy");
                int roundResult;
                using (var client = new StrategySet(options.OrchestratorUrl, "StrategySet", options.Round))
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
                while (true)
                {
                    foreach (var test in testsList)
                    {
                        var testDisposed = false;
                        try
                        {
                            var sp = Stopwatch.StartNew();
                            stdOut.Write($"{DateTime.Now} {test.TestName}:{Environment.NewLine}Initialize...");
                            test.Initialize();
                            stdOut.Write($" RunTest...");
                            test.RunTest();
                            stdOut.Write($" Dispose...");
                            testDisposed = true;
                            test.Dispose();
                            stdOut.WriteLine($" Done @ {sp.Elapsed}");
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
        public string OrchestratorUrl;
        public int Round = -1;
        public string StdOut;
    }
}
