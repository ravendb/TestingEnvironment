using System;
using System.Linq;
using Nancy;
using Nancy.ModelBinding;
using ServiceStack;
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
                    Uri.UnescapeDataString((string) Request.Query.round),
                    Uri.UnescapeDataString((string)Request.Query.testid)
                    ));

            Put("/unregister", @params =>
            {
                Orchestrator.Instance.UnregisterTest(Uri.UnescapeDataString(
                        (string) Request.Query.testName), 
                    Uri.UnescapeDataString((string)Request.Query.round),
                    Uri.UnescapeDataString((string)Request.Query.testid)
                    );
                return Empty;
            });

            // Due to bug in Nancy (cannot serialize Exception), we copy the EventInfo:
                        
            Post("/report", @params =>
            {
                return Orchestrator.Instance.ReportEvent(Uri.UnescapeDataString((string) Request.Query.testName),
                    Uri.UnescapeDataString((string)Request.Query.testid),
                    Uri.UnescapeDataString((string)Request.Query.round),
                        this.Bind<EventInfoWithExceptionAsString>());
            });

            Delete("/cancel", @params =>
            {
                Orchestrator.Instance.UnregisterTest(Uri.UnescapeDataString(
                        (string)Request.Query.testName),
                    Uri.UnescapeDataString((string)Request.Query.round),
                    Uri.UnescapeDataString((string)Request.Query.testid)
                );
                return Orchestrator.Instance.Cancel(Uri.UnescapeDataString((string) Request.Query.testName),
                    Uri.UnescapeDataString((string) Request.Query.testid),
                    Uri.UnescapeDataString((string) Request.Query.round));
            });

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
                return Orchestrator.Instance.TrySetConfigSelectorStrategy(Uri.UnescapeDataString((string)Request.Query.strategyName), Uri.UnescapeDataString((string)Request.Query.dbIndex ?? ""));
            });

            // GET http://localhost:5000/get-round?doc='staticInfo doc id'
            Get<dynamic>("/get-round", @params =>
                Response.AsJson(Orchestrator.Instance.GetRound(Uri.UnescapeDataString((string)Request.Query.doc))));

            //PUT http://localhost:5000/set-round?doc='staticInfo doc id'&round=345&archive=<0|1>
            Put("/set-round", @params => Orchestrator.Instance.
                SetRound(
                    Uri.UnescapeDataString((string)Request.Query.doc), 
                    Request.Query.round, 
                    Uri.UnescapeDataString((string)Request.Query.version), 
                    Uri.UnescapeDataString((string)Request.Query.archive)
                    ).ToString());

            Get<dynamic>("/round-results", _ =>
                 Response.AsJson(Orchestrator.Instance.GetRoundResults((string)Request.Query.round)));
            
            // http://localhost:5000/custom-command?command={command}&data={dataString}");
            Put("/custom-command",
                @params => Orchestrator.Instance.ExecuteCommand(Uri.UnescapeDataString((string) Request.Query.command),
                    Uri.UnescapeDataString((string) Request.Query.data)));
        }
    }
}
