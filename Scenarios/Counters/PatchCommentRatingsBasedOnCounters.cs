using System;
using System.Linq;
using Raven.Client.Documents.Operations;
using TestingEnvironment.Client;

namespace Counters
{
    public class PatchCommentRatingsBasedOnCounters : BaseTest
    {
        public PatchCommentRatingsBasedOnCounters(string orchestratorUrl) : base(orchestratorUrl, "PatchCommentRatingsBasedOnCounters", "Aviv")
        {
        }

        public override void RunActualTest()
        {
            var now = DateTime.UtcNow;

            using (var session = DocumentStore.OpenSession())
            {
                ReportInfo("Starting PatchByQueryOperation on BlogComments collection");

                // ReSharper disable once ReplaceWithSingleCallToCount
                var toUpdateCount = session.Query<BlogComment>()
                    .Where(comment => comment.LastModified < now.AddHours(-1))
                    .Count();

                if (toUpdateCount == 0)
                {
                    ReportInfo("Aborting patch. All docs have been updated in the last hour");
                    return;
                }

                ReportInfo($"Number of BlogComments to patch : {toUpdateCount}");
            }

            // Where comment.LastModified < now.AddHours(-1), 
            // patch 'Rating' property based on 'likes' and 'dislikes' counters

            var script = @"from BlogComments as comment 
                           where comment.LastModified < '" + now.AddHours(-1).ToString("o") + @"'
                           update 
                           {
	                           var likes = counter(comment, 'likes');
	                           var dislikes = counter(comment, 'dislikes');
	                           var rating = ((likes / (likes + dislikes)) * 100) || 0;
	                           comment.Rating = rating;
                               comment.LastModified = new Date('" + now + @"');
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
                // ReSharper disable once ReplaceWithSingleCallToCount
                var count = session.Query<BlogComment>()
                    .Where(comment => comment.LastModified < now.AddHours(-1))
                    .Count();

                if (count != 0)
                {
                    ReportFailure("Failed. Expected to have no BlogComment docs that have " +
                                  $"LastModified < {now.AddHours(-1):o}, but found {count} such docs. ", null);
                }
                else
                {
                    ReportSuccess($"Success. All BlogComment docs now have LastModified >= {now.AddHours(-1):o}. ");
                }
            }
        }
    }
}
