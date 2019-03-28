using System;
using System.Linq;
using Nancy;
using Nancy.ModelBinding;
using TestingEnvironment.Common;

// ReSharper disable VirtualMemberCallInConstructor

namespace TestingEnvironment.Orchestrator
{    
    public class OrchestratorController : NancyModule
    {
        private readonly OrchestratorConfiguration _config;
        private static readonly object Empty = new object();

        public OrchestratorController(OrchestratorConfiguration config)
        {
            
            _config = config;
            Put("/register", @params => 
                Orchestrator.Instance.RegisterTest(
                    Uri.UnescapeDataString((string) Request.Query.testName), 
                    Uri.UnescapeDataString((string) Request.Query.testClassName),
                    Uri.UnescapeDataString((string) Request.Query.author)));

            Put("/unregister", @params =>
            {
                Orchestrator.Instance.UnregisterTest(Uri.UnescapeDataString((string) Request.Query.testName));
                return Empty;
            });

            // Due to bug in Nancy (cannot serialize Exception), we copy the EventInfo:
                        
            Post("/report", @params =>
            {
                return Orchestrator.Instance.ReportEvent(Uri.UnescapeDataString((string) Request.Query.testName),
                        this.Bind<EventInfoWithExceptionAsString>());
            });
            
            //get latest test by name
            Get<dynamic>("/latest-tests", @params => 
                Response.AsJson(Orchestrator.Instance.GetLastTestByName(Uri.UnescapeDataString((string) Request.Query.testName))));

            Get<dynamic>("/config-selectors",_ =>
                Response.AsJson(Orchestrator.Instance.ConfigSelectorStrategies.Select(x => new
                {
                    x.Name,
                    x.Description
                })));

            //PUT http://localhost:5000/config-selectors?strategyName=FirstClusterSelector
            Put("/config-selectors", @params =>
            {
                var isSucceeded = Orchestrator.Instance.TrySetConfigSelectorStrategy(Uri.UnescapeDataString((string) Request.Query.strategyName));
                return isSucceeded ? HttpStatusCode.OK : HttpStatusCode.NotFound;
            });
        }
    }
}
