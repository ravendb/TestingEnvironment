using System;
using System.Collections.Generic;
using System.Linq;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Queries;
using TestingEnvironment.Client;

namespace Counters
{
    public class QueryBlogCommentsByTag : BaseTest
    {
        #region DTO

        public class ProjectionResult
        {
            public string Id { get; set; }
            public string Tag { get; set; }
            public double Rating { get; set; }
            public long? Likes { get; set; }
            public long? Dislikes { get; set; }
            public long? TotalViews { get; set; }
        }

        #endregion

        public QueryBlogCommentsByTag(string orchestratorUrl, string testName) : base(orchestratorUrl, testName, "Aviv")
        {
        }

        public override void RunActualTest()
        {
            using (DocumentStore.Initialize())
            {
                using (var session = DocumentStore.OpenSession())
                {
                    ReportInfo("Started querying docs where Tag in ('Sports', 'Music', 'Tech')");

                    var query = session.Query<BlogComment>()
                        .Customize(x => x.WaitForNonStaleResults(TimeSpan.FromSeconds(30)))
                        .Where(comment => comment.Tag.In(new[] { "Sports", "Music", "Tech" }))
                        .Select(x => new ProjectionResult
                        {
                            Id = x.Id,
                            Tag = x.Tag,
                            Rating = x.Rating,
                            Likes = RavenQuery.Counter("likes"),
                            Dislikes = RavenQuery.Counter("dislikes"),
                            TotalViews = RavenQuery.Counter("total-views")
                        });

                    List<ProjectionResult> result;
                    var retries = 3;
                    while (true)
                    {
                        try
                        {
                            result = query.ToList();
                            break;
                        }
                        catch (Exception e)
                        {
                            if (--retries > 0)
                                continue;

                            ReportFailure("Failed to get query results after 3 tries", e);
                            return;
                        }
                    }

                    if (result.Count == 0)
                    {
                        ReportInfo("No matching results. Aborting");
                        return;
                    }

                    ReportInfo($"Found {result.Count} matching results. " +
                                "Asserting valid counter values");

                    foreach (var doc in result)
                    {
                        // assert likes <= total views

                        if (doc.Likes.HasValue &&
                            (doc.TotalViews == null || doc.Likes > doc.TotalViews))
                        {
                            ReportFailure($"Failed on doc {doc.Id} with tag {doc.Tag}. " +
                                          "Expected 'likes' <= 'total-views' but got " +
                                          $"'likes' : {doc.Likes}, total-views : {doc.TotalViews}.", null);
                            return;
                        }

                        // assert likes <= total views

                        if (doc.Dislikes.HasValue &&
                            (doc.TotalViews == null || doc.Dislikes > doc.TotalViews))
                        {
                            ReportFailure($"Failed on doc {doc.Id} with tag {doc.Tag}. " +
                                              "Expected 'dislikes' <= 'total-views' but got " +
                                              $"'dislikes' : {doc.Dislikes}, total-views : {doc.TotalViews}.", null);
                            return;
                        }
                    }

                    ReportSuccess("Finished asserting valid counter values");
                }

            }
        }
    }
}
