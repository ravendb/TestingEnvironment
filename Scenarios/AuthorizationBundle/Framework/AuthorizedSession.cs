using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Isopoh.Cryptography.Argon2;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Operations.CompareExchange;
using Raven.Client.Documents.Session;
using Raven.Client.Exceptions;

namespace AuthorizationBundle
{
    public sealed class AuthorizedSession : IDisposable
    {
        public const string UserPrefix = "users/";
        public const string GroupPrefix = "groups/";
        private IDocumentSession _session;

        private AuthorizedSession(IDocumentStore store, IDocumentSession session, AuthorizedUser authorizedUser)
        {
            _store = store;
            _session = session;
            _authorizedUser = authorizedUser;
        }

        public static AuthorizedSession OpenSession(IDocumentStore store , string userId, string password)
        {
            IDocumentSession session = null;
            try
            {
                session = store.OpenSession();                
                var user = session.Load<AuthorizedUser>(userId);
                if (user == null)
                    ThrowNoPermissions(userId);
                //TODO:change to graph query
                if (CheckValidAuthorizedUser(userId, password, user.PasswordHash) == false)
                    ThrowNoPermissions(userId);                
                return new AuthorizedSession(store, session, user);
            }
            catch
            {
                session?.Dispose();
                throw;
            }
        }

        private static readonly string Root = "RootUser"; 
        public void CreateUser(string user, string password)
        {
            try
            {
                using (var session = _store.OpenSession(new SessionOptions
                {
                    TransactionMode = TransactionMode.ClusterWide
                }))
                {
                    var value = session.Advanced.ClusterTransaction.GetCompareExchangeValue<string>(UserPrefix + Root);
                    if (value.Value != _authorizedUser.Id)
                    {
                        throw new UnauthorizedAccessException($"AuthorizedUser {_authorizedUser.Id} is not allowed to create new users");
                    }

                    session.Advanced.ClusterTransaction.CreateCompareExchangeValue(UserPrefix + user, 0);
                    session.Store(new AuthorizedUser
                    {
                        Id = user,
                        PasswordHash = ComputeHash(user, password),
                        Groups = new HashSet<string>()
                    });
                    session.SaveChanges();
                }
            }
            catch (ConcurrencyException)
            {
                using (OpenSession(_store, user, password))
                {
                    //Concurrent creation of the same authorizedUser
                }
            }
        }

        //TODO: add method to add authorizedUser to group
        public void AddUserToGroup(string userId, string groupName)
        {
            using (var session = _store.OpenSession(new SessionOptions
            {
                TransactionMode = TransactionMode.ClusterWide
            }))
            {
                var groupValue = session.Advanced.ClusterTransaction.GetCompareExchangeValue<Group.GroupVersion>(GroupPrefix + groupName);
                if(groupValue == null)
                {
                    Thread.Sleep(15000);
                    groupValue = session.Advanced.ClusterTransaction.GetCompareExchangeValue<Group.GroupVersion>(GroupPrefix + groupName);
                    if (groupValue == null)
                    {
                        throw new InvalidOperationException($"Can't add authorizedUser {userId} to a none existing group {groupName}, did you forget to generate it?");
                    }
                    //problem in cluster tx                    
                }
                if (groupValue.Value.Creator != _authorizedUser.Id)
                {
                    throw new UnauthorizedAccessException($"Only the group creator {_authorizedUser.Id} may add new members to {groupName} group");
                }
                var userVersion = session.Advanced.ClusterTransaction.GetCompareExchangeValue<int>(UserPrefix + userId);
                if(userVersion == null)
                {
                    Thread.Sleep(15000);
                    userVersion = session.Advanced.ClusterTransaction.GetCompareExchangeValue<int>(UserPrefix + userId);
                    if (userVersion == null)
                    {                        
                        throw new InvalidOperationException($"AuthorizedUser {userId} does not exist");
                    }                    
                    //problem in cluster tx
                }
                session.Advanced.ClusterTransaction.UpdateCompareExchangeValue(new CompareExchangeValue<int>(userVersion.Key,userVersion.Index,userVersion.Value+1));
                var user = session.Load<AuthorizedUser>(userId);
                var group = session.Load<Group>(groupName);                
                session.Advanced.ClusterTransaction.UpdateCompareExchangeValue(
                    new CompareExchangeValue<Group.GroupVersion>(groupValue.Key, groupValue.Index, 
                        new Group.GroupVersion
                        {
                            Creator = groupValue.Value.Creator,
                            Version = groupValue.Value.Version + 1
                        }));
                group.Members.Add(userId);
                session.Store(group);
                user.Groups.Add(groupName);
                session.Store(user);
                try
                {
                    session.SaveChanges();
                }catch(ConcurrencyException)
                {
                    user = session.Load<AuthorizedUser>(userId);
                    group = session.Load<Group>(groupName);
                    if((user?.Groups.Contains(groupName)??false) && (group?.Members.Contains(user.Id)??false))
                    {
                        //AuthorizedUser already added 
                        return;
                    }

                    throw;
                }
            }
        }

