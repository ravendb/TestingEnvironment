using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Raven.Client;

namespace CorruptedCasino
{
    public static class UserOperations
    {
        public static string[] NamePool;

        public static readonly int NamePoolCount = 500;

        static UserOperations()
        {
            NamePool = new string[NamePoolCount];
            for (int i = 0; i < NamePoolCount; i++)
            {
                NamePool[i] = GenerateName(6);
            }
        }

        public static string GetName()
        {
            //return NamePool[Rand.Next(0, NamePoolCount)];
            return GenerateName(7);
        }

        //https://stackoverflow.com/a/49922533
        private static string GenerateName(int len)
        {
            Random r = new Random();
            string[] consonants = { "b", "c", "d", "f", "g", "h", "j", "k", "l", "m", "l", "n", "p", "q", "r", "s", "sh", "zh", "t", "v", "w", "x" };
            string[] vowels = { "a", "e", "i", "o", "u", "ae", "y" };
            string Name = "";
            Name += consonants[r.Next(consonants.Length)].ToUpper();
            Name += vowels[r.Next(vowels.Length)];
            int b = 2; //b tells how many times a new letter has been added. It's 2 right now because the first two letters are already in the name.
            while (b < len)
            {
                Name += consonants[r.Next(consonants.Length)];
                b++;
                Name += vowels[r.Next(vowels.Length)];
                b++;
            }

            return Name;
        }

        public static async Task<User> RegisterOrLoad(string email, string name)
        {
            using (var session = Casino.GetClusterSessionAsync)
            {
                var result = await session.Advanced.ClusterTransaction.GetCompareExchangeValueAsync<string>(email).ConfigureAwait(false);
                if (result?.Value != null)
                {
                    return await session.LoadAsync<User>(result.Value).ConfigureAwait(false);
                }
            }

            var user = new User
            {
                Email = email,
                Name = name,
                Credit = 1000
            };

            using (var session = Casino.GetClusterSessionAsync)
            {
                await session.StoreAsync(user).ConfigureAwait(false);
                session.Advanced.ClusterTransaction.CreateCompareExchangeValue(user.Email, user.Id);
                await session.SaveChangesAsync().ConfigureAwait(false);
            }

            return user;
        }

        public static async Task Delete(string email)
        {
            using (var session = Casino.GetClusterSessionAsync)
            {
                var result = await session.Advanced.ClusterTransaction.GetCompareExchangeValueAsync<string>(email).ConfigureAwait(false);
                session.Advanced.ClusterTransaction.DeleteCompareExchangeValue(email, result.Index);
                session.Delete(result.Value);
                await session.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public static async Task AddAvatar(string id, string path)
        {
            using (var stream = new FileStream(path, FileMode.Open))
            using (var session = Casino.GetSessionAsync)
            {
                session.Advanced.Attachments.Store(id, "Avatar", stream);
                await session.SaveChangesAsync().ConfigureAwait(false);
            }
        }
    }

    public class User
    {
        public string Id;
        public string Name;
        public string Email;

        public List<string> Bets = new List<string>();
        public long Credit;

        public async Task PlaceBet(string lotteryId, int[] numbers, int price)
        {
            if (price <= 0)
                throw new ArgumentException("Price must be positive number.");

            using (var session = Casino.GetSessionAsync)
            {
                var user = await session.LoadAsync<User>(Id).ConfigureAwait(false);
                if (user.Credit < price)
                    throw new InsufficientFunds("Not enough credit");

                var bet = new Bet
                {
                    UserId = Id,
                    LotteryId = lotteryId,
                    Numbers = numbers,
                    Price = price,
                    BetStatus = Bet.Status.Active
                };

                await session.StoreAsync(bet).ConfigureAwait(false);
                session.Advanced.GetMetadataFor(bet)[Constants.Documents.Metadata.Expires] = DateTime.UtcNow.AddMinutes(10);

                user.Credit -= price;
                user.Bets.Add(bet.Id);

                await Lottery.ValidateOpen(session, lotteryId).ConfigureAwait(false);
                session.CountersFor(lotteryId).Increment(DateTime.UtcNow.ToString("yyyy MMMM dd hh:mm"));

                await session.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task ChangeBet(string betId, int[] numbers, int price)
        {
            if (price <= 0)
                throw new ArgumentException("Price must be positive number.");

            using (var session = Casino.GetSessionAsync)
            {
                var bet = await session.LoadAsync<Bet>(betId).ConfigureAwait(false);

                var user = await session.LoadAsync<User>(Id).ConfigureAwait(false);
                await Lottery.ValidateOpen(session, bet.LotteryId).ConfigureAwait(false);

                user.Credit += bet.Price;
                if (user.Credit < price)
                    throw new InsufficientFunds("Not enough credit");

                bet.Price = price;
                bet.Numbers = numbers;

                user.Credit -= price;
                await session.StoreAsync(bet).ConfigureAwait(false);
                await session.StoreAsync(user).ConfigureAwait(false);
                await session.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task DeleteBet(string betId)
        {
            using (var session = Casino.GetSessionAsync)
            {
                var user = await session.LoadAsync<User>(Id).ConfigureAwait(false);
                var bet = await session.LoadAsync<Bet>(betId).ConfigureAwait(false);

                await Lottery.ValidateOpen(session, bet.LotteryId).ConfigureAwait(false);

                bet.BetStatus = Bet.Status.Deleted;

                if (user.Bets.Remove(betId))
                {
                    user.Credit += bet.Price;
                    await session.StoreAsync(bet).ConfigureAwait(false);
                    await session.StoreAsync(user).ConfigureAwait(false);
                    await session.SaveChangesAsync().ConfigureAwait(false);
                }
            }
        }
    }

    public class InsufficientFunds : Exception
    {
        public InsufficientFunds(string notEnoughCredit) : base(notEnoughCredit)
        {
        }
    }
}
