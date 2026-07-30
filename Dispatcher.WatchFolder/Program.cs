using System;

namespace Intranet.WorkflowStudio.Dispatcher.WatchFolder
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                var opt = WatchFolderOptions.LoadFromConfig();
                DispatcherDailyLog.Initialize(opt.LogFolder);

                DispatcherDailyLog.Info("=== Workflow Studio - WatchFolder Dispatcher ===");
                DispatcherDailyLog.Info("Input:       " + opt.InputFolder);
                DispatcherDailyLog.Info("Processing:  " + opt.ProcessingFolder);
                DispatcherDailyLog.Info("Processed:   " + opt.ProcessedFolder);
                DispatcherDailyLog.Info("Error:       " + opt.ErrorFolder);
                DispatcherDailyLog.Info("Log:         " + opt.LogFolder);
                DispatcherDailyLog.Info("Mode:        " + (opt.RouterEnabled ? "ENRUTADOR" : "WORKFLOW FIJO"));
                DispatcherDailyLog.Info("Channel:     " + opt.ChannelCode);

                if (opt.RouterEnabled)
                    DispatcherDailyLog.Info("Workflow:    resuelto por WF_IngresoRuta / Bandeja de Ingreso");
                else
                    DispatcherDailyLog.Info("Workflow:    " + opt.WorkflowName);

                DispatcherDailyLog.Info("Input field: " + opt.WorkflowInputField);
                DispatcherDailyLog.Info("Pattern:     " + opt.Pattern);
                DispatcherDailyLog.Info("PollSeconds: " + opt.PollSeconds);
                DispatcherDailyLog.Info("MoveAfter:   " + opt.MoveAfter);
                DispatcherDailyLog.Info("");

                var dispatcher = new WatchFolderDispatcher(opt);
                dispatcher.RunLoop();

                return 0;
            }
            catch (Exception ex)
            {
                DispatcherDailyLog.Error("FATAL: " + ex.GetType().Name + " - " + ex.Message);
                DispatcherDailyLog.Error(ex.ToString());
                return 2;
            }
        }
    }
}
