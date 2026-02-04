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

                Console.WriteLine("=== Workflow Studio - WatchFolder Dispatcher ===");
                Console.WriteLine("Input:      " + opt.InputFolder);
                Console.WriteLine("Processed:  " + opt.ProcessedFolder);
                Console.WriteLine("Error:      " + opt.ErrorFolder);
                Console.WriteLine("WorkflowKey:" + opt.WorkflowKey);
                Console.WriteLine("Pattern:    " + opt.Pattern);
                Console.WriteLine("PollSeconds:" + opt.PollSeconds);
                Console.WriteLine("");

                var dispatcher = new WatchFolderDispatcher(opt);
                dispatcher.RunLoop();

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FATAL: " + ex.GetType().Name + " - " + ex.Message);
                Console.Error.WriteLine(ex.ToString());
                return 2;
            }
        }
    }
}
