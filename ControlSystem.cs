using Crestron.SimplSharp;
using Crestron.SimplSharpPro;
using Crestron.SimplSharpPro.CrestronThread;
using Logging;
using Logging.Enums;
using System;

namespace MAV.ProductionRegressionController
{
    public sealed class ControlSystem : CrestronControlSystem
    {
        private RegressionController _controller;

        public ControlSystem() : base()
        {
            try
            {
                Thread.MaxNumberOfUserThreads = 20;
                Logger.SetLoggingLevel(LogLevel.Trace);
                Logger.InitializeFileLogging("MAV Production Regression Controller");
                Logger.Log("MAV Production Regression Controller constructor starting.", LogLevel.Information);
            }
            catch (Exception ex)
            {
                CrestronConsole.PrintLine("Regression Controller logger init error: {0}", ex.Message);
            }
        }

        public override void InitializeSystem()
        {
            try
            {
                _controller = new RegressionController();
                _controller.Start();
                CrestronConsole.AddNewConsoleCommand(
                    args => CrestronConsole.PrintLine(_controller.GetConsoleStatus()),
                    "mavregstatus",
                    "Show MAV Production Regression Controller status",
                    ConsoleAccessLevelEnum.AccessOperator);
                CrestronConsole.PrintLine("MAV Production Regression Controller ready: /cws/regression/ui");
            }
            catch (Exception ex)
            {
                Logger.Log("Regression Controller initialization failed: " + ex, LogLevel.Error);
                CrestronConsole.PrintLine("Regression Controller initialization failed: {0}", ex);
            }
        }
    }
}
