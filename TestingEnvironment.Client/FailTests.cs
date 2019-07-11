using System.Linq;
using Raven.Client.Documents.Indexes;
using TestingEnvironment.Common;
using TestingEnvironment.Common.OrchestratorReporting;

namespace TestingEnvironment.Client
{
    public class FailTests : AbstractIndexCreationTask<TestInfo>
    {
        public FailTests()
        {
            Map = tests => from test in tests
                           where (test.Events.All(x => x.Type != EventInfoWithExceptionAsString.EventType.TestSuccess) ||
                                  test.Events.Any(x => x.Type == EventInfoWithExceptionAsString.EventType.TestFailure))
                           select new
                           {
                               test.Id,
                               test.TestId,
                               test.Name,
                               test.Round,
                               test.Finished,
                               test.Author
                           };
        }
    }

    public class FailTestsByCurrentRound : AbstractIndexCreationTask<TestInfo>
    {
        public FailTestsByCurrentRound()
        {
            Map = tests => from test in tests
                let round = LoadDocument<StaticInfo>("staticInfo/1").Round
                where (test.Events.All(x => x.Type != EventInfoWithExceptionAsString.EventType.TestSuccess) ||
                       test.Events.Any(x => x.Type == EventInfoWithExceptionAsString.EventType.TestFailure)) &&
                      test.Finished == true &&
                      test.Round == round
                select new
                {
                    test.Id                    
                };            
        }
    }
}
