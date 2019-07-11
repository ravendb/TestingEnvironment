using System;

namespace Subscriptions
{
    public class Program
    {
        static void Main(string[] args)
        {
            RunSubscriptionScenarios(args[0]);
        }

        static void RunSubscriptionScenarios(string orchestratorUrl)
        {
            using (var client = new FilterAndProjection(orchestratorUrl, "FilterAndProjection", -1, Guid.NewGuid().ToString()))
            {
                client.Initialize();
                client.RunTest();
            }
        }
    }
}
