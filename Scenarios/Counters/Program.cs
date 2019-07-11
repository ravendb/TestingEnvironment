using System;

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
            using (var client = new PutCommentsTest(orchestratorUrl, "PutCommentsTest", -1, Guid.NewGuid().ToString()))
            {
                client.Initialize();
                client.RunTest();
            }

            using (var client = new PutCountersOnCommentsBasedOnTopic(orchestratorUrl, "PutCountersOnCommentsBasedOnTopic", -1, Guid.NewGuid().ToString()))
            {
                client.Initialize();
                client.RunTest();
            }

            using (var client = new PutCountersOnCommentsRandomly(orchestratorUrl, "PutCountersOnCommentsRandomly", -1, Guid.NewGuid().ToString()))
            {
                client.Initialize();
                client.RunTest();
            }

            using (var client = new QueryBlogCommentsByTag(orchestratorUrl, "QueryBlogCommentsByTag", -1, Guid.NewGuid().ToString()))
            {
                client.Initialize();
                client.RunTest();
            }

            using (var client = new PatchCommentRatingsBasedOnCounters(orchestratorUrl, "PatchCommentRatingsBasedOnCounters", -1, Guid.NewGuid().ToString()))
            {
                client.Initialize();
                client.RunTest();
            }

            using (var client = new QueryBlogCommentsAndIncludeCounters(orchestratorUrl, "QueryBlogCommentsAndIncludeCounters", -1, Guid.NewGuid().ToString()))
            {
                client.Initialize();
                client.RunTest();
            }

            using (var client = new LoadBlogCommentsAndIncludeCounters(orchestratorUrl, "LoadBlogCommentsAndIncludeCounters", -1, Guid.NewGuid().ToString()))
            {
                client.Initialize();
                client.RunTest();
            }

            using (var client = new IncrementCountersByPatch(orchestratorUrl, "IncrementCountersByPatch", -1, Guid.NewGuid().ToString()))
            {
                client.Initialize();
                client.RunTest();
            }

            using (var client = new SubscribeToCounterChanges(orchestratorUrl, "SubscribeToCounterChanges", -1, Guid.NewGuid().ToString()))
            {
                client.Initialize();
                client.RunTest();
            }

            using (var client = new IndexQueryOnCounterNames(orchestratorUrl, "IndexQueryOnCounterNames", -1, Guid.NewGuid().ToString()))
            {
                client.Initialize();
                client.RunTest();
            }

            using (var client = new CounterRevisions(orchestratorUrl, "CounterRevisions", -1, Guid.NewGuid().ToString()))
            {
                client.Initialize();
                client.RunTest();
            }

        }
    }
}