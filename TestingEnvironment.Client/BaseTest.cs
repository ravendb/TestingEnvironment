using System;
using System.Collections.Generic;
using System.Globalization;
using Raven.Client.Documents;
using ServiceStack;
using TestingEnvironment.Common;

namespace TestingEnvironment.Client
{
    public abstract class BaseTest : IDisposable
    {
        protected readonly string OrchestratorUrl;
        public readonly string TestName;
        
        private readonly string _author;
        private readonly int _round;
        private readonly string _testid;

        protected IDocumentStore DocumentStore;
        protected Random Random = new Random(123);

        private readonly JsonServiceClient _orchestratorClient;

        protected BaseTest(string orchestratorUrl, string testName, string author, int round, string testid)
        {
            _testid = testid;
            _round = round;
            OrchestratorUrl = orchestratorUrl ?? throw new ArgumentNullException(nameof(orchestratorUrl));
            TestName = testName ?? throw new ArgumentNullException(nameof(testName));
            _author = author;
            _orchestratorClient = new JsonServiceClient(OrchestratorUrl);           
        }

        public virtual void Initialize()
        {
            var url = $"/register?testName={Uri.EscapeDataString(TestName)}&testClassName={Uri.EscapeDataString(GetType().FullName)}&author={Uri.EscapeDataString(_author)}&round={_round}&testid={_testid}";
            var config = _orchestratorClient.Put<TestConfig>(url,null);
            var cert = config.PfxFilePath == null || config.PfxFilePath.Equals("") ? null :
                config.Password == null || config.Password.Equals("") ? new System.Security.Cryptography.X509Certificates.X509Certificate2(config.PfxFilePath) :
                new System.Security.Cryptography.X509Certificates.X509Certificate2(config.PfxFilePath, config.Password); // TODO : remove "HasAuthentication"
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
            var response = ReportEvent(new EventInfo
            {
                Message = message,
                AdditionalInfo = additionalInfo,
                Type = EventInfo.EventType.Info
            }, _testid);
            if (response.Type != EventResponse.ResponseType.Ok)
                Console.WriteLine($"ERROR: ReportEvent Response == {response.Type} for : '{message}'");
        }

        protected void ReportSuccess(string message, Dictionary<string, string> additionalInfo = null)
        {
            var response = ReportEvent(new EventInfo
            {
                Message = message,
                AdditionalInfo = additionalInfo,
                Type = EventInfo.EventType.TestSuccess
            }, _testid);
            if (response.Type != EventResponse.ResponseType.Ok)
                Console.WriteLine($"ERROR: ReportEvent Response == {response.Type} for : '{message}'");
        }

        protected void ReportFailure(string message, Exception error, Dictionary<string, string> additionalInfo = null)
        {
            var response = ReportEvent(new EventInfo
            {
                Message = message,
                AdditionalInfo = additionalInfo,
                Exception = error,
                Type = EventInfo.EventType.TestFailure
            }, _testid);
            if (response.Type != EventResponse.ResponseType.Ok)
                Console.WriteLine($"ERROR: ReportEvent Response == {response.Type} for : '{message}'");
        }

        private EventResponse ReportEvent(EventInfo eventInfo, string testid)
        {
            var eventInfoWithExceptionAsString = new EventInfoWithExceptionAsString
            {
                AdditionalInfo = eventInfo.AdditionalInfo,
                Exception = eventInfo.Exception?.ToString(),
                Message = eventInfo.Message,
                Type = (EventInfoWithExceptionAsString.EventType)eventInfo.Type,
                EventTime = DateTime.UtcNow.ToString(CultureInfo.InvariantCulture)
            };
            return _orchestratorClient.Post<EventResponse>($"/report?testName={TestName}&testid={testid}&round={_round}", eventInfoWithExceptionAsString);
        }

        protected string ExecuteCommand(string command, string dataString)
        {
            Console.WriteLine($"/custom-command?command={Uri.EscapeDataString(command)}&data={Uri.EscapeDataString(dataString)}");
            return _orchestratorClient.Put<string>($"/custom-command?command={Uri.EscapeDataString(command)}&data={Uri.EscapeDataString(dataString)}", "");
        }

        protected int SetRound(int round)
        {
            var currentRound = _orchestratorClient.Get<int>($"/get-round");
            if (round == 0)
                round = ++currentRound;
            if (round != -1)
            {
                var response = _orchestratorClient.Put<dynamic>($"/set-round?round={round}", "");
                currentRound = int.Parse(response);
            }
            return currentRound;
        }

        protected bool SetStrategy(string strategy, string dbIndex)
        {
            var _ = ReportEvent(new EventInfo { Message = $"Setting strategy to {strategy}" }, _testid);
            var dbIndexStr = dbIndex == null ? "" : $"&dbIndex={Uri.EscapeDataString(dbIndex)}";
            var rc = _orchestratorClient.Put<bool>($"/config-selectors?strategyName={Uri.EscapeDataString(strategy)}{dbIndexStr}", "");
            if (rc)
                ReportInfo($"Successfully set strategy to {strategy}");
            else
                ReportFailure("Failed to set", new Exception($"Failed to /config-selectors?strategyName={strategy}"));
            return rc;
        }
            

        public virtual void Dispose()
        {
            _orchestratorClient.Put<object>($"/unregister?testName={TestName}&round={_round}&testid={_testid}",null);

            _orchestratorClient.Dispose();
            DocumentStore.Dispose();
        }
    }
}
