using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Raven.Client.Documents;
using TestingEnvironment.Client;
using TestingEnvironment.Common.OrchestratorReporting;

namespace TestingEnvironment.Orchestrator
{
    public class SlackNotifier
    {
        public  class NotifierConfig
        {
            public string UserEmail { get; set; }
            public string UserName { get; set; }
            public string Uri { get; set; }
        }

        public class NotifierArgs
        {
            public string RavendbUrl { get; set; }
            public string OrchestratorUrl { get; set; }
            public string ForceUpdate { get; set; }
        }

        public static void ReadAndNotify(NotifierArgs rcArgs, bool forceUpdate, ref int lastDaySent, TextWriter stdOut, NotifierConfig appConfig)
        {
            stdOut.WriteLine();
            using (var store = new DocumentStore
            {
                Urls = new[] { rcArgs.RavendbUrl },
                Database = "Orchestrator"
            }.Initialize())
            {
                stdOut.WriteLine($"{DateTime.Now} Read index results...");
                using (var session = store.OpenAsyncSession())
                {
                    var roundResult = session.LoadAsync<StaticInfo>("staticInfo/1").Result;
                    if (roundResult == null)
                    {
                        stdOut.WriteLine("Warning: There is no staticInfo/1 doc yet (ok for the first run)");
                        return;
                    }
                    var round = roundResult.Round;
                    var copyRound = round;
                    var results = session.Query<TestInfo, FailTests>().Where(x => x.Round == copyRound, true).ToListAsync().Result;
                    stdOut.WriteLine("Total=" + results.Count);
                    stdOut.WriteLine("Round=" + round);
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
                    stdOut.WriteLine();
                    stdOut.WriteLine("Failed:");
                    stdOut.WriteLine("======");
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

                            stdOut.WriteLine($"{ kv.Key} = {kv.Value}");

                            failureText.Append($"{kv.Key}({kv.Value})");
                        }
                        failureText.Append(@""",  ""short"": true
                                                },
                            ");
                    }

                    var notFinishedText = new StringBuilder();

                    stdOut.WriteLine();
                    stdOut.WriteLine("Not Finished:");
                    stdOut.WriteLine("============ ");
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
                        stdOut.WriteLine($"{kv.Key} = {kv.Value}");
                        notFinishedText.Append(@"
                                                    {
                                                        ""title"": """ + kv.Key + @""",
                                                        ""value"": ""Count: " + kv.Value + @""",
                                                        ""short"": true
                                                    },
                            ");
                    }

                    var copyRound2 = round;
                    var total = session.Query<TestInfo>().Customize(x => x.WaitForNonStaleResults(TimeSpan.FromSeconds(30))).Where(x => x.Author != "TestRunner" && x.Round == copyRound2, true).CountAsync().Result;
                    stdOut.WriteLine($"Out of total={total}");

                    var color = "good"; // green
                    if (fails.Count > 0)
                        color = "danger"; // red
                    else if (total == 0 ||
                        notFinished.Count > 1)
                        color = "warning"; // yellow                        

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
                                                        ""text"": ""<" + rcArgs.RavendbUrl + @"/studio/index.html#databases/query/index/FailTests?&database=Orchestrator|RavenDB Studio> - See all rounds errors\n"",
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
                    stdOut.WriteLine($"now={now}, now.Hour={now.Hour}, now.Day={now.Day}, lastDaySent={lastDaySent}");
                    if (forceUpdate || (now.Hour >= 9 &&
                        now.Day != lastDaySent))
                    {
                        stdOut.WriteLine();
                        stdOut.WriteLine("Sending:");
                        stdOut.WriteLine(msgstring);

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
                            stdOut.WriteLine();
                            stdOut.WriteLine(responseText);
                            stdOut.WriteLine("Done");
                        }
                    }
                }
            }
        }
    }
}
