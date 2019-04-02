using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Polly;
using Raven.Client.Documents;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Operations.Expiration;
using Raven.Client.Documents.Operations.Revisions;
using Raven.Client.Documents.Session;
using Raven.Client.Exceptions;
using Raven.Client.Exceptions.Documents.Patching;
using Raven.Client.ServerWide;
using Raven.Client.ServerWide.Operations;
using TestingEnvironment.Client;
using TestingEnvironment.Common;

namespace CorruptedCasino
{
    public class Casino : BaseTest
    {
        public static readonly Casino Instance;

        private static bool Local = false;

        static Casino()
        {
            var url = Local ? "http://localhost:8080" : "http://10.0.0.69:8090";
            Instance = new Casino(url, "CorruptedCasino", "Karmel");
            Instance.Initialize();
            Name = Store.Database;
        }

        private Casino(string orchestratorUrl, string testName, string author) : base(orchestratorUrl, testName, author)
        {
        }

        public override void Initialize()
        {
            if (Local == false)
            {
                base.Initialize();
                return;
            }

            Store = new DocumentStore
            {
                Urls = new[] { "http://localhost:8080" },
                Database = "Game"
            };
            Store.Initialize();
        }

        public static IDocumentStore Store;

        public static readonly string Name;

        public static IAsyncDocumentSession GetClusterSessionAsync => Store.OpenAsyncSession(new SessionOptions
        {
            TransactionMode = TransactionMode.ClusterWide
        });

        public static IAsyncDocumentSession GetSessionAsync => Store.OpenAsyncSession();

        public static async Task Bootstrap()
        {
            if (Local)
            {
                try
                {
                    Store.Maintenance.Server.Send(new CreateDatabaseOperation(new DatabaseRecord(Name)));
                }
                catch
                {
                    // ignore
                }
            }

            try
            {
                new BetsIndex().Execute(Store);
            }
            catch
            {
                // ignore
            }

            await ConfigureExpiration();
            await ConfigureRevisions();
        }

        private static async Task ConfigureExpiration()
        {
            await Store.Maintenance.SendAsync(new ConfigureExpirationOperation(new ExpirationConfiguration
            {
                Disabled = false,
                DeleteFrequencyInSec = 600
            }));
        }

        private static async Task ConfigureRevisions()
        {
            var config = new RevisionsConfiguration
            {
                Collections = new Dictionary<string, RevisionsCollectionConfiguration>
                {
                    ["Users"] = new RevisionsCollectionConfiguration
                    {
                        PurgeOnDelete = true,
                        MinimumRevisionAgeToKeep = TimeSpan.FromSeconds(180)
                    },
                    ["Bets"] = new RevisionsCollectionConfiguration()
                }
            };

            await Store.Maintenance.SendAsync(new ConfigureRevisionsOperation(config));
        }

        public class BetsIndex : AbstractIndexCreationTask<Bet, BetsIndex.BetsResult>
        {
            public class BetsResult
            {
                public string LotteryId;

                public long Total;

                public bool Won;

                public string[] BetsId;
            }

            public BetsIndex()
            {
                Map = bets => from bet in bets
                    let lottery = LoadDocument<Lottery>(bet.LotteryId)
                    where bet.BetStatus == Bet.Status.Active
                    select new
                    {
                        bet.LotteryId,
                        Total = bet.Price,
                        Won = lottery != null && lottery.Status == Lottery.LotteryStatus.Over && lottery.Result.OrderBy(x => x).SequenceEqual(bet.Numbers.OrderBy(x => x)),
                        BetsId = new[] { bet.Id }
                    };

                Reduce = results => from result in results
                    group result by new { result.LotteryId, result.Won } into g
                    select new
                    {
                        LotteryId = g.Key.LotteryId,
                        Won = g.Key.Won,
                        Total = g.Sum(x => x.Total),
                        BetsId = g.Key.Won ? g.Select(b => b.BetsId[0]).ToArray() : null
                    };
            }
        }

        public static int[] RandomSequence()
        {
            var result = new int[Lottery.NumberSize];
            for (int i = 0; i < Lottery.NumberSize; i++)
            {
                int num;
                do
                {
                    num = Lottery.Rand.Next(0, Lottery.PoolSize);

                } while (result.Contains(num));

                result[i] = num;
            }
            return result.OrderBy(x=>x).ToArray();
        }

