using Newtonsoft.Json;
using Raven.Client.Documents;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net;
using System.Text;
using System.Threading;
using TestingEnvironment.Common.OrchestratorReporting;

namespace Notifier
{
    public class Program
    {
        public class Payload
        {
            [JsonProperty("channel")]
            public string Channel { get; set; }
            [JsonProperty("username")]
            public string Username { get; set; }
            [JsonProperty("attachments")]
            public string Attachments { get; set; }
        }
        internal class Results
        {
            public int TotalTests { get; set; }
            public Dictionary<string, int> Failed { get; set; }
            public Dictionary<string, int> NotFinished { get; set; }
        }

        public static void Main(string[] args)
        {
            var url = args[0];

            int lastDaySent = DateTime.Now.Day;
            var round = 1;
            Console.WriteLine($"Notifier of Embedded Server : {url}");

            while (true)
            {
                Console.WriteLine();
                using (var store = new DocumentStore
                {
                    Urls = new[] { url },
                    Database = "Orchestrator"
                }.Initialize())
                {
                    Console.WriteLine($"{DateTime.Now} Read index results...");
                    using (var session = store.OpenAsyncSession())
                    {
                        var results = session.Query<TestInfo, TestingEnvironment.Orchestrator.FailTestsComplete>().ToListAsync().Result;
                        Console.WriteLine("Total=" + results.Count);
                        var fails = new Dictionary<string, int>();
                        var notFinished = new Dictionary<string, int>();
                        var totalFailuresCount = 0;
                        var totalNotCompletedCount = 0;

                        foreach (var item in results)
                        {
                            if (item.Finished == true)
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
                        foreach (var kv in fails)
                        {
                            if (first)
                            {
                                failureText.Append(@"
                                                    {
                                                        ""title"": ""*_Failures_*"",
                                                        ""value"": ""Unique Tests Count: " + fails.Count + @""",
                                                        ""short"": false
                                                    },
                                ");
                                first = false;
                            }
                            Console.WriteLine($"{kv.Key} = {kv.Value}");
                            failureText.Append(@"
                                                {
                                                    ""title"": """ + kv.Key + @""",
                                                    ""value"": ""Count: " + kv.Value + @""",
                                                    ""short"": true
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

                        var total = session.Query<TestInfo>().Where(x => x.Author != "TestRunner", true).CountAsync().Result;
                        Console.WriteLine($"Out of total={total}");

                        var color = "good"; // green
                        if (fails.Count > 0 ||
                            notFinished.Count > 0)
                            color = "danger"; // red
                        else if (total == 0)
                            color = "warning"; // yellow                        

                        

                        string msgstring = @"
                                            {   
                                                ""Username"": """+ usermail + @""",
                                                ""Channel"": """ + username + @""",
                                                ""attachments"": [
                                                    {
                                                        ""mrkdwn_in"": [""text""],
                                                        ""color"": """ + color + @""",
                                                        ""pretext"": ""Testing Environment Results"",
                                                        ""author_name"": ""Round " + round + @" (Click to view)"",
                                                        ""author_link"": ""http://10.0.0.69:8090/studio/index.html#databases/query/index/FailTestsComplete?&database=Orchestrator"",
                                                        ""author_icon"": ""https://ravendb.net/img/team/adi_avivi.jpg"",
                                                        ""title"": ""Total Tests: " + total + @" | Total Failures: " + totalFailuresCount + @" | Total Not Completed: " + totalNotCompletedCount + @""",
                                                        ""text"": ""\n\n"",
                                                        ""fields"": [
                                                                    " + notFinishedText.ToString() + @"
                                                            " + failureText.ToString() + @"                        
                                                        ],                        
                                                        ""thumb_url"": ""https://ravendb.net/img/home/raven.png"",
                                                        ""footer"": ""The results were generated at " + DateTime.Now + @""",
                                                        ""footer_icon"": ""https://platform.slack-edge.com/img/default_application_icon.png"",
                                                        ""ts"": 123456789
                                                    }
                                                ]
                                            }
                                            ";

                        
                        var now = DateTime.Now;
                        if (now.Hour >= 9 &&
                            now.Day != lastDaySent)
                        {
                            Console.WriteLine();
                            Console.WriteLine("Sending:");
                            Console.WriteLine(msgstring);

                            lastDaySent = now.Day;
                            using (WebClient client = new WebClient())
                            {
                                NameValueCollection data = new NameValueCollection();
                                data["payload"] = msgstring;
                                var response = client.UploadValues(uri, "POST", data);

                                //The response text is usually "ok"
                                string responseText = Encoding.UTF8.GetString(response);
                                Console.WriteLine();
                                Console.WriteLine(responseText);
                                Console.WriteLine("Done");
                                round++;
                            }
                        }
                    }
                }

                Thread.Sleep(TimeSpan.FromHours(1));
            }
        }
    }
}
