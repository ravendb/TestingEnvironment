using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TestingEnvironment.Client;

namespace Counters
{
    public class PutCountersOnCommentsBasedOnTopic : BaseTest
    {
        public PutCountersOnCommentsBasedOnTopic(string orchestratorUrl, string testName) : base(orchestratorUrl, testName, "Aviv")
        {
        }

        public override void RunActualTest()
        {
            using (DocumentStore.Initialize())
            {
                var viewed = new List<string>();
                var liked = new List<string>();
                var disliked = new Dictionary<string, bool>();

                using (var session = DocumentStore.OpenSession())
                {
                    // query random 128 BlogComment docs

                    var query = session.Query<BlogComment>();
                    var count = query.Count();
                    var take = 128;

                    var skip = count - take < 0
                        ? 0
                        : Random.Next(0, count - take);

                    ReportInfo("Started querying random 128 docs");

                    var streamQuery = session.Advanced.Stream(query.Skip(skip).Take(take));

                    while (streamQuery.MoveNext())
                    {
                        var comment = streamQuery.Current.Document;

                        viewed.Add(comment.Id);

                        switch (comment.Tag)
                        {
                            // +1 if topic is 'Music' 'Sports' or 'Tech'
                            case nameof(CommentTag.Music):
                            case nameof(CommentTag.Sports):
                            case nameof(CommentTag.Tech):
                                liked.Add(comment.Id);
                                break;

                            // -100 if topic is 'Religion' or 'Politics'
                            case nameof(CommentTag.Religion):
                            case nameof(CommentTag.Politics):
                                disliked.Add(comment.Id, true);
                                break;

                            // -1 if it's not one of the above topics
                            default:
                                disliked.Add(comment.Id, false);
                                break;
                        }
                    }

                }

                ReportInfo("Started incrementing their counters based on topic");

                using (var session = DocumentStore.OpenSession())
                {
                    // increment counter 'total views' for each comment

                    foreach (var id in viewed)
                    {
                        session.CountersFor(id).Increment("total-views");
                    }

                    session.SaveChanges();
                }

                using (var session = DocumentStore.OpenSession())
                {
                    // increment counter 'likes' for each liked comment

                    foreach (var id in liked)
                    {
                        session.CountersFor(id).Increment("likes");
                    }

                    // increment counter 'dislikes' for each disliked comment

                    foreach (var kvp in disliked)
                    {
                        session.CountersFor(kvp.Key).Increment("dislikes", kvp.Value ? 100 : 1);
                    }

                    session.SaveChanges();
                }

                ReportInfo("Finished incrementing counters based on topic");

                AssertCounterValue(viewed, "total-views");
                AssertCounterValue(liked, "likes");
                AssertCounterValue(disliked.Keys.ToList(), "dislikes");

                ReportSuccess("Finished asserting counter values");
            }
        }

        public void AssertCounterValue(List<string> ids, string counterName)
        {
            foreach (var id in ids)
            {
                var retries = 3;
                while (true)
                {
                    using (var session = DocumentStore.OpenSession())
                    {
                        var counterValue = session.CountersFor(id).Get(counterName);

                        // assert counter value > 0

                        if (counterValue == null || counterValue < 1)
                        {
                            if (--retries == 0)
                            {
                                ReportFailure($"Failed on counter {counterName} of document '{id}' (after 3 retries). " +
                                              $"Expected counter value > 0 but got :  '{(counterValue == null ? "null" : counterValue.ToString())}'", null);
                                return;
                            }

                            Thread.Sleep(10000);

                            continue;
                        }

                        break;
                    }
                }
            }
        }
    }
}
