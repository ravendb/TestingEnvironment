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
            Console.WriteLine("Running RavenDB Test Orchestrator.");
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
