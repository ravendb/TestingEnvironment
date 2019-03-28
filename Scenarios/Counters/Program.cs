namespace Counters
{
    class Program
    {
        static void Main(string[] args)
        {
            RunCountersScenarios(args[0]);
        }

        static void RunCountersScenarios(string orchestratorUrl)
        {
            using (var client = new PutCommentsTest(orchestratorUrl))
            {
                client.Initialize();
                client.RunTest();
            }

            using (var client = new PutCountersOnCommentsBasedOnTopic(orchestratorUrl))
            {
                client.Initialize();
                client.RunTest();
            }

            using (var client = new PutCountersOnCommentsRandomly(orchestratorUrl))
            {
                client.Initialize();
                client.RunTest();
            }

            using (var client = new QueryBlogCommentsByTag(orchestratorUrl))
            {
                client.Initialize();
                client.RunTest();
            }

            using (var client = new PatchCommentRatingsBasedOnCounters(orchestratorUrl))
            {
                client.Initialize();
                client.RunTest();
            }

            using (var client = new QueryBlogCommentsAndIncludeCounters(orchestratorUrl))
            {
                client.Initialize();
                client.RunTest();
            }

            using (var client = new LoadBlogCommentsAndIncludeCounters(orchestratorUrl))
            {
                client.Initialize();
                client.RunTest();
            }

            using (var client = new IncrementCountersByPatch(orchestratorUrl))
            {
                client.Initialize();
                client.RunTest();
            }

            using (var client = new SubscribeToCounterChanges(orchestratorUrl))
            {
                client.Initialize();
                client.RunTest();
            }

            using (var client = new IndexQueryOnCounterNames(orchestratorUrl))
            {
                client.Initialize();
                client.RunTest();
            }

        }
    }
}