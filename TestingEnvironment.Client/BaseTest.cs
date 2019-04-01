using System;
using System.Collections.Generic;
using Raven.Client.Documents;
using ServiceStack;
using TestingEnvironment.Common;

namespace TestingEnvironment.Client
{
    public abstract class BaseTest : IDisposable
    {
        protected readonly string OrchestratorUrl;
        protected readonly string TestName;
        
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
            var config = _orchestratorClient.Put<TestConfig>($"/register?testName={Uri.EscapeDataString(TestName)}&testClassName={Uri.EscapeDataString(GetType().FullName)}&author={Uri.EscapeDataString(_author)}",null);
            DocumentStore = new DocumentStore
            {
                Urls = config.Urls,
                Database = config.Database
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

        protected virtual EventResponse ReportEvent(EventInfo eventInfo) => 
            _orchestratorClient.Post<EventResponse>($"/report?testName={TestName}", eventInfo);

        public virtual void Dispose()
        {
            _orchestratorClient.Put<object>($"/unregister?testName={TestName}",null);

            _orchestratorClient.Dispose();
            DocumentStore.Dispose();
        }
    }
}
