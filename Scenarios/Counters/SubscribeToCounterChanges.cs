using System;
using System.Collections.Concurrent;
using System.Linq;
using Raven.Client.Documents.Changes;
using TestingEnvironment.Client;

namespace Counters
{
    public class SubscribeToCounterChanges : BaseTest
    {
        public SubscribeToCounterChanges(string orchestratorUrl, string testName, int round) : base(orchestratorUrl, testName, "Aviv", round)
        {
        }

        public override void RunActualTest()
        {
            string docId;
            using (var session = DocumentStore.OpenSession())
            {
                var q = session
                    .Query<BlogComment>()
                    .FirstOrDefault(c => c.Rating > 0);

                if (q == null)
                {
                    ReportInfo("No document with Rating > 0. Aborting test");
                    return;
                }

                docId = q.Id;
            }

            var counterName = "likes";
            var list = new BlockingCollection<CounterChange>();
            var taskObservable = DocumentStore.Changes();
            taskObservable.EnsureConnectedNow().Wait();
            var success = false;
            try
            {

                ReportInfo($"Subscribing to counter changes via Changes API for counter {counterName} of document {docId}. ");

                var observableWithTask = taskObservable.ForCounterOfDocument(docId, counterName);

                observableWithTask.Subscribe(list.Add);
                observableWithTask.EnsureSubscribedNow().Wait();

                using (var session = DocumentStore.OpenSession())
                {
                    ReportInfo($"Incrementing counter {counterName} of document {docId}. ");

                    session.CountersFor(docId).Increment(counterName);
                    session.CountersFor(docId).Increment("total-views");
                    session.SaveChanges();
                }

                if (list.TryTake(out var counterChange, TimeSpan.FromMinutes(1)) == false)
                {
                    ReportFailure("Failed to get counter change after waiting for 1 minute. Aborting", null);
                }

                if (AssertCounterChange(counterChange, docId, counterName) == false)
                    return;

                ReportInfo("Asserted valid counter changes. "); // success
                success = true;

                var value = counterChange.Value;

                using (var session = DocumentStore.OpenSession())
                {
                    ReportInfo($"Incrementing counter {counterName} of document {docId}. ");

                    session.CountersFor(docId).Increment("likes");
                    session.CountersFor(docId).Increment("total-views");

                    session.SaveChanges();
                }

                if (list.TryTake(out counterChange, TimeSpan.FromMinutes(1)) == false)
                {
                    success = false;
                    ReportFailure("Failed to get counter change after waiting for 1 minute. Aborting", null);
                }

                if (AssertCounterChange(counterChange, docId, counterName, value) == false)
                    return;

                ReportInfo("Asserted valid counter changes. ");

            }
            catch (Exception e)
            {
                success = false;
                ReportFailure("An error occurred while trying to increment " +
                              "counter and wait for counter changes. Aborting", e);
                throw;
            }
            finally
            {
                if (success)
                {
                    ReportSuccess("Test ended successfully");
                }
                taskObservable?.Dispose();
            }
        }

        private bool AssertCounterChange(CounterChange counterChange, string docId, string counterName, long? val = null)
        {
            if (counterChange.DocumentId != docId)
            {
                ReportFailure($"Got the wrong counter change. Subscribed to counter changes for document '{docId}' " +
                              $"but got counter change for document '{counterChange.DocumentId}'. Aborting", null);
                return false;
            }


            if (val == null)
            {
                if (counterChange.Type != CounterChangeTypes.Increment &&
                    counterChange.Type != CounterChangeTypes.Put)
                {
                    ReportFailure("Got the wrong counter change type. " +
                                  "Expected 'CounterChangeTypes.Increment' or 'CounterChangeTypes.Put' " +
                                  $"but got : '{counterChange.Type}'. Aborting", null);
                    return false;
                }
            }
            else
            {
                if (counterChange.Type != CounterChangeTypes.Increment)
                {
                    ReportFailure("Got the wrong counter change type. " +
                                  $"Expected 'CounterChangeTypes.Increment' but got : '{counterChange.Type}'. Aborting", null);
                    return false;
                }
            }

            if (counterChange.Name != counterName)
            {
                ReportFailure("Got wrong counter change. " +
                              $"Subscribed to counter changes for counter '{counterName}' " +
                              $"but got counter change for : '{counterChange.Name}'. Aborting", null);
                return false;
            }

            if (val != null && val >= counterChange.Value)
            {
                ReportFailure("Got wrong counter-change value. " +
                              "Expected old value < counter-change value but got : " +
                              $"old value : '{val}', counter-change value: '{counterChange.Value}'. Aborting", null);
                return false;
            }

            return true;
        }
    }
}
