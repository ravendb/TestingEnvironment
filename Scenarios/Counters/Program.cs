using System.Threading;

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

            using (var client =
                new PutCountersOnCommentsBasedOnTopic(orchestratorUrl, "PutCountersOnCommentsBasedOnTopic"))
            {
                client.Initialize();
                client.RunTest();
            }

            Thread.Sleep(10000);

            using (var client = new PutCountersOnCommentsRandomly(orchestratorUrl, "PutCountersOnCommentsRandomly"))
            {
                client.Initialize();
                client.RunTest();
            }

            Thread.Sleep(10000);

            using (var client = new QueryBlogCommentsByTag(orchestratorUrl, "QueryBlogCommentsByTag"))
            {
                client.Initialize();
                client.RunTest();
            }

            Thread.Sleep(10000);

            using (var client =
                new PatchCommentRatingsBasedOnCounters(orchestratorUrl, "PatchCommentRatingsBasedOnCounters"))
            {
                client.Initialize();
                client.RunTest();
            }

            Thread.Sleep(10000);

            using (var client =
                new QueryBlogCommentsAndIncludeCounters(orchestratorUrl, "QueryBlogCommentsAndIncludeCounters"))
            {
                client.Initialize();
                client.RunTest();
            }
        }
    }
}