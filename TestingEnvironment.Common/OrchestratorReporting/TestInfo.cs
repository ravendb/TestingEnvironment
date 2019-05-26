using System;
using System.Collections.Generic;

namespace TestingEnvironment.Common.OrchestratorReporting
{
    public class TestInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Author { get; set; }
        public string ExtendedName { get; set; }
        public string TestClassName { get; set; }
        public DateTime Start { get;set; }
        public DateTime End { get;set; }
        public bool Finished { get; set; }
        public List<EventInfoWithExceptionAsString> Events { get;set; }
        public TestConfig Config { get; set; }
    }
}
