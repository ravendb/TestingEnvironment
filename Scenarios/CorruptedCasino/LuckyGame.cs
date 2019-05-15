using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Session;
using Raven.Client.Exceptions;

namespace CorruptedCasino
{
    public class Bet
    {
        public enum Status
        {
            None,
            Active,
            Deleted,
            Closed
        }

        public string Id;
        public string UserId;
        public string LotteryId;
        public int[] Numbers = new int[Lottery.NumberSize];
        public int Price;
        public Status BetStatus;
    }

    public class Lottery
    {
        [ThreadStatic]
        private static Random _rand;

        public static Random Rand => _rand ?? (_rand = new Random());

        public static int NumberSize = 5;

        public static int PoolSize = 10;

        private static readonly long WinRatio = GetBinCoeff(PoolSize, NumberSize);

        public enum LotteryStatus
        {
            None,
            Open,
            PendingResults,
            Over
        }

        public string Id;
        public DateTime DueTime;
        public int[] Result;
        public LotteryStatus Status;

        private readonly Dictionary<int, int> _collector = new Dictionary<int, int>();
        private readonly ManualResetEvent _collecting = new ManualResetEvent(false);

        public static async Task<Lottery> CreateLottery(CasinoTest casinoInstance)
        {
            var lottery = new Lottery(casinoInstance, $"Lottery/{DateTime.UtcNow}/" + Guid.NewGuid())
            {
                DueTime = DateTime.UtcNow.AddMinutes(1),
                Status = LotteryStatus.Open
            };

            using (var session = casinoInstance.GetSessionAsync)
            {
                session.Advanced.WaitForReplicationAfterSaveChanges(timeout:TimeSpan.FromSeconds(180),replicas: casinoInstance.ReplicaCount());
                await session.StoreAsync(lottery, lottery.Id);
                await session.SaveChangesAsync();
            }

            return lottery;
        }

        public Lottery()
        {
            // for de-serialize
        }

        private Lottery(CasinoTest instance, string id)
        {
            CasinoTestInstance = instance;
            Id = id;

            for (int i = 1; i <= PoolSize; i++)
            {
                _collector[i] = 0;
            }

            var subscription = CasinoTestInstance.GetDocumentStore.Subscriptions.Create<Bet>(database: CasinoTestInstance.GetDocumentStore.Database,
                predicate: (bet) => bet.LotteryId == Id);

            var worker = CasinoTestInstance.GetDocumentStore.Subscriptions.GetSubscriptionWorker<Bet>(subscription, database: CasinoTestInstance.GetDocumentStore.Database);

            worker.Run((batch) =>
            {
                foreach (var bet in batch.Items)
                {
                    if (bet.Result.BetStatus == Bet.Status.Closed)
                    {
                        CasinoTestInstance.GetDocumentStore.Subscriptions.Delete(subscription);
                        _collecting.Set();
                        return;
                    }

                    foreach (var number in bet.Result.Numbers)
                    {
                        _collector[number]++;
                    }
                }
            });
        }

        public CasinoTest CasinoTestInstance { get; set; }

        public async Task FinalizeBets()
        {
            using (var session = CasinoTestInstance.GetSessionAsync)
            {
                // ensures that no more bets accepted after this session is committed 
                session.Advanced.WaitForReplicationAfterSaveChanges(TimeSpan.FromSeconds(180), replicas: CasinoTestInstance.ReplicaCount());

                var lottery = await session.LoadAsync<Lottery>(Id);
                lottery.Status = LotteryStatus.PendingResults;

                await session.StoreAsync(new Bet
                {
                    LotteryId = Id,
                    BetStatus = Bet.Status.Closed
                }, $"close/{Id}");

                await session.SaveChangesAsync();
            }

            if (_collecting.WaitOne(TimeSpan.FromSeconds(30)) == false)
                throw new TimeoutException();
        }

