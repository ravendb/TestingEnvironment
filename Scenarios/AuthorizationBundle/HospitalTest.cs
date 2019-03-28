using System;
using System.Collections.Generic;
using System.Text;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Session;
using Raven.Client.Exceptions;
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
                    ReportInfo("Creating groups with members");
                    GenerateGroups(session);
                    ReportInfo("Creating orphan users");
                    GenerateOrphanUsers(session);
                }

                try
                {
                    CleanupTestsEntities();
                }
                catch(Exception e)
                {
                    //We already cleaned the data
                    ReportInfo("Exception cleaning up: " + e);
                }

                ReportSuccess("Test was completed successfully");
            }
        }

        private void CleanupTestsEntities()
        {
            using (var session =
                DocumentStore.OpenSession(new SessionOptions {TransactionMode = TransactionMode.ClusterWide}))
            {
                var rootUserValue = session.Advanced.ClusterTransaction.GetCompareExchangeValue<string>("users/RootUser");
                session.Advanced.ClusterTransaction.DeleteCompareExchangeValue(rootUserValue.Key, rootUserValue.Index);

                foreach (var group in AllGroups)
                {
                    var value = session.Advanced.ClusterTransaction.GetCompareExchangeValue<Group.GroupVersion>(
                        "groups/" + group.Id);
                    if (value != null)
                    {
                        session.Advanced.ClusterTransaction.DeleteCompareExchangeValue(value.Key, value.Index);
                        session.Delete(group.Id);
                    }
                }

                foreach (var user in AllUsers)
                {
                    var value = session.Advanced.ClusterTransaction.GetCompareExchangeValue<int>(
                        "users/" + user.Id);
                    if (value != null)
                    {
                        session.Advanced.ClusterTransaction.DeleteCompareExchangeValue(value.Key, value.Index);
                        session.Delete(user.Id);
                    }
                }
                session.SaveChanges();
            }            
        }

        private HashSet<TestUser> OrphanUsers = new HashSet<TestUser>();
        private void GenerateOrphanUsers(AuthorizedSession session)
        {
            for (var i = 0; i < 1000; i++)
            {
                OrphanUsers.Add(GenerateRandomUser());
            }            
        }

        private int _numberOfGroups;
        private void GenerateGroups(AuthorizedSession session)
        {
            _rootGroup = GenerateGroup(session, null, "Root group");
            var prevLevelGroups = new[] {_rootGroup};
            for (var numberOfLevels = 0; numberOfLevels < 3; numberOfLevels++)
            {
                var nextLevelGroups = new List<TestGroup>();
                for (var i = 0; i < prevLevelGroups.Length; i++)
                {
                    var currGroup = prevLevelGroups[i];
                    var rand = new Random(GetStableHashCode(currGroup.Id));
                    var numberOfSubGroups = rand.Next(2, 4);
                    for (var j = 0; j < numberOfSubGroups; j++)
                    {
                        nextLevelGroups.Add(GenerateGroup(session, currGroup.Id));
                    }
                }

                prevLevelGroups = nextLevelGroups.ToArray();
            }
        }

        private HashSet<TestGroup> AllGroups = new HashSet<TestGroup>();
        private TestGroup _rootGroup; 

        private TestGroup GenerateGroup(AuthorizedSession session, string parent, string description = null)
        {
            var groupNumber = _numberOfGroups++;
            var groupName = "Group/" + groupNumber;
            var group = new TestGroup
            {
                Description = description ?? $"Group {groupNumber}",
                Id = groupName,
                Members = new HashSet<string>(),
                Permission = new Permission
                {
                    Collections = new HashSet<string>{$"Group{groupNumber}Only"},
                    Description = $"Allows access to Group{groupNumber} related documents",
                    Ids = new HashSet<string>()
                },
                Parent = parent
            };
            AllGroups.Add(group);
            session.CreateGroup(group.Id, group.Parent, group.Description, group.Permission);

            GenerateMembersForGroup(session, groupName);
            return group;
        }

        private void GenerateMembersForGroup(AuthorizedSession session, string groupName)
        {
            var random = new Random(GetStableHashCode(groupName));
            var numberOfMembers = random.Next(50, 100);
            for (int i = 0; i < numberOfMembers; i++)
            {
                var user = GenerateRandomUser();
                session.CreateUser(user.Id, user.Password);
                session.AddUserToGroup(user.Id, groupName);
                user.Groups.Add(groupName);
            }
        }

        private int UserCount { get; set; }
        private TestUser GenerateRandomUser()
        {
            var userName = "User/" + UserCount++;
            var user = new TestUser{Id = userName, Password = GenerateRandomPassword(userName),Groups = new HashSet<string>(),Permissions = new Permission()};
            AllUsers.Add(user);
            return user;
        }

        private HashSet<TestUser> AllUsers = new HashSet<TestUser>();

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