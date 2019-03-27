using System.Collections.Generic;

namespace AuthorizationBundle
{
    public class Permission
    {
        public string Description { get; set; }
        public HashSet<string> Collections { get; set; }
        public HashSet<string> Ids { get; set; }
    }
}
