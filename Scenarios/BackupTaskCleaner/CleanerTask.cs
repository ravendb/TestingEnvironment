﻿using TestingEnvironment.Client;

namespace CleanerTask
{
    public class CleanerTask : BaseTest
    {
        public CleanerTask(string orchestratorUrl, string testName, int round, string testid) : base(orchestratorUrl, testName, "Adi", round, testid)
        {
        }

        public override void RunActualTest()
        {
            DoWork();
        }

        public void DoWork()
        {
            ReportSuccess("TODO");
        }
    }
}
