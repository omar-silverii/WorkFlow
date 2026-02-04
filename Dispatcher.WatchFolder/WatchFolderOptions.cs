using System;
using System.Configuration;
using System.IO;

namespace Intranet.WorkflowStudio.Dispatcher.WatchFolder
{
    public sealed class WatchFolderOptions
    {
        public string InputFolder { get; private set; }
        public string ProcessedFolder { get; private set; }
        public string ErrorFolder { get; private set; }

        public string WorkflowKey { get; private set; }
        public string WorkflowInputField { get; private set; }

        public string Pattern { get; private set; }
        public int PollSeconds { get; private set; }

        public int StableChecks { get; private set; }
        public int StableDelayMs { get; private set; }

        public bool MoveAfter { get; private set; }

        public static WatchFolderOptions LoadFromConfig()
        {
            var o = new WatchFolderOptions
            {
                InputFolder = GetReq("WatchFolder.Input"),
                ProcessedFolder = GetReq("WatchFolder.Processed"),
                ErrorFolder = GetReq("WatchFolder.Error"),

                WorkflowKey = GetReq("Workflow.Nombre"),
                WorkflowInputField = GetOpt("Workflow.InputField", "filePath"),

                Pattern = GetOpt("WatchFolder.Pattern", "*.txt"),
                PollSeconds = GetInt("WatchFolder.PollSeconds", 2),

                StableChecks = GetInt("WatchFolder.StableChecks", 2),
                StableDelayMs = GetInt("WatchFolder.StableDelayMs", 500),

                MoveAfter = GetBool("WatchFolder.MoveAfter", true)
            };

            // Normalizar y asegurar carpetas
            o.InputFolder = Path.GetFullPath(o.InputFolder);
            o.ProcessedFolder = Path.GetFullPath(o.ProcessedFolder);
            o.ErrorFolder = Path.GetFullPath(o.ErrorFolder);

            Directory.CreateDirectory(o.InputFolder);
            Directory.CreateDirectory(o.ProcessedFolder);
            Directory.CreateDirectory(o.ErrorFolder);

            if (o.PollSeconds < 1) o.PollSeconds = 1;
            if (o.StableChecks < 1) o.StableChecks = 1;
            if (o.StableDelayMs < 0) o.StableDelayMs = 0;

            return o;
        }

        private static string GetReq(string key)
        {
            var v = ConfigurationManager.AppSettings[key];
            if (string.IsNullOrWhiteSpace(v))
                throw new ConfigurationErrorsException("Falta appSetting: " + key);
            return v.Trim();
        }

        private static string GetOpt(string key, string def)
        {
            var v = ConfigurationManager.AppSettings[key];
            return string.IsNullOrWhiteSpace(v) ? def : v.Trim();
        }

        private static int GetInt(string key, int def)
        {
            var v = ConfigurationManager.AppSettings[key];
            int i;
            return int.TryParse(v, out i) ? i : def;
        }

        private static bool GetBool(string key, bool def)
        {
            var v = ConfigurationManager.AppSettings[key];
            bool b;
            return bool.TryParse(v, out b) ? b : def;
        }
    }
}
