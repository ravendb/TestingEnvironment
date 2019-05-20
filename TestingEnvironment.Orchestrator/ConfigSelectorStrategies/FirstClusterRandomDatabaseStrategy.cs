using System;
using Raven.Client.Documents;
using TestingEnvironment.Common;
using TestingEnvironment.Common.OrchestratorReporting;

namespace TestingEnvironment.Orchestrator.ConfigSelectorStrategies
{
    public class FirstClusterRandomDatabaseStrategy : ITestConfigSelectorStrategy
    {
        private Lazy<TestConfig> _nextConfigLazy;

        public void Initialize(OrchestratorConfiguration configuration)
        {
            if (configuration.Databases?.Length == 0)
                throw new InvalidOperationException("Must be at least one database configured!");

            if (configuration.Clusters?.Length == 0)
                throw new InvalidOperationException("Must be at least one cluster configured!");

            var rnd = new Random().Next(1, configuration.Databases.Length - 1);

            _nextConfigLazy = new Lazy<TestConfig>(() => new TestConfig
            {
                Database = configuration.Databases[rnd],
                Urls = configuration.Clusters[0].Urls,
                StrategyName = Name
            });
        }

        public string Name => "FirstClusterRandomDatabaseSelector";
        public string Description => "Simply selects as configuration the first database and the first cluster from the settings file";

        public void OnBeforeRegisterTest(IDocumentStore store)
        {
        }

        public void OnAfterUnregisterTest(TestInfo testInfo, IDocumentStore store)
        {
        }

        public TestConfig GetNextTestConfig() => _nextConfigLazy.Value;
    }
}
