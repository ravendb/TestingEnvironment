using System.Collections.Generic;

namespace AuthorizationBundle
{
    public class AuthorizedUser
    {
        public string PasswordHash { get; set; }
        public HashSet<string> Groups { get; set; } 
        public Permission Permissions { get; set; }
        public string Id { get; set; }
    }
}
