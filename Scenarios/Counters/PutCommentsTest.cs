using System;
using System.Linq;
using TestingEnvironment.Client;

namespace Counters
{
    public class PutCommentsTest : BaseTest
    {
        public PutCommentsTest(string orchestratorUrl, string testName) : base(orchestratorUrl, testName, "Aviv")
        {
        }

        public override void RunActualTest()
        {
            using (DocumentStore.Initialize())
            {
                using (var session = DocumentStore.OpenSession())
                {
                    var count = session.Query<BlogComment>().Count();
                    if (count >= 10 * 1024)
                    {
                        ReportInfo("Aborting BlogComment documents insertion, we already have enough docs");
                        return;
                    }
                }

                ReportInfo("Inserting BlogComment documents");

                var numOfRetries = 3;
                while (true)
                {
                    try
                    {
                        PutBlogCommentDocs();
                    }
                    catch (Exception e)
                    {
                        if (--numOfRetries > 0)
                            continue;

                        ReportFailure("Failed to store BlogComment documents after 3 tries. Aborting", e);
                        return;
                    }

                    break;
                }

                ReportSuccess("Finished inserting documents");
            }
        }

        private void PutBlogCommentDocs()
        {
            using (var bulk = DocumentStore.BulkInsert())
            {
                // bulk insert 1000 BlogComment docs

                for (int i = 0; i < 1024; i++)
                {
                    var randTag = ((CommentTag)Random.Next(0, 9)).ToString();

                    var randYearOffset = Random.Next(0, 4);
                    var randMonthOffset = Random.Next(0, 11);
                    var randDayOffset = Random.Next(0, 30);
                    var randDate = DateTime.Today.ToUniversalTime()
                        .AddYears(-randYearOffset)
                        .AddMonths(-randMonthOffset)
                        .AddDays(-randDayOffset);

                    var comment = new BlogComment
                    {
                        PostedAt = randDate,
                        LastModified = DateTime.UtcNow.AddYears(-1),
                        Author = "Jon Doe",
                        Text = "Some text",
                        Tag = randTag
                    };

                    bulk.Store(comment);
                }
            }
        }
    }

}
