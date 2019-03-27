using System;
using System.Text;
using Raven.Client.Documents.Indexes;
using TestingEnvironment.Client;

namespace AuthorizationBundle
{
    internal class HospitalTest : BaseTest
    {
        public HospitalTest(string orchestratorUrl, string testName) : base(orchestratorUrl, testName, "Tal")
        {

        }

        public override void RunActualTest()
        {
            using (DocumentStore)
            {
                ReportInfo("Creating root user");
                _rootUser = GenerateRandomUser();
                AuthorizedSession.CreateRootUser(DocumentStore, _rootUser.Id, _rootUser.Password);
                using (var session = AuthorizedSession.OpenSession(DocumentStore, _rootUser.Id, _rootUser.Password))
                {

                }
                ReportSuccess("Test was completed successfully");
            }
        }
        private int UserCount { get; set; }
        private TestUser GenerateRandomUser()
        {
            var userName = "User/" + UserCount++;
            return new TestUser{Id = userName, Password = GenerateRandomPassword(userName) };
        }

        public static int GetStableHashCode(string str)
        {
            unchecked
            {
                int hash1 = 5381;
                int hash2 = hash1;

                for (int i = 0; i < str.Length && str[i] != '\0'; i += 2)
                {
                    hash1 = ((hash1 << 5) + hash1) ^ str[i];
                    if (i == str.Length - 1 || str[i + 1] == '\0')
                        break;
                    hash2 = ((hash2 << 5) + hash2) ^ str[i + 1];
                }

                return hash1 + (hash2 * 1566083941);
            }
        }

        private string GenerateRandomPassword(string userName)
        {
            var hash = GetStableHashCode(userName);
            var random = new Random(hash);
            var passwordLength = random.Next(8,16);
            var sb = new StringBuilder();
            for (var i = 0; i < passwordLength; i++)
            {
                sb.Append(Abc[random.Next(Abc.Length)]);
            }

            return sb.ToString();
        }

        private static readonly string Abc = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*()";
        private TestUser _rootUser;
    }
}