using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using static TestingEnvironment.Orchestrator.SlackNotifier;

namespace TestingEnvironment.Orchestrator
{
    public class Program
    {
        private static ManualResetEvent _mre = new ManualResetEvent(false);
        private static CancellationTokenSource _cts = new CancellationTokenSource();
        private static OrchestratorConfiguration _appConfig;

        public static void LaunchReadAndNotify()
        {
            var firstRun = true;
            var lastDaySent = DateTime.Now.Day;
            NotifierArgs rcArgs = new NotifierArgs
            {
                OrchestratorUrl = _appConfig.OrchestratorUrl,
                ForceUpdate = "none",
                RavendbUrl = _appConfig.EmbeddedServerUrl

            };
            var waitTime = TimeSpan.FromHours(1);
            while (true)
            {
                try
                {
                    using (var fs = new FileStream("notifier.log", FileMode.OpenOrCreate, FileAccess.ReadWrite))
                    {
                        using (var fw = new StreamWriter(fs))
                        {
                            ReadAndNotify(rcArgs, false, ref lastDaySent, fw, _appConfig.NotifierConfig);
                            fw.Flush();
                        }
                    }
                }
                catch (Exception e)
                {
                    if (firstRun)
                    {
                        Console.WriteLine($"Got on first run: {e.Message}");
                        firstRun = false;
                        waitTime = TimeSpan.FromSeconds(15);
                    }
                    else
                    {
                        Console.WriteLine(e);
                        waitTime = TimeSpan.FromHours(1);
                    }
                }

                if (_cts.Token.WaitHandle.WaitOne(waitTime))
                    break;                
            }

            _mre.Set();
        }

        public static void Main(string[] args)
        {
            var builder = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .SetBasePath(Environment.CurrentDirectory);
            var config = builder.Build();
            var appConfig = new OrchestratorConfiguration();
            ConfigurationBinder.Bind(config, appConfig);


            Console.WriteLine("Running RavenDB Test Orchestrator");
            Console.WriteLine("=================================");
            var _orch = Orchestrator.Instance;
            var strategy = "FirstClusterSelector";
            if (_orch.TrySetConfigSelectorStrategy(strategy, "1"))
                Console.WriteLine($"Set default strategy to: {strategy}");
            else
                throw new InvalidOperationException($"Cannot set strategy to {strategy}");

            _appConfig = appConfig;         
            var notifTask = new Task(LaunchReadAndNotify, _cts.Token, TaskCreationOptions.LongRunning);
            notifTask.Start();
            

            CreateWebHostBuilder(appConfig.OrchestratorUrl).Build().Run();
            Console.WriteLine("Exiting...");
            _cts.Cancel();
            _mre.WaitOne(TimeSpan.FromSeconds(30));
            Console.WriteLine("Exited");
        }

        public static IWebHostBuilder CreateWebHostBuilder(string orchUrl)
        {
            return new WebHostBuilder()
                .UseContentRoot(Directory.GetCurrentDirectory())
                .UseUrls(orchUrl) // , "http://localhost:5000")
                .UseKestrel()
                .UseStartup<Startup>();
        }
    }
}
