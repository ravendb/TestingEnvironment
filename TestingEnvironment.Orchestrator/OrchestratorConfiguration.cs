namespace TestingEnvironment.Orchestrator
{
    public class ServerInfo
    {
        public string Port { get; set; }
        public string Path { get; set; }
        public string Url { get; set; }
    }

    public class ClusterInfo
    {
        public string Name { get; set; }
        public bool HasAuthentication { get; set; }
        public string[] Urls { get; set; }

        protected bool Equals(ClusterInfo other)
        {
            return string.Equals(Name, other.Name) && HasAuthentication == other.HasAuthentication && Equals(Urls, other.Urls);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((ClusterInfo) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Name != null ? Name.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ HasAuthentication.GetHashCode();
                hashCode = (hashCode * 397) ^ (Urls != null ? Urls.GetHashCode() : 0);
                return hashCode;
            }
        }

        public static bool operator ==(ClusterInfo left, ClusterInfo right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(ClusterInfo left, ClusterInfo right)
        {
            return !Equals(left, right);
        }
    }

    public class OrchestratorConfiguration
    {
        public string OrchestratorUrl { get; set; }
        public string EmbeddedServerUrl { get; set; }
        public string[] Databases { get; set; }
        public ServerInfo[] LocalRavenServers { get; set; }
        public ClusterInfo[] Clusters { get; set; }
    }
}
