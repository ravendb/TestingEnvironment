using TestingEnvironment.Common.OrchestratorReporting;

namespace TestingEnvironment.Orchestrator
{
    public class RoundResults
    {
        public int Round;
        public string RoundStatus;
        public int TotalTestsInRound;
        public int TotalFailures;
        public int TotalStillRunning;
        public int UniqueFailCount;
        public UniqueTestsDetails[] UniqueTestsDetailsInfo;
        public TestInfo[] FailTestInfoDetails;
    }

    public class UniqueTestsDetails
    {
        public string TestName;
        public int FailCount;
    }
}