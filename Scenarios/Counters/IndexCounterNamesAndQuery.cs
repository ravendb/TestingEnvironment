using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Operations.Counters;
using Raven.Client.Documents.Operations.Indexes;
using TestingEnvironment.Client;

namespace Counters
{
    public class IndexQueryOnCounterNames : BaseTest
    {
        #region index 

        public class BlogCommentsIndex : AbstractIndexCreationTask<BlogComment>
        {
            public class Result
            {
                public string Id { get; set; }
                public double Rating { get; set; }
                public List<string> CounterNames { get; set; }
            }

            public BlogCommentsIndex()
            {
                Map = comments => 
                    from comment in comments
                    let counterNames = CounterNamesFor(comment)
                    select new
                    {
                        comment.Rating,
                        comment.Tag,
                        CounterNames = counterNames
                    };

                Store("CounterNames", FieldStorage.Yes);
            }
        }

        #endregion

        public IndexQueryOnCounterNames(string orchestratorUrl) : base(orchestratorUrl, "IndexQueryOnCounterNames", "Aviv")
        {
        }

        public override void RunActualTest()
        {
            try
            {
                var indexNames = DocumentStore.Maintenance.Send(new GetIndexNamesOperation(0, int.MaxValue));

                if (indexNames.Contains(nameof(BlogCommentsIndex)) == false)
                {
                    ReportInfo("Deploying index 'BlogCommentsIndex' to server");

                    new BlogCommentsIndex().Execute(DocumentStore);

                    Thread.Sleep(5000);
                }

                using (var session = DocumentStore.OpenSession())
                {
                    ReportInfo("Started querying index 'BlogCommentsIndex' and projecting counters");

                    var result = session.Query<BlogCommentsIndex.Result, BlogCommentsIndex>()
                        .Customize(x => x.WaitForNonStaleResults(TimeSpan.FromSeconds(30)))
                        .Where(comment => comment.Rating > 0 && comment.CounterNames.Contains("likes"))
                        .Select(x => new
                        {
                            Id = x.Id,
                            CounterNames = x.CounterNames,
                            Likes = session.CountersFor(x).Get("likes")
                        })
                        .ToList();

                    if (result.Count == 0)
                    {
                        ReportInfo("No matching results. Aborting");
                        return;
                    }

                    ReportInfo($"Found {result.Count} matching results. Asserting valid results. ");

                    foreach (var doc in result)
                    {
                        if (doc.Likes == null)
                        {
                            // we queried for docs that have 'likes' in counter names

                            ReportFailure($"Failed on document {doc.Id}. counter 'likes' should not be null. Aborting", null);
                            return;
                        }

                        if (doc.CounterNames.Count < 2 || doc.CounterNames.Count > 3)
                        {
                            ReportFailure($"Failed on document {doc.Id}. Expected to have 2 or 3 counters, " +
                                          $"but got : {doc.CounterNames.Count}. Aborting", null);
                            return;
                        }

                        foreach (var name in doc.CounterNames)
                        {
                            if (name.In("likes", "dislikes", "total-views") == false)
                            {
                                ReportFailure($"Failed on document {doc.Id}. " +
                                              $"Unexpected counter name : {doc.CounterNames.Count}. Aborting", null);
                                return;
                            }
                        }

                        var likes = DocumentStore.Operations.Send(new GetCountersOperation(doc.Id, "likes"))?.Counters[0]?.TotalValue;

                        if (doc.Likes > likes)
                        {
                            ReportFailure($"Failed on document '{doc.Id}'. " +
                                          "Expected its 'likes' counter to have a value greater than or equal to the value from projection result." +
                                          $"Got : new-value = '{likes}', old value = '{doc.Likes}'. Aborting", null);
                            return;
                        }
                    }

                    ReportSuccess("Finished asserting valid results");
                }
            }
            catch (Exception e)
            {
                ReportFailure("An error occurred during test. Aborting ", e);
            }

        }
    }
}
