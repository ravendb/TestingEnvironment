using System;
using System.Collections.Generic;
using System.Linq;
using Raven.Client.Documents.Linq;
using TestingEnvironment.Client;

namespace Counters
{
    public class LoadBlogCommentsAndIncludeCounters : BaseTest
    {
        public LoadBlogCommentsAndIncludeCounters(string orchestratorUrl, string testName) : base(orchestratorUrl, testName, "Aviv")
        {
        }

        public override void RunActualTest()
        {
            var ids = new List<string>();
            using (var session = DocumentStore.OpenSession())
            {
                ReportInfo("Started querying docs where PostedAt > new DateTime(2017, 1, 1) and Rating > 0");

                var query = session.Query<BlogComment>()
                    .Customize(x => x.WaitForNonStaleResults(TimeSpan.FromSeconds(30)))
                    .Where(comment => comment.PostedAt > new DateTime(2017, 1, 1) && comment.Rating > 0)
                    .Select(x => x.Id);

                var retries = 3;
                while (true)
                {
                    try
                    {
                        ids = query.ToList();
                        break;
                    }
                    catch (Exception e)
                    {
                        if (--retries > 0)
                        {
                            ReportFailure("Encounter an error when trying to query. Trying again. ", e);
                            continue;
                        }

                        ReportFailure("Failed to get query results after 3 tries", e);
                        return;
                    }
                }

                if (ids.Count == 0)
                {
                    ReportInfo("No matching results. Aborting");
                    return;
                }

                ReportInfo($"Found {ids.Count} matching results. " +
                           "Starting to load these docs and including their counters." +
                           "Asserting 1 <= number-of-counters <= 3 for each result, " +
                           "and that all included counters are cached in session. ");

            }

            foreach (var id in ids)
            {
                using (var session = DocumentStore.OpenSession())
                {
                    var doc = session.Load<BlogComment>(id, includeBuilder =>
                        includeBuilder.IncludeCounter("likes")
                            .IncludeCounter("dislikes")
                            .IncludeCounter("total-views"));

                    var all = session.CountersFor(doc).GetAll();

                    if (all.Count == 0 || all.Count > 3)
                    {
                        // we are only loading docs where Rating > 0
                        // so each doc must have at least one counter on it
                        // and we only use 3 different counter names in this collection ('likes', 'dislikes', 'total-views')

                        ReportFailure($"Failed on document '{doc.Id}'. " +
                                      $"Expected 1 <= number-of-counters <= 3, but got number-of-counters = {all.Count}",
                            null);
                    }

                    if (session.Advanced.NumberOfRequests > 1)
                    {
                        ReportFailure($"Failed on document '{doc.Id}'. " +
                                      "Included counters were not cached in session. " +
                                      $"Expected session.Advanced.NumberOfRequests = 1 but got {session.Advanced.NumberOfRequests}",
                            null);
                    }
                }

            }

            ReportSuccess("Finished asserting included counters");
        }
    }
}
