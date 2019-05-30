using System;

namespace BackupAndRestore
{
    public class Program
    {
        public static void Main(string[] args)
        {
            RunCountersScenarios(args[0]);
        }

        public static void RunCountersScenarios(string orchestratorUrl)
        {
            using (var client = new BackupAndRestore(orchestratorUrl, "BackupAndRestore", -1))
            {
                client.Initialize();
                client.RunTest();
            }
        }
    }
}