        //TODO: add methods for giving and revoking permissions
        public void GiveUserPermissionToAccessDocument(string userId, string documentId, string collection)
        {
            if (CheckIfUserHasPermissionTo(documentId, collection) == false)
            {
                throw new UnauthorizedAccessException(
                    $"AuthorizedUser {_authorizedUser.Id} doesn't have permissions to access document {documentId} in collection {collection}");
            }

            using (var session = _store.OpenSession(new SessionOptions
            {
                TransactionMode = TransactionMode.ClusterWide
            }))
            {
                var userVersion = session.Advanced.ClusterTransaction.GetCompareExchangeValue<int>(UserPrefix + userId);
                if(userVersion == null)
                    throw new InvalidOperationException($"No authorizedUser named {userId} in the system");
                var user = session.Load<AuthorizedUser>(userId);
                session.Advanced.ClusterTransaction.UpdateCompareExchangeValue(new CompareExchangeValue<int>(userVersion.Key, userVersion.Index, userVersion.Value+1));
                if (user.Permissions == null)
                {
                    user.Permissions = new Permission{Ids = new HashSet<string>{documentId}};
                }
                else if (user.Permissions.Ids == null)
                {
                    user.Permissions.Ids = new HashSet<string> { documentId };
                }
                else
                {
                    user.Permissions.Ids.Add(documentId);
                }
                session.Store(user, userId);
                session.SaveChanges();
            }
        }

        private bool CheckIfUserHasPermissionTo(string documentId, string collection)
        {
            var res = _session.Advanced.GraphQuery<dynamic>("match (dp) or (u)-[Groups]->(ag) or (u)-[Groups]->(Groups as g)-recursive (0, shortest) {[Parent]->(Groups)}-[Parent]->(ag)")
                .With("u", _session.Query<AuthorizedUser>().Where(u => u.Id == _authorizedUser.Id))
                .With("dp",
                    _session.Query<AuthorizedUser>().Where(u =>
                        u.Id == _authorizedUser.Id && (u.Permissions.Ids.Contains(documentId) ||
                                             u.Permissions.Collections.Contains(collection))))
                .With("ag",
                    _session.Query<Group>().Where(g =>
                        g.Permission.Ids.Contains(documentId) || g.Permission.Collections.Contains(collection)));
            if (res.FirstOrDefault() == null)
            {
                //check if root authorizedUser
                using (var session = _store.OpenSession(new SessionOptions
                {
                    TransactionMode = TransactionMode.ClusterWide
                }))
                {
                    var value = session.Advanced.ClusterTransaction.GetCompareExchangeValue<string>(UserPrefix + Root);
                    return value.Value == _authorizedUser.Id;
                }
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ThrowNoPermissions(string userId)
        {
            throw new UnauthorizedAccessException($"AuthorizedUser {userId} is not a registered authorizedUser");
        }

        private static bool CheckValidAuthorizedUser(string user, string password, string userHash)
        {
            return Argon2.Verify(userHash, user + password);
        }

        private static string ComputeHash(string user, string password)
        {
            var hash = Argon2.Hash(user + password);
            return hash;
        }

        private AuthorizedUser _authorizedUser;
        private IDocumentStore _store;

        public void Dispose()
        {
            _session?.Dispose();
        }

        public static void CreateRootUser(IDocumentStore store, string root, string password)
        {
            using (var session = store.OpenSession(new SessionOptions
            {
                TransactionMode = TransactionMode.ClusterWide
            }))
            {
                try
                {
                    session.Advanced.ClusterTransaction.CreateCompareExchangeValue(UserPrefix + Root, root);
                    session.Advanced.ClusterTransaction.CreateCompareExchangeValue(UserPrefix + root, 0);
                    session.Store(new AuthorizedUser
                    {
                        Id = root,
                        PasswordHash = ComputeHash(root, password)
                    });
                    session.SaveChanges();
                }
                catch (ConcurrencyException)
                {
                    var rootName = session.Advanced.ClusterTransaction.GetCompareExchangeValue<string>(UserPrefix + Root);
                    if (rootName.Value != root)
                    {
                        throw new UnauthorizedAccessException($"Can't generate root authorizedUser {root} since there is another root authorizedUser named {rootName.Value}");
                    }
                }
            }
        }

        public void CreateGroup(string groupName,string parent, string description, Permission permission)
        {
            using (var session = _store.OpenSession(new SessionOptions
            {
                TransactionMode = TransactionMode.ClusterWide
            }))
            {
                try
                {
                    session.Advanced.ClusterTransaction.CreateCompareExchangeValue(GroupPrefix + groupName, 
                        new Group.GroupVersion
                        {
                            Creator = _authorizedUser.Id,
                            Version = 0
                        });
                    session.Store(new Group
                    {
                        Parent = parent,
                        Description = description,
                        Members = new HashSet<string>(),
                        Permission = permission
                    }, groupName);
                    session.SaveChanges();
                }
                catch (ConcurrencyException)
                {
                    //Group was already created 
                    var creator = session.Advanced.ClusterTransaction.GetCompareExchangeValue<Group.GroupVersion>(GroupPrefix + groupName);
                    if (creator.Value.Creator != _authorizedUser.Id)
                    {
                        throw new UnauthorizedAccessException($"Can't generate group {groupName} since such a group was already created by {creator.Value.Creator}");
                    }
                }
            }
        }
    }
}
