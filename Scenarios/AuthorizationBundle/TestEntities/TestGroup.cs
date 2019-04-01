using System.Collections.Generic;

namespace AuthorizationBundle
{ 
    public class TestGroup : Group
    {
        public TestGroup()
        {
            Children = new HashSet<TestGroup>();
        }

        public HashSet<TestGroup> Children { get; set; }


    }
}
