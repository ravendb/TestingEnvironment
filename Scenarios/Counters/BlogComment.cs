using System;

namespace Counters
{
    public class BlogComment
    {
        public string Id { get; set; }
        public string Author { get; set; }
        public string Tag { get; set; }
        public string Text { get; set; }
        public double Rating { get; set; }
        public DateTime PostedAt { get; set; }
        public DateTime LastModified { get; set; }
    }

    public enum CommentTag
    {
        Politics = 0,
        Economics = 1,
        Sports = 2,
        Entertainment = 3,
        Science = 4,
        Music = 5,
        Religion = 6,
        Food = 7,
        Tech = 8,
        Other = 9
    }
}
