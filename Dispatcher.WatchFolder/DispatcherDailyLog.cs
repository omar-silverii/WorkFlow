using System;
using System.IO;
using System.Text;

namespace Intranet.WorkflowStudio.Dispatcher.WatchFolder
{
    internal static class DispatcherDailyLog
    {
        private static readonly object Sync = new object();
        private static string _folder;

        public static void Initialize(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
                throw new ArgumentException("La carpeta de log no puede estar vacía.", nameof(folder));

            _folder = Path.GetFullPath(folder);
            Directory.CreateDirectory(_folder);
            Info("Log diario activo: " + CurrentFilePath());
        }

        public static void Info(string message)
        {
            Write("INFO", message, false);
        }

        public static void Error(string message)
        {
            Write("ERROR", message, true);
        }

        private static void Write(string level, string message, bool error)
        {
            var text = message ?? "";

            lock (Sync)
            {
                if (error) Console.Error.WriteLine(text);
                else Console.WriteLine(text);

                if (string.IsNullOrWhiteSpace(_folder))
                    return;

                try
                {
                    Directory.CreateDirectory(_folder);
                    var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

                    using (var sw = new StreamWriter(CurrentFilePath(), true, new UTF8Encoding(false)))
                    {
                        foreach (var line in lines)
                        {
                            sw.Write(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                            sw.Write(" [");
                            sw.Write(level);
                            sw.Write("] ");
                            sw.WriteLine(line ?? "");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("[Log/error] No se pudo escribir el log diario: " + ex.Message);
                }
            }
        }

        private static string CurrentFilePath()
        {
            return Path.Combine(_folder, "Dispatcher_" + DateTime.Now.ToString("yyyyMMdd") + ".log");
        }
    }
}
