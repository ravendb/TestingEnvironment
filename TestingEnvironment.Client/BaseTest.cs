using System;
using System.Collections.Generic;
using Raven.Client.Documents;
using ServiceStack;
using TestingEnvironment.Common;
using System.Net;

namespace TestingEnvironment.Client
{
    public abstract class BaseTest : IDisposable
    {
        protected readonly string OrchestratorUrl;
        public readonly string TestName;
        
        private readonly string _author;

        protected IDocumentStore DocumentStore;
        protected Random Random = new Random(123);

        private readonly JsonServiceClient _orchestratorClient;

        protected BaseTest(string orchestratorUrl, string testName, string author)
        {
            OrchestratorUrl = orchestratorUrl ?? throw new ArgumentNullException(nameof(orchestratorUrl));
            TestName = testName ?? throw new ArgumentNullException(nameof(testName));
            _author = author;
            _orchestratorClient = new JsonServiceClient(OrchestratorUrl);           
        }

        public virtual void Initialize()
        {
            var url = $"/register?testName={Uri.EscapeDataString(TestName)}&testClassName={Uri.EscapeDataString(GetType().FullName)}&author={Uri.EscapeDataString(_author)}";
            var config = _orchestratorClient.Put<TestConfig>(url,null);
            var cert = config.PemFilePath == null ? null : new System.Security.Cryptography.X509Certificates.X509Certificate2(config.PemFilePath); // TODO : remove "HasAuthentication"
            DocumentStore = new DocumentStore
            {
                Urls = config.Urls,
                Database = config.Database,
                Certificate = cert

            };
            DocumentStore.Initialize();
        }

        public void RunTest()
        {
            try
            {
                RunActualTest();
            }
            catch (Exception e)
            {
                ReportFailure("Unhandled exception in test code.",e);
            }
        }

        public abstract void RunActualTest();

        protected void ReportInfo(string message, Dictionary<string, string> additionalInfo = null)
        {
            ReportEvent(new EventInfo
            {
                Message = message,
                AdditionalInfo = additionalInfo,
                Type = EventInfo.EventType.Info
            });
        }

        protected void ReportSuccess(string message, Dictionary<string, string> additionalInfo = null)
        {
            ReportEvent(new EventInfo
            {
                Message = message,
                AdditionalInfo = additionalInfo,
                Type = EventInfo.EventType.TestSuccess
            });
        }

        protected void ReportFailure(string message, Exception error, Dictionary<string, string> additionalInfo = null)
        {
            ReportEvent(new EventInfo
            {
                Message = message,
                AdditionalInfo = additionalInfo,
                Exception = error,
                Type = EventInfo.EventType.TestFailure
            });
        }

        private EventResponse ReportEvent(EventInfo eventInfo)
        {
            var eventInfoWithExceptionAsString = new EventInfoWithExceptionAsString
            {
                AdditionalInfo = eventInfo.AdditionalInfo,
                Exception = eventInfo.Exception?.ToString(),
                Message = eventInfo.Message,
                Type = (EventInfoWithExceptionAsString.EventType)eventInfo.Type,
                EventTime = DateTime.Now.ToString()
            };
            return _orchestratorClient.Post<EventResponse>($"/report?testName={TestName}", eventInfoWithExceptionAsString);
        }

        protected int SetRound(int round)
        {            
            var currentRound = _orchestratorClient.Get<int>($"/get-round");
            if (round == 0)
                round = ++currentRound;
            if (round != -1)
                currentRound = _orchestratorClient.Put<int>($"/set-round?round={round}");
            return currentRound;
        }

        protected bool SetStrategy(string strategy)
        {
            ReportEvent(new EventInfo { Message = $"Setting strategy to {strategy}" });
            var rc = _orchestratorClient.Put<bool>($"/config-selectors?strategyName={Uri.EscapeDataString(strategy)}", "");
            if (rc)
                ReportSuccess("Successfully set");
            else
                ReportFailure("Failed to set", new Exception($"Failed to /config-selectors?strategyName={strategy}"));
            return rc;
        }
            

        public virtual void Dispose()
        {
            _orchestratorClient.Put<object>($"/unregister?testName={TestName}",null);

            _orchestratorClient.Dispose();
            DocumentStore.Dispose();
        }
    }
}
