using System;
using System.Diagnostics;
using System.Linq;
using Raven.Client.Documents.Operations;
using TestingEnvironment.Client;

namespace Counters
{
    public class PatchCommentRatingsBasedOnCounters : BaseTest
    {
        public PatchCommentRatingsBasedOnCounters(string orchestratorUrl, string testName, int round, string testid) : base(orchestratorUrl, testName, "Aviv", round, testid)
        {
        }

        public override void RunActualTest()
        {
            var now = DateTime.UtcNow;
            int notToUpdateCount = 0;
            using (var session = DocumentStore.OpenSession())
            {
                ReportInfo("Starting PatchByQueryOperation on BlogComments collection");

                // ReSharper disable once ReplaceWithSingleCallToCount
                var toUpdateCount = session.Query<BlogComment>()
                    .Where(comment => comment.LastModified < now.AddMinutes(-15))
                    .Count();

                notToUpdateCount = session.Query<BlogComment>().Count() - toUpdateCount;

                if (toUpdateCount == 0)
                {
                    ReportSuccess("Aborting patch. All docs have been updated in the last 15 minutes");
                    return;
                }

                ReportInfo($"Number of BlogComments to patch : {toUpdateCount}");
            }

            var script = @"from BlogComments as comment 
                           where comment.LastModified < '" + now.AddMinutes(-15).ToString("o") + @"'
                           update 
                           {
	                           var likes = counter(comment, 'likes');
	                           var dislikes = counter(comment, 'dislikes');
	                           var rating = ((likes / (likes + dislikes)) * 100) || 0;
	                           comment.Rating = rating;
                               comment.LastModified = new Date('" + now.ToString("o") + @"');
                           }";

            var op = DocumentStore.Operations.Send(new PatchByQueryOperation(script));

            try
            {
                op.WaitForCompletion(TimeSpan.FromMinutes(1));
            }
            catch (Exception e1)
            {
                ReportFailure("An error occurred while waiting for PatchByQueryOperation to complete. Trying again", e1);

                try
                {
                    op.WaitForCompletion(TimeSpan.FromMinutes(1));
                }
                catch (Exception e2)
                {
                    ReportFailure("Failed to complete patch by query operation after waiting for 2 minutes. Aborting.", e2);
                    return;
                }
            }

            ReportInfo("Finished PatchByQueryOperation on BlogComments");

            using (var session = DocumentStore.OpenSession())
            {
                var almostNow = now - TimeSpan.FromSeconds(1);
                // ReSharper disable once ReplaceWithSingleCallToCount
                var count = session.Query<BlogComment>()
                    .Customize(x => x.WaitForNonStaleResults(TimeSpan.FromMinutes(1)))
                    .Where(comment => comment.LastModified < almostNow)
                    .Count();

                if (count != notToUpdateCount) 
                {
                    ReportFailure("Failed. Expected to have no BlogComment docs that have " +
                                  $"LastModified < {almostNow:o}, but found {count} such docs. (notToUpdateCount={notToUpdateCount})", null);
                }
                else
                {
                    ReportSuccess($"Success. All BlogComment docs now have LastModified >= {almostNow:o}. ");
                }
            }
        }
    }
}