        public static Task StartPlacingBets(Lottery lottery, int num)
        {
            var tasks = new List<Task>();
            for (int i = 0; i < num; i++)
            {
                var t = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(Lottery.Rand.Next(500, 1000));
                        var name = UserOperations.GetName();
                        var user = await UserOperations.RegisterOrLoad($"{name}@karmel.com", name);
                        for (int j = 0; j < 10; j++)
                        {
                            await Task.Delay(Lottery.Rand.Next(500, 1000));
                            await user.PlaceBet(lottery.Id, RandomSequence(), Lottery.Rand.Next(1, 10));
                        }
                    }
                    catch (ConcurrencyException)
                    {
                        // expected
                    }
                    catch (InsufficientFunds)
                    {
                        // expected
                    }
                    catch (JavaScriptException e) when(e.Message.Contains("Lottery is over!"))
                    {
                        // expected
                    }
                    catch (Exception e)
                    {
                        // not expected at all!
                        Console.WriteLine(e);
                        throw;
                    }
                });
                tasks.Add(t);
            }

            return Task.WhenAll(tasks);
        }

        public static void ChangeBets(Lottery lottery, int num)
        {
            for (int i = 0; i < num; i++)
            {
                Task.Run(async () =>
                {
                    await Task.Delay(Lottery.Rand.Next(100, 1000));
                    var name = UserOperations.GetName();
                    var user = await UserOperations.RegisterOrLoad($"{name}@karmel.com", name);
                    while (true)
                    {
                        await Task.Delay(Lottery.Rand.Next(100, 500));
                        await user.PlaceBet(lottery.Id, RandomSequence(), Lottery.Rand.Next(1, 10));
                    }
                });
            }
        }

        public static int ReplicaCount()
        {
            return Store.Maintenance.Server.Send(new GetDatabaseRecordOperation(Store.Database)).Topology.Count - 1;
        }

        public static async Task CreateAndRunLottery()
        {
            var policy = Policy.Handle<TimeoutException>().Retry(5);
            Console.Write("Creating Lottery ... ");
            var lottery = await Lottery.CreateLottery();

            Instance.ReportEvent(new EventInfo
            {
                Message = $"Lottery {lottery.Id} was created and will overdue at {lottery.DueTime}",
                Type = EventInfo.EventType.Info
            });
            Console.WriteLine($"Done {lottery.Id}");

            Console.WriteLine("Start betting");
            var t = StartPlacingBets(lottery, 1000);
            Instance.ReportEvent(new EventInfo
            {
                Message = $"Start betting in lottery {lottery.Id}",
                Type = EventInfo.EventType.Info
            });
            var sleep = (int)(lottery.DueTime - DateTime.UtcNow).TotalMilliseconds;
            if (sleep > 10)
                await Task.Delay(sleep);

            Instance.ReportEvent(new EventInfo
            {
                Message = $"Lottery {lottery.Id} is overdue",
                Type = EventInfo.EventType.Info
            });

            Console.Write("Finalize bets ... ");
            await policy.Execute(lottery.FinalizeBets);
            Console.WriteLine("Done");

            Instance.ReportEvent(new EventInfo
            {
                Message = $"Rolling the dice for lottery {lottery.Id}",
                Type = EventInfo.EventType.Info
            });
            Console.WriteLine("Rolling the Dice ... ");
            lottery.RollTheDice();
            Console.Write("Completing the lottery ... ");

            await policy.Execute(lottery.Complete);
            Instance.ReportEvent(new EventInfo
            {
                Message = $"Lottery {lottery.Id} is completed",
                Type = EventInfo.EventType.Info
            });
            Console.WriteLine("Done");

            var profit = await policy.Execute(lottery.GetFinalBettingReport);
            Instance.ReportEvent(new EventInfo
            {
                Message = $"Report for lottery {lottery.Id} was generated and winners were rewarded.",
                Type = EventInfo.EventType.TestSuccess
            });
            Console.WriteLine(profit);

            await t;
        }

        protected override EventResponse ReportEvent(EventInfo eventInfo) => Local ? new EventResponse() : base.ReportEvent(eventInfo);

        public override void RunActualTest()
        {
            for (int i = 0; i < 10000; i++)
            {
                var t = Task.Run(CreateAndRunLottery);

                try
                {
                    t.Wait();
                }
                catch (Exception e)
                {
                    ReportFailure("Lottery failed", e);
                }
            }
        }
    }
}
