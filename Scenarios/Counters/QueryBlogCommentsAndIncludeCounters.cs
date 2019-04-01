using System;
using System.Collections.Generic;
using System.Linq;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;
using TestingEnvironment.Client;

namespace Counters
{
    public class QueryBlogCommentsAndIncludeCounters : BaseTest
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

        public QueryBlogCommentsAndIncludeCounters(string orchestratorUrl, string testName) : base(orchestratorUrl, testName, "Aviv")
        {
        }

        public override void RunActualTest()
        {
            using (DocumentStore.Initialize())
            {
                using (var session = DocumentStore.OpenSession())
                {
                    ReportInfo("Started querying docs with including counters");

                    var query = session.Query<BlogComment>()
                        .Customize(x => x.WaitForNonStaleResults(TimeSpan.FromSeconds(30)))
                        .Where(comment => comment.PostedAt > new DateTime(2017, 1, 1) && comment.Rating > 0)
                        .Include(includeBuilder =>
                            includeBuilder.IncludeAllCounters())
                        .Select(x => new ProjectionResult
                        {
                            Id = x.Id,
                            Tag = x.Tag,
                            Rating = x.Rating
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
                               "Asserting 1 <= number-of-counters <= 3 for each result " +
                               "and that all included counters are cached in session. ");

                    var numOfRequest = session.Advanced.NumberOfRequests;

                    foreach (var doc in result)
                    {
                        var all = session.CountersFor(doc.Id).GetAll();

                        if (all.Count == 0 || all.Count > 3)
                        {
                            // we queried docs where Rating > 0
                            // so each result must have at least one counter on it
                            // and we only use 3 different counter names in this collection ('likes', 'dislikes', 'total-views')

                            ReportFailure($"Failed on document '{doc.Id}'. " +
                                          $"Expected 1 <= number-of-counters <= 3, but got number-of-counters = {all.Count}", null);
                        }

                        if (session.Advanced.NumberOfRequests > numOfRequest)
                        {
                            ReportFailure($"Failed on document '{doc.Id}'. " +
                                          "Included counters were not cached in session", null);
                        }
                    }

                    ReportSuccess("Finished asserting included counters");
                }

            }
        }
    }
}
