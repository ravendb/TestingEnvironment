using System;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace TestingEnvironment.Orchestrator
{
    class Program
    {
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
            var strategy = "FirstClusterRandomDatabaseSelector";
            if (_orch.TrySetConfigSelectorStrategy(strategy))
                Console.WriteLine($"Set default strategy to: {strategy}");
            else
                throw new InvalidOperationException($"Cannot set strategy to {strategy}");
            CreateWebHostBuilder(appConfig.OrchestratorUrl).Build().Run();
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
