using System;
using TestingEnvironment.Client;
using Command = TestingEnvironment.Common.Command;

namespace CustomOrchestratorCommands
{
    public class CustomOrchestratorCommands : BaseTest
    {
        public Command Cmd;
        public string CmdData;
        public CustomOrchestratorCommands(string orchestratorUrl, string testName, int round, string testid) : base(orchestratorUrl, testName, "Adi", round, testid)
        {
        }

        public override void RunActualTest()
        {
            switch (Cmd)
            {
                // data: roundNum
                case Command.RemoveLastRunningTestInfo:
                    try
                    {
                        var rc = ExecuteCommand(Cmd.ToString(), CmdData);
                        ReportSuccess($"Results={rc}");
                    }
                    catch (Exception e)
                    {
                        ReportFailure("Couldn't remove latest", e);
                    }
                    break;
                default:
                    ReportFailure($"Invalid command passed to CustomOrchestratorCommands: {Cmd}", null);
                    break;
            }
        }
    }
}
