using System;
using System.Configuration;
using System.IO;

namespace Intranet.WorkflowStudio.Dispatcher.WatchFolder
{
    public sealed class WatchFolderOptions
    {
        public string InputFolder { get; private set; }
        public string ProcessingFolder { get; private set; }
        public string ProcessedFolder { get; private set; }
        public string ErrorFolder { get; private set; }
        public string LogFolder { get; private set; }

        public bool RouterEnabled { get; private set; }
        public string ChannelCode { get; private set; }

        // Compatibilidad con fix75: se usa únicamente cuando RouterEnabled=false.
        public string WorkflowName { get; private set; }
        public string WorkflowInputField { get; private set; }

        public string Pattern { get; private set; }
        public int PollSeconds { get; private set; }

        public int StableChecks { get; private set; }
        public int StableDelayMs { get; private set; }

        public bool MoveAfter { get; private set; }

        public static WatchFolderOptions LoadFromConfig()
        {
            bool routerEnabled = GetBool("Ingress.Router.Enabled", false);

            var o = new WatchFolderOptions
            {
                InputFolder = GetReq("WatchFolder.Input"),
                ProcessingFolder = GetReq("WatchFolder.Processing"),
                ProcessedFolder = GetReq("WatchFolder.Processed"),
                ErrorFolder = GetReq("WatchFolder.Error"),
                LogFolder = GetOpt("WatchFolder.LogFolder", @"C:\WorkflowStudio\Logs\Dispatcher"),

                RouterEnabled = routerEnabled,
                ChannelCode = GetOpt("Ingress.ChannelCode", "GENERAL").Trim().ToUpperInvariant(),

                WorkflowName = GetOptionalFirst("Workflow.Nombre", "Workflow.Key"),
                WorkflowInputField = GetOpt("Workflow.InputField", "filePath"),

                Pattern = GetOpt("WatchFolder.Pattern", "*.*"),
                PollSeconds = GetInt("WatchFolder.PollSeconds", 2),

                StableChecks = GetInt("WatchFolder.StableChecks", 2),
                StableDelayMs = GetInt("WatchFolder.StableDelayMs", 500),

                MoveAfter = GetBool("WatchFolder.MoveAfter", true)
            };

            if (!o.RouterEnabled && string.IsNullOrWhiteSpace(o.WorkflowName))
            {
                throw new ConfigurationErrorsException(
                    "Con Ingress.Router.Enabled=false debe configurarse Workflow.Nombre " +
                    "(compatibilidad anterior: Workflow.Key)."
                );
            }

            if (string.IsNullOrWhiteSpace(o.ChannelCode))
                o.ChannelCode = "GENERAL";

            o.InputFolder = Path.GetFullPath(o.InputFolder);
            o.ProcessingFolder = Path.GetFullPath(o.ProcessingFolder);
            o.ProcessedFolder = Path.GetFullPath(o.ProcessedFolder);
            o.ErrorFolder = Path.GetFullPath(o.ErrorFolder);
            o.LogFolder = Path.GetFullPath(o.LogFolder);

            ValidateDistinctFolders(o);
            ValidateAtomicClaimVolume(o.InputFolder, o.ProcessingFolder);

            Directory.CreateDirectory(o.InputFolder);
            Directory.CreateDirectory(o.ProcessingFolder);
            Directory.CreateDirectory(o.ProcessedFolder);
            Directory.CreateDirectory(o.ErrorFolder);
            Directory.CreateDirectory(o.LogFolder);

            if (o.PollSeconds < 1) o.PollSeconds = 1;
            if (o.StableChecks < 2) o.StableChecks = 2;
            if (o.StableDelayMs < 0) o.StableDelayMs = 0;

            return o;
        }

        private static void ValidateDistinctFolders(WatchFolderOptions o)
        {
            var folders = new[]
            {
                o.InputFolder,
                o.ProcessingFolder,
                o.ProcessedFolder,
                o.ErrorFolder
            };

            for (int i = 0; i < folders.Length; i++)
            {
                for (int j = i + 1; j < folders.Length; j++)
                {
                    if (string.Equals(
                        TrimEndingSeparator(folders[i]),
                        TrimEndingSeparator(folders[j]),
                        StringComparison.OrdinalIgnoreCase))
                    {
                        throw new ConfigurationErrorsException(
                            "Las carpetas Input, Processing, Processed y Error deben ser diferentes.");
                    }
                }
            }
        }

        private static void ValidateAtomicClaimVolume(string inputFolder, string processingFolder)
        {
            var inputRoot = Path.GetPathRoot(inputFolder) ?? "";
            var processingRoot = Path.GetPathRoot(processingFolder) ?? "";

            if (!string.Equals(inputRoot, processingRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new ConfigurationErrorsException(
                    "WatchFolder.Input y WatchFolder.Processing deben estar en el mismo volumen o recurso compartido " +
                    "para que el claim del archivo sea atómico.");
            }
        }

        private static string TrimEndingSeparator(string path)
        {
            return (path ?? "").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static string GetReq(string key)
        {
            var v = ConfigurationManager.AppSettings[key];
            if (string.IsNullOrWhiteSpace(v))
                throw new ConfigurationErrorsException("Falta appSetting: " + key);
            return v.Trim();
        }

        private static string GetOptionalFirst(string primaryKey, string legacyKey)
        {
            var primary = ConfigurationManager.AppSettings[primaryKey];
            if (!string.IsNullOrWhiteSpace(primary))
                return primary.Trim();

            var legacy = ConfigurationManager.AppSettings[legacyKey];
            if (!string.IsNullOrWhiteSpace(legacy))
                return legacy.Trim();

            return null;
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
