namespace MarineResearch
{
    class Program
    {
        static void Main(string[] args)
        {
            using (var client = new MarineResearchTest(args[0], "PutCommentsTest"))
            {
                client.Initialize();
                client.RunTest();
            }
        }
    }
}
