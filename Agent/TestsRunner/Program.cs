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
        public int _round;
        public StrategySet(string orchestratorUrl, string testName, int round) : base(orchestratorUrl, testName, "TestRunner", round)
        {
            _round = round;
        }

        public override void RunActualTest()
        {
            var success = SetStrategy("FirstClusterRandomDatabaseSelector");
            _round = SetRound(_round);
            ReportInfo($"Round set to {_round}");
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
            (var stdOut, var orchestratorUrl, var round) = HandleArgs(args);

            try
            {

                stdOut.WriteLine("Tests Runner v4.2");
                stdOut.WriteLine("=================");
                stdOut.WriteLine();
                stdOut.WriteLine($"OS / Framework: {RuntimeInformation.OSDescription} / {RuntimeInformation.FrameworkDescription}");
                stdOut.WriteLine($"Arch / Process / Cores: {Environment.ProcessorCount} / {RuntimeInformation.OSArchitecture} / {RuntimeInformation.ProcessArchitecture}");
                stdOut.WriteLine();
                stdOut.WriteLine($"OrcestratorUrl: {orchestratorUrl}");
                stdOut.WriteLine();
                stdOut.Flush();

                stdOut.WriteLine("Setting Strategy: FirstClusterRandomDatabaseStrategy");
                int roundResult = -1;
                using (var client = new StrategySet(orchestratorUrl, "StrategySet", round))
                {
                    client.Initialize();
                    client.RunTest();
                    roundResult = client._round;
                }
                stdOut.Flush();

                Console.WriteLine("Setting round: " + round);


                stdOut.Write("Loading Tests: ");

                var tests = new Type[]
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

                var ctorTypes = new Type[] { typeof(string), typeof(string), typeof(int) };
                var testsList = new List<BaseTest>();
                foreach (var test in tests)
                {
                    if (tests[0] != test)
                        stdOut.Write(", ");
                    var testName = test.Name;
                    var testclass = test.GetConstructor(ctorTypes);
                    stdOut.Write(testName);
                    var instance = (BaseTest)testclass.Invoke(new object[] { orchestratorUrl, testName, roundResult });
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

        private static (TextWriter stdOut, string orchestratorUrl, int round) HandleArgs(string[] args)
        {
            string orchestratorUrl = null;
            string filepath = null;
            var round = -1;


            for (int i = 0; i < args.Length; i++)
            {
                if (args.Length == 1 && (
                    args[0].ToLower().Equals("-h") ||
                    args[0].ToLower().Equals("--help")))
                {
                    args[0] = "--help=help";
                }

                var kv = args[i]?.Split('=');
                if (kv == null || kv.Length != 2)
                {
                    Console.WriteLine("Invalid arguments");
                    Environment.Exit(1);
                }

                kv[0] = kv[0].ToLower();
                switch (kv[0])
                {
                    case "--help":
                    case "-h":
                        Console.WriteLine();
                        Console.WriteLine("Usage: TestsRunner --orchestratorUrl=<url> [ OPTION=<value ] ...      ");
                        Console.WriteLine("                                                                      ");
                        Console.WriteLine("    OPTIONS:                                                          ");
                        Console.WriteLine("        --orchestratorUrl=<url>                                       ");
                        Console.WriteLine("        -o=<url>                                                      ");
                        Console.WriteLine("            Orchestrator listening url (as defined in orchestrator    ");
                        Console.WriteLine("            appsettings.json/OrchestratorUrl).                        ");
                        Console.WriteLine("                                                                      ");
                        Console.WriteLine("        --output-to-file=<filepath>                                   ");
                        Console.WriteLine("        -f=<filepath>                                                 ");
                        Console.WriteLine("            Redirect output to a file. If option not specified, StdOut");
                        Console.WriteLine("            will be used.                                             ");
                        Console.WriteLine("        --round=<round number>                                        ");
                        Console.WriteLine("        -r=<round number>                                             ");
                        Console.WriteLine("            Set or increase round number. If not set, no change done. ");
                        Console.WriteLine("            Round number above 0 overrides current round number,      ");
                        Console.WriteLine("            setting to 0 will increase current round by one.          ");                        
                        Console.WriteLine();
                        Environment.Exit(0);
                        break;
                    case "--orchestratorurl":
                    case "-o":
                        orchestratorUrl = kv[1];
                        break;
                    case "--output-to-file":
                    case "-f":
                        filepath = kv[1];
                        break;
                    case "--round":
                    case "-r":
                        if (int.TryParse(kv[1], out round) == false)
                        {
                            Console.WriteLine("Invalid Arguments (round is not a number)");
                        }
                        break;
                    default:
                        Console.WriteLine("Invalid Arguments");
                        Environment.Exit(1);
                        break;
                }
            }

            if (orchestratorUrl == null)
            {
                Console.WriteLine("Invalid Arguments: --orchestratorUrl must be specified");
                Environment.Exit(1);
            }

            TextWriter textWriter = filepath == null ? Console.Out : File.CreateText(filepath);
            return (textWriter, orchestratorUrl, round);
        }
    }
}
