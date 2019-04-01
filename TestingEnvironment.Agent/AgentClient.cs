//using System;
//using System.Collections.Generic;
//using System.Reflection;
//using Microsoft.AspNetCore.Connections;
//using ServiceStack;
//
//namespace TestingEnvironment.Agent
//{
//    public abstract class AgentClient : IDisposable
//    {
//        private readonly JsonServiceClient _agentClient;
//        
//        public virtual void Initialize(string agenturl)
//        {
//            var regStatus = _agentClient.Put<string>($"/agent-register?agenturl={agenturl}", null);
//            if (regStatus.Contains("OK", StringComparison.OrdinalIgnoreCase) == false)
//            {
//                throw new ConnectionAbortedException($"Failed to to agent-register?agenturl={agenturl}");
//            }
//
//            Console.WriteLine($"Registered agent with url={agenturl}");
//        }
//        
//        
//        protected virtual EventResponse ReportEvent(EventInfo eventInfo) =>
//            _orchestratorClient.Post<EventResponse>($"/report?testName={TestName}", eventInfo);
//
//        public virtual void Dispose()
//        {
//            _orchestratorClient.Put<object>($"/unregister?testName={TestName}", null);
//
//            _orchestratorClient.Dispose();
//            DocumentStore.Dispose();
//        }
//    }
//}
