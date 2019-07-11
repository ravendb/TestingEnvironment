using System;
using System.Collections.Generic;

namespace TestingEnvironment.Common
{
    public class EventInfo
    {
        public enum EventType
        {
            Info,
            Warning,
            Error,
            TestSuccess,
            TestFailure
        }

        public string Message { get;set; }
        public Dictionary<string, string> AdditionalInfo { get; set; }
        public EventType Type { get; set; }
        public Exception Exception { get; set; }
    }

    // Nancy bug forces us to use this:
    public class EventInfoWithExceptionAsString
    {
        public enum EventType
        {
            Info,
            Warning,
            Error,
            TestSuccess,
            TestFailure
        }

        public string Message { get; set; }
        public Dictionary<string, string> AdditionalInfo { get; set; }
        public EventType Type { get; set; }
        public string Exception { get; set; }
        public string EventTime { get; set; }
    }

    public enum Command
    {
        None = 0,
        RemoveLastRunningTestInfo
    }
}
