using System;
using System.Collections.Generic;
using System.Linq;
using Raven.Client.Documents.Operations;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Queries;
using TestingEnvironment.Client;

namespace Counters
{
    public class IncrementCountersByPatch : BaseTest
    {
        public IncrementCountersByPatch(string orchestratorUrl, string testName) : base(orchestratorUrl, testName, "Aviv")
        {
        }

        public override void RunActualTest()
        {
            string randTag1;
            string randTag2;
            var dict = new Dictionary<string, (long? LikesCount, long? TotalViews)>();

            IRavenQueryable<BlogComment> query;
            int toUpdateCount;

            using (var session = DocumentStore.OpenSession())
            {
                ReportInfo("Starting Stream query on BlogComments collection");

                // choose 2 different random tags

                randTag1 = ((CommentTag)Random.Next(0, 9)).ToString();
                do
                {
                    randTag2 = ((CommentTag)Random.Next(0, 9)).ToString();
                } while (randTag2 == randTag1);

                query = session.Query<BlogComment>()
                    .Where(comment => comment.Rating > 50 && 
                                      comment.Tag.In(new[] {randTag1, randTag2}));

                toUpdateCount = query.Count();
                if (toUpdateCount == 0)
                {
                    ReportInfo("Aborting. No BlogComments to update");
                    return;
                }

                ReportInfo("Collecting old counter values by stream query");

                var projection = query.Select(c => new
                {
                    Likes = RavenQuery.Counter("likes"),
                    TotalViews = RavenQuery.Counter("total-views")
                });

                var stream = session.Advanced.Stream(projection);

                try
                {
                    while (stream.MoveNext())
                    {
                        if (stream.Current.Document.Likes == null)
                        {
                            ReportFailure($"Failed on document '{stream.Current.Id}'. " +
                                          "Invalid data - counter 'likes' is missing while the parent document has 'Rating' > 0. Aborting", null);
                        }

                        if (stream.Current.Document.TotalViews == null)
                        {
                            ReportFailure($"Failed on document '{stream.Current.Id}'. " +
                                          "Invalid data - counter 'total-views' is missing while the parent document has 'Rating' > 0. Aborting", null);
                        }

                        dict.Add(stream.Current.Id, (stream.Current.Document.Likes, stream.Current.Document.TotalViews));
                    }
                }
                catch (Exception e)
                {
                    ReportFailure("An error occurred during stream query. Aborting. ", e);
                    return;
                }

            }

            var script = @"from BlogComments as comment "+
                         $"where comment.Rating > 50 and comment.Tag in('{randTag1}', '{randTag2}')" + @"
                           update 
                           {
	                           incrementCounter(comment, 'total-views');
	                           incrementCounter(comment, 'likes');                               
                           }";

            ReportInfo($"Starting PatchByQueryOperation. Number of BlogComments to patch : {toUpdateCount}");

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
                    ReportFailure("Failed to complete PatchByQueryOperation after waiting for 2 minutes. Aborting.", e2);
                    return;
                }
            }

            ReportInfo("Finished PatchByQueryOperation on BlogComments. " +
                       "Asserting counter values");

            using (var session = DocumentStore.OpenSession())
            {
                var projection = query.Select(c => new
                {
                    Likes = RavenQuery.Counter("likes"),
                    TotalViews = RavenQuery.Counter("total-views")
                });

                var stream = session.Advanced.Stream(projection);

                try
                {
                    while (stream.MoveNext())
                    {
                        if (dict.TryGetValue(stream.Current.Id, out var oldCounterValues) == false)
                        {
                            // recently added result, skip assertion
                            continue;
                        }

                        if (stream.Current.Document.Likes <= oldCounterValues.LikesCount)
                        {
                            ReportFailure($"Failed on counter 'likes' of document {stream.Current.Id}. " +
                                          "Expected old value < new value, but got : " +
                                          $"old value = {oldCounterValues.LikesCount}, new value = {stream.Current.Document.Likes}", null);
                            return;
                        }

                        if (stream.Current.Document.TotalViews <= oldCounterValues.TotalViews)
                        {
                            ReportFailure($"Failed on counter 'total-views' of document {stream.Current.Id}. " +
                                          "Expected old value < new value, but got : " +
                                          $"old value = {oldCounterValues.TotalViews}, new value = {stream.Current.Document.TotalViews}", null);
                            return;
                        }
                    }

                }
                catch (Exception e)
                {
                    ReportFailure("An error occurred during stream query. Aborting. ", e);
                    return;
                }

                ReportSuccess("Finished asserting counter values");
            }
        }
    }
}
