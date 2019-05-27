using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using TestingEnvironment.Common;

namespace TestingEnvironment.Orchestrator
{
    class Program
    {
        public static void Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.WriteLine("Usage: Orchestrator <orchestrator url>");
                Environment.Exit(1);
            }
            Console.WriteLine("Running RavenDB Test Orchestrator");
            Console.WriteLine("=================================");
            var _orch = Orchestrator.Instance;
            var strategy = "FirstClusterRandomDatabaseSelector";
            if (_orch.TrySetConfigSelectorStrategy(strategy))
                Console.WriteLine($"Set default strategy to: {strategy}");
            else
                throw new InvalidOperationException($"Cannot set strategy to {strategy}");
            CreateWebHostBuilder(args).Build().Run();
        }

        public static IWebHostBuilder CreateWebHostBuilder(string[] args)
        {
            return new WebHostBuilder()
                .UseContentRoot(Directory.GetCurrentDirectory())
                .UseUrls(args[0]) // , "http://localhost:5000")
                .UseKestrel()
                .UseStartup<Startup>();
        }
    }
}
