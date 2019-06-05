using Microsoft.Extensions.Configuration;
using Raven.Client.Documents;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net;
using System.Text;
using System.Threading;
using TestingEnvironment.Common.OrchestratorReporting;
using TestingEnvironment.Client;

namespace Notifier
{
    public class Program
    {
        public class MyArgs
        {
            public string RavendbUrl { get; set; }
            public string OrchestratorUrl { get; set; }
            public string ForceUpdate { get; set; }
        }
        public static void Main(string[] args)
        {
            var helpText = @"
Usage: Notifier --ravendbUrl=<url> --orchestratorUrl=<url> [--forceUpdate=force]
";
            var rcArgs = new HandleArgs<MyArgs>().ProcessArgs(args, helpText);
            bool forceUpdate = rcArgs.ForceUpdate != null && rcArgs.ForceUpdate.ToLower().Equals("force");
            if (rcArgs.RavendbUrl == null)
            {
                Console.WriteLine("Must specify --ravendbUrl");
                Console.WriteLine(helpText);
                Environment.Exit(1);
            }
            if (rcArgs.OrchestratorUrl == null)
            {
                Console.WriteLine("Must specify --orchestratorUrl");
                Console.WriteLine(helpText);
                Environment.Exit(1);
            }
            int lastDaySent = DateTime.Now.Day;            
            Console.WriteLine($"Notifier of Embedded Server : {rcArgs.RavendbUrl}");

            while (true)
            {
                try
                {
                    Console.WriteLine();
                    using (var store = new DocumentStore
                    {
                        Urls = new[] { rcArgs.RavendbUrl },
                        Database = "Orchestrator"
                    }.Initialize())
                    {
                        Console.WriteLine($"{DateTime.Now} Read index results...");
                        using (var session = store.OpenAsyncSession())
                        {
                            var roundResult = session.LoadAsync<StaticInfo>("staticInfo/1").Result;
                            var round = roundResult.Round;
                            var copyRound = round;
                            var results = session.Query<TestInfo, TestingEnvironment.Orchestrator.FailTestsComplete>().Where(x => x.Round == copyRound, true).ToListAsync().Result;
                            Console.WriteLine("Total=" + results.Count);
                            Console.WriteLine("Round=" + round);
                            var fails = new Dictionary<string, int>();
                            var notFinished = new Dictionary<string, int>();
                            var totalFailuresCount = 0;
                            var totalNotCompletedCount = 0;

                            foreach (var item in results)
                            {
                                if (item.Finished)
                                {
                                    if (fails.ContainsKey(item.Name))
                                        fails[item.Name] = fails[item.Name] + 1;
                                    else
                                        fails[item.Name] = 1;
                                    ++totalFailuresCount;
                                }
                                else
                                {
                                    if (notFinished.ContainsKey(item.Name))
                                        notFinished[item.Name] = notFinished[item.Name] + 1;
                                    else
                                        notFinished[item.Name] = 1;
                                    ++totalNotCompletedCount;
                                }
                            }

                            var failureText = new StringBuilder();
                            bool first = true;
                            Console.WriteLine();
                            Console.WriteLine("Failed:");
                            Console.WriteLine("======");
                            if (fails.Count > 0)
                            {
                                failureText.Append(@"
                                                    {
                                                        ""title"": ""*_Failures_*"",
                                                        ""value"": ""Unique Tests Count: " + fails.Count + @""",
                                                        ""short"": false
                                                    },
                                ");

                                failureText.Append(@"
                                                {
                                                    ""title"": ""Test Names (FailCount):"",
                                                    ""value"": """);
                                foreach (var kv in fails)
                                {
                                    if (first)
                                        first = false;
                                    else
                                        failureText.Append(", ");

                                    Console.WriteLine($"{ kv.Key} = {kv.Value}");

                                    failureText.Append($"{kv.Key}({kv.Value})");
                                }
                                failureText.Append(@""",  ""short"": true
                                                },
                            ");
                            }

                            var notFinishedText = new StringBuilder();

                            Console.WriteLine();
                            Console.WriteLine("Not Finished:");
                            Console.WriteLine("============ ");
                            first = true;
                            foreach (var kv in notFinished)
                            {
                                if (first)
                                {
                                    notFinishedText.Append(@"
                                                        {
                                                            ""title"": ""*_Not Completed_*"",
                                                            ""value"": ""Unique Tests Count: " + notFinished.Count + @""",
                                                            ""short"": false
                                                        },
                                ");
                                    first = false;
                                }
                                Console.WriteLine($"{kv.Key} = {kv.Value}");
                                notFinishedText.Append(@"
                                                    {
                                                        ""title"": """ + kv.Key + @""",
                                                        ""value"": ""Count: " + kv.Value + @""",
                                                        ""short"": true
                                                    },
                            ");
                            }

                            var copyRound2 = round;
                            var total = session.Query<TestInfo>().Where(x => x.Author != "TestRunner" && x.Round == copyRound2, true).CountAsync().Result;
                            Console.WriteLine($"Out of total={total}");

                            var color = "good"; // green
                            if (fails.Count > 0)
                                color = "danger"; // red
                            else if (total == 0 ||
                                notFinished.Count > 1)
                                color = "warning"; // yellow                        

                            var builder = new ConfigurationBuilder()
                                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                                .SetBasePath(Environment.CurrentDirectory);
                            var config = builder.Build();
                            var appConfig = new NotifierConfig();
                            ConfigurationBinder.Bind(config, appConfig);

                            string msgstring = @"
                                            {   
                                                ""Username"": """ + appConfig.UserEmail + @""",
                                                ""Channel"": """ + appConfig.UserName + @""",
                                                ""attachments"": [
                                                    {
                                                        ""mrkdwn_in"": [""text""],
                                                        ""color"": """ + color + @""",
                                                        ""pretext"": ""Testing Environment Results"",
                                                        ""author_name"": ""Round " + round + @" (Click to view)"",
                                                        ""author_link"": """ + rcArgs.OrchestratorUrl + @"/round-results?round=" + round + @""",
                                                        ""author_icon"": ""https://ravendb.net/img/team/adi_avivi.jpg"",
                                                        ""title"": ""Total Tests: " + total + @" | Total Failures: " + totalFailuresCount + @" | Still Running: " + totalNotCompletedCount + @""",                                                        
                                                        ""text"": ""<" + rcArgs.RavendbUrl + @"/studio/index.html#databases/query/index/FailTestsComplete?&database=Orchestrator|RavenDB Studio> - See all rounds errors\n"",
                                                        ""fields"": [
                                                                    " + /*notFinishedText.ToString()*/ "" + @"
                                                            " + failureText + @"                        
                                                        ],                        
                                                        ""thumb_url"": ""https://ravendb.net/img/home/raven.png"",
                                                        ""footer"": ""The results were generated at " + DateTime.Now + @""",
                                                        ""footer_icon"": ""https://platform.slack-edge.com/img/default_application_icon.png""                                                        
                                                    }
                                                ]
                                            }
                                            ";


                            var now = DateTime.Now;
                            if (forceUpdate || (now.Hour >= 9 &&
                                now.Day != lastDaySent))
                            {
                                Console.WriteLine();
                                Console.WriteLine("Sending:");
                                Console.WriteLine(msgstring);

                                lastDaySent = now.Day;
                                using (WebClient client = new WebClient())
                                {
                                    NameValueCollection data = new NameValueCollection
                                    {
                                        ["payload"] = msgstring
                                    };
                                    var response = client.UploadValues(appConfig.Uri, "POST", data);

                                    //The response text is usually "ok"
                                    string responseText = Encoding.UTF8.GetString(response);
                                    Console.WriteLine();
                                    Console.WriteLine(responseText);
                                    Console.WriteLine("Done");
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("ERROR:");
                    Console.WriteLine(e);
                }
                Thread.Sleep(TimeSpan.FromHours(1));
            }
            // ReSharper disable once FunctionNeverReturns
        }
    }
}
