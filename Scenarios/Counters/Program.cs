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
            using (var client = new PutCommentsTest(orchestratorUrl, "PutCommentsTest"))
            {
                client.Initialize();
                client.RunTest();
            }

            using (var client = new PutCountersOnCommentsBasedOnTopic(orchestratorUrl, "PutCountersOnCommentsBasedOnTopic"))
            {
                client.Initialize();
                client.RunTest();
            }

            using (var client = new PutCountersOnCommentsRandomly(orchestratorUrl, "PutCountersOnCommentsRandomly"))
            {
                client.Initialize();
                client.RunTest();
            }

            using (var client = new QueryBlogCommentsByTag(orchestratorUrl, "QueryBlogCommentsByTag"))
            {
                client.Initialize();
                client.RunTest();
            }

            using (var client = new PatchCommentRatingsBasedOnCounters(orchestratorUrl, "PatchCommentRatingsBasedOnCounters"))
            {
                client.Initialize();
                client.RunTest();
            }

            using (var client = new QueryBlogCommentsAndIncludeCounters(orchestratorUrl, "QueryBlogCommentsAndIncludeCounters"))
            {
                client.Initialize();
                client.RunTest();
            }

            using (var client = new LoadBlogCommentsAndIncludeCounters(orchestratorUrl, "LoadBlogCommentsAndIncludeCounters"))
            {
                client.Initialize();
                client.RunTest();
            }

            using (var client = new IncrementCountersByPatch(orchestratorUrl, "IncrementCountersByPatch"))
            {
                client.Initialize();
                client.RunTest();
            }

            using (var client = new SubscribeToCounterChanges(orchestratorUrl, "SubscribeToCounterChanges"))
            {
                client.Initialize();
                client.RunTest();
            }

            using (var client = new IndexQueryOnCounterNames(orchestratorUrl, "IndexQueryOnCounterNames"))
            {
                client.Initialize();
                client.RunTest();
            }

            using (var client = new CounterRevisions(orchestratorUrl, "CounterRevisions"))
            {
                client.Initialize();
                client.RunTest();
            }

        }
    }
}