using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using TestingEnvironment.Client;

namespace TestsRunner
{
    public class Program
    {
        public static void Main(string[] args)
        {
            (var stdOut, var orchestratorUrl) = HandleArgs(args);

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
                stdOut.Write("Loading Tests: ");

                var tests = new Type[]
                {
                   typeof(AuthorizationBundle.HospitalTest),
                   typeof(BlogComment.Program.PutCommentsTest),
                   typeof(CorruptedCasino.Casino),
                   typeof(Counters.PutCommentsTest),
                   typeof(Counters.PutCountersOnCommentsBasedOnTopic),
                   typeof(Counters.PutCountersOnCommentsRandomly),
                   typeof(Counters.QueryBlogCommentsByTag),
                   typeof(Counters.PatchCommentRatingsBasedOnCounters),
                   typeof(Counters.QueryBlogCommentsAndIncludeCounters),
                   typeof(Counters.LoadBlogCommentsAndIncludeCounters),
                   typeof(Counters.IncrementCountersByPatch),
                   typeof(Counters.SubscribeToCounterChanges),
                   typeof(Counters.IndexQueryOnCounterNames),
                   typeof(Counters.CounterRevisions),
                   typeof(MarineResearch.MarineResearchTest),
                   // typeof(Subscriptions.FilterAndProjection),
                   // typeof(BackupAndRestore.BackupAndRestore)
                };

                var ctorTypes = new Type[] { typeof(string), typeof(string) };
                var testsList = new List<BaseTest>();
                foreach (var test in tests)
                {
                    if (tests[0] != test)
                        stdOut.Write(", ");
                    var testName = test.Name;
                    var testclass = test.GetConstructor(ctorTypes);
                    stdOut.Write(testName);
                    var instance = (BaseTest)testclass.Invoke(new object[] { orchestratorUrl, testName });
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
                            Console.WriteLine(Environment.NewLine + "Exception in Test: " + e);
                            if (testDisposed == false)
                            {
                                try
                                {
                                    test.Dispose();
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine("Unable to dispose test after exception: " + e);
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

        private static (TextWriter stdOut, string orchestratorUrl) HandleArgs(string[] args)
        {
            string orchestratorUrl = null;
            string filepath = null;

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
                        Console.WriteLine();
                        Environment.Exit(0);
                        break;
                    case "--orchestratorUrl":
                    case "-o":
                        orchestratorUrl = kv[1];
                        break;
                    case "--output-to-file":
                    case "-f":
                        filepath = kv[1];
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
            return (textWriter, orchestratorUrl);
        }
    }
}
