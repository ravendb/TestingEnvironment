namespace TaskCleaner
{
    public class Program
    {
        public static void Main(string[] args)
        {
            RunTaskCleanerScenario(args[0]);
        }

        public static void RunTaskCleanerScenario(string orchestratorUrl)
        {
            using (var client = new TaskCleaner(orchestratorUrl, "TaskCleaner"))
            {
                client.Initialize();
                client.RunTest();
            }
        }
    }
}
