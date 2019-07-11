using System;

namespace BackupTaskCleaner
{
    public class Program
    {
        public static void Main(string[] args)
        {
            RunTaskCleanerScenario(args[0]);
        }

        public static void RunTaskCleanerScenario(string orchestratorUrl)
        {
            using (var client = new BackupTaskCleaner(orchestratorUrl, "BackupTaskCleaner", -1, Guid.NewGuid().ToString()))
            {
                client.Initialize();
                client.RunTest();
            }
        }
    }
}
