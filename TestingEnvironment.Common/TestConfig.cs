using System;

namespace TestingEnvironment.Common
{
    public class TestConfig
    {
        public string StrategyName { get;set; }
        public string[] Urls { get; set; }
        public string Database { get; set; }
        public string PemFilePath { get; set; }
    }
}