        public async Task<long> GetFinalBettingReport()
        {
            using (var session = CasinoTestInstance.GetSessionAsync)
            {
                var won = session.Query<CasinoTest.BetsIndex.BetsResult, CasinoTest.BetsIndex>()
                    .Where(b => b.Won && b.LotteryId == Id)
                    .Customize(x => x.WaitForNonStaleResults(TimeSpan.FromSeconds(5))).LazilyAsync();

                var lost = session.Query<CasinoTest.BetsIndex.BetsResult, CasinoTest.BetsIndex>()
                    .Where(b => b.Won == false && b.LotteryId == Id)
                    .Customize(x => x.WaitForNonStaleResults(TimeSpan.FromSeconds(5))).LazilyAsync();

                var winners = (await won.Value).SingleOrDefault();
                var losers = (await lost.Value).SingleOrDefault();

                var realWinners = winners?.BetsId;
                if (realWinners != null)
                {
                    var winnerBets = await session.LoadAsync<Bet>(realWinners);
                    var group = winnerBets.GroupBy(w => w.Value.UserId);
                    foreach (var userGroup in group)
                    {
                        var winner = await session.LoadAsync<User>(userGroup.Key);
                        foreach (var bet in userGroup)
                        {
                            winner.Credit += bet.Value.Price * WinRatio;
                        }
                       
                        // WriteLine($"{winner.Name} has won!!");
                    }

                    await session.SaveChangesAsync();
                }
                else
                {
                    // WriteLine("No winners! the house take it all!");
                }

                return losers?.Total ?? 0 - (winners?.Total ?? 0) * WinRatio;
            }
        }

        public async Task Complete()
        {
            using (var session = CasinoTestInstance.GetSessionAsync)
            {
                session.Advanced.WaitForReplicationAfterSaveChanges(TimeSpan.FromSeconds(180), replicas: CasinoTestInstance.ReplicaCount());

                var lottery = await session.LoadAsync<Lottery>(Id);
                lottery.Result = Result;
                lottery.Status = Status;
                await session.SaveChangesAsync();
            }
        }

        public void RollTheDice()
        {
            Result = new int[NumberSize];

            /*var luckManipulation = _collector.OrderBy(x => x.Value).Select(x => x.Key).ToArray();
            for (int i = 0; i < NumberSize; i++)
            {
                int num;
                do
                {
                    num = luckManipulation[Rand.Next(0, PoolSize / 2)];
                } while (Result.Contains(num));

                Result[i] = num;
            }*/
            Result = CasinoTestInstance.RandomSequence();
            Status = LotteryStatus.Over;
        }

        public async Task AnnounceWinners()
        {
            using (var session = CasinoTestInstance.GetSessionAsync)
            {
                var winners = await RewardWinners(session);
                // WriteLine(winners.Count > 0 ? "We have a winner!!" : $"No one won the lottery {Id} :(");
            }
        }

        private async Task<HashSet<User>> RewardWinners(IAsyncDocumentSession session)
        {
            var winners = new HashSet<User>();
            var winnersBets = session.Query<Bet>()
                .Where(b => b.BetStatus == Bet.Status.Active && b.Numbers.ContainsAll(Result))
                .Include(b => b.UserId).ToArray();

            foreach (var bet in winnersBets)
            {
                var user = await session.LoadAsync<User>(bet.UserId);
                user.Credit += bet.Price * WinRatio;
                winners.Add(user);
            }
            await session.SaveChangesAsync();

            return winners;
        }

        public static async Task ValidateOpen(IAsyncDocumentSession session, string lotteryId)
        {
            var lottery = await session.LoadAsync<Lottery>(lotteryId);
            if (lottery.Status != LotteryStatus.Open)
            {
                throw new ConcurrencyException();
            }

            /*session.Advanced.Defer(new PatchCommandData(
                id: lotteryId,
                changeVector: null,
                patch:
                new PatchRequest
                {
                    Script = @"
                        if (this.Status !== 'Open')
                            throw 'Lottery is over!';
                        "
                },
                patchIfMissing: new PatchRequest
                {
                    Script = @"throw 'Lottery not found!';"
                }));*/
        }

        public static long GetBinCoeff(long N, long K)
        {
            // This function gets the total number of unique combinations based upon N and K.
            // N is the total number of items.
            // K is the size of the group.
            // Total number of unique combinations = N! / ( K! (N - K)! ).
            // This function is less efficient, but is more likely to not overflow when N and K are large.
            // Taken from:  http://blog.plover.com/math/choose.html
            //
            long r = 1;
            long d;
            if (K > N) return 0;
            for (d = 1; d <= K; d++)
            {
                r *= N--;
                r /= d;
            }
            return r;
        }
    }
}
