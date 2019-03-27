using System;
using System.Linq;
using TestingEnvironment.Client;

namespace BlogComment
{
    class Program
    {
        static void Main(string[] args)
        {
            using (var client = new PutCommentsTest(args[0], "PutCommentsTest"))
            {
                client.Initialize();
                client.RunTest();
            }
        }

        public class PutCommentsTest : BaseTest
        {
            #region DTOs

            public class BlogComment
            {
                public string Id { get; set; }
                public string Author { get; set; }
                public string Tag { get; set; }
                public string Text { get; set; }
                public double Rating { get; set; }
                public DateTime PostedAt { get; set; }
                public DateTime LastModified { get; set; }
            }

            public enum CommentTag
            {
                Politics = 0,
                Economics = 1,
                Sports = 2,
                Entertainment = 3,
                Science = 4,
                Music = 5,
                Religion = 6,
                Food = 7,
                Tech = 8,
                Other = 9
            }

            #endregion

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
                        var rnd = new Random();

                        var randTag = ((CommentTag)rnd.Next(0, 9)).ToString();

                        var randYearOffset = rnd.Next(0, 4);
                        var randMonthOffset = rnd.Next(0, 11);
                        var randDayOffset = rnd.Next(0, 30);
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
}
