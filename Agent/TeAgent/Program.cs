using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TeAgent
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("WARNING: Running this agent gives unrestricted remote access to this machine!");

            if (args.Length != 1 || args[0].Equals("--skip-warning") == false)
            {
                Console.WriteLine("Type: 'I Understand' to allow and continue");
                var ans = Console.ReadLine();
                if (ans.ToLower().StartsWith("i understand") == false)
                {
                    Console.WriteLine(ans.ToLower());
                    Environment.Exit(1);
                }
            }
            
            CreateWebHostBuilder(args).Build().Run();
        }

        public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                .UseUrls(new [] { "http://0.0.0.0:9123" })
                .UseStartup<Startup>();
    }
}
