using Microsoft.Extensions.Configuration;
using Raven.Client.Documents;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using TestingEnvironment.Common.OrchestratorReporting;
using TestingEnvironment.Client;
using TestingEnvironment.Orchestrator;
using static TestingEnvironment.Orchestrator.SlackNotifier;

namespace Notifier
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var helpText = @"
Usage: Notifier --ravendbUrl=<url> --orchestratorUrl=<url> [--forceUpdate=force]
";
            var defaults = new NotifierArgs();
            var rcArgs = new HandleArgs<NotifierArgs>().ProcessArgs(args, helpText, defaults);
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

            var builder = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .SetBasePath(Environment.CurrentDirectory);
            var config = builder.Build();
            var appConfig = new NotifierConfig();
            ConfigurationBinder.Bind(config, appConfig);

            while (true)
            {
                try
                {
                    ReadAndNotify(rcArgs, forceUpdate, ref lastDaySent, Console.Out, appConfig);
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
