using System;
using System.IO;
using Microsoft.AspNetCore.Hosting;

namespace TestingEnvironment.Orchestrator
{
    class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("Running RavenDB Test Orchestrator.");
            CreateWebHostBuilder(args).Build().Run();
        }

        public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
            new WebHostBuilder()
                .UseContentRoot(Directory.GetCurrentDirectory())
		        .UseUrls(args[0], "http://localhost:5000")
                .UseKestrel()
                .UseStartup<Startup>();
    }
}
