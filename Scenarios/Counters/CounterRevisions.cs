using System;
using System.Linq;
using System.Threading;
using Raven.Client;
using Raven.Client.Documents.Operations.Revisions;
using Raven.Client.Documents.Session;
using Raven.Client.ServerWide.Operations;
using TestingEnvironment.Client;

namespace Counters
{
    public class CounterRevisions : BaseTest
    {
        public CounterRevisions(string orchestratorUrl, string testName, int round) : base(orchestratorUrl, testName, "Aviv", round)
        {
        }

        public override void RunActualTest()
        {
            var dbRecord = DocumentStore.Maintenance.Server.Send(new GetDatabaseRecordOperation(DocumentStore.Database));

            var revisionConfig = dbRecord.Revisions;

            if (revisionConfig == null)
            {
                revisionConfig = new RevisionsConfiguration();                
            }
            if (revisionConfig.Collections == null)
            {
                revisionConfig.Collections = new System.Collections.Generic.Dictionary<string, RevisionsCollectionConfiguration>();
            }

            if (revisionConfig.Collections.ContainsKey("BlogComments") == false &&
                (revisionConfig.Default == null || revisionConfig.Default.Disabled))
            {
                revisionConfig.Collections.Add("BlogComments", new RevisionsCollectionConfiguration
                {
                    Disabled = false
                });

                ReportInfo("Adding revision configuration for 'BlogComments' collection");

                DocumentStore.Maintenance.Send(new ConfigureRevisionsOperation(revisionConfig));

                Thread.Sleep(5000);
            }

            string docId;
            using (var session = DocumentStore.OpenSession())
            {
                // query for old comment that has a counter on it 

                ReportInfo("Querying for old blog comment that has counters on it");

                var doc = session.Query<BlogComment>()
                    .Customize(x => x.WaitForNonStaleResults(TimeSpan.FromSeconds(30)))
                    .FirstOrDefault(comment => comment.Rating > 0 && comment.PostedAt < new DateTime(2017, 1, 1));

                if (doc == null)
                {
                    ReportInfo("Didn't find a matching doc. Aborting test");
                    return;
                }

                docId = doc.Id;

                ReportInfo($"Incrementing counter 'likes' and updating LastModified on doc {docId}.");

                session.CountersFor(doc).Increment("likes");
                session.CountersFor(doc).Increment("total-views");
                doc.LastModified = DateTime.UtcNow;
                
                session.SaveChanges();

            }

            using (var session = DocumentStore.OpenSession())
            {
                ReportInfo($"Trying to get revision for doc '{docId}'");

                var blogCommentRevisions = session.Advanced.Revisions.GetFor<BlogComment>(docId);

                if (blogCommentRevisions.Count == 0)
                {
                    ReportFailure($"Failed to get revision for document '{docId}'", null);
                }

                ReportInfo($"Successfully got document revision for document '{docId}'");

                var md = session.Advanced.GetMetadataFor(blogCommentRevisions[0]);

                if (md.ContainsKey(Constants.Documents.Metadata.RevisionCounters) == false)
                {
                    ReportFailure($"Failed. Expected to have counter-snapshot in metadata of revision document '{blogCommentRevisions[0].Id}'", null);
                    return;
                }

                var revisionCounters = (IMetadataDictionary)md[Constants.Documents.Metadata.RevisionCounters];

                if (revisionCounters == null || revisionCounters.Count == 0)
                {
                    ReportFailure("Failed. counter-snapshot array is empty", null);
                    return;
                }

                if (revisionCounters.ContainsKey("likes") == false)
                {
                    ReportFailure("Failed. counter-snapshot array does not contain counter 'likes'", null);
                    return;
                }
            }

            ReportSuccess("Finished asserting counter revisions");
        }
    }
}
