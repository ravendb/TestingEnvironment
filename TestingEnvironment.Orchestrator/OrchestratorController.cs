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
                    Uri.UnescapeDataString((string) Request.Query.author),
                    Uri.UnescapeDataString((string) Request.Query.round)));

            Put("/unregister", @params =>
            {
                Orchestrator.Instance.UnregisterTest(Uri.UnescapeDataString((string) Request.Query.testName), Uri.UnescapeDataString((string)Request.Query.round));
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

            //non success tests
            Get<dynamic>("/failing-tests", @params =>
                Response.AsJson(Orchestrator.Instance.GetFailingTests()));

            Get<dynamic>("/config-selectors",_ =>
                Response.AsJson(Orchestrator.Instance.ConfigSelectorStrategies.Select(x => new
                {
                    x.Name,
                    x.Description
                })));

            //PUT http://localhost:5000/config-selectors?strategyName=FirstClusterSelector
            Put("/config-selectors", @params =>
            {
                return Orchestrator.Instance.TrySetConfigSelectorStrategy(Uri.UnescapeDataString((string)Request.Query.strategyName));
            });

            Get<dynamic>("/get-round", _ =>
                 Response.AsJson(Orchestrator.Instance.GetRound()));

            //PUT http://localhost:5000/set-round?round=345
            Put("/set-round", @params => Orchestrator.Instance.SetRound(Request.Query.round).ToString());

            Get<dynamic>("/round-results", _ =>
                 Response.AsJson(Orchestrator.Instance.GetRoundResults((string)Request.Query.round)));
        }
    }
}
