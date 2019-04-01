using System.Collections.Generic;

namespace AuthorizationBundle
{
    public class TestUser
    {
        public string Password { get; set; }
        public HashSet<string> Groups { get; set; } 
        public Permission Permissions { get; set; }
        public string Id { get; set; }
    }
}
