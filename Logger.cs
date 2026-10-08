using System;
using System.IO;

namespace TranslationEplAddIn.Tool
{
    public static class Logger
    {
        private static readonly string LogFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EPLAN_TranslationAddIn",
            "logs"
        );

        private static readonly object _lock = new object();

        static Logger()
        {
            try
            {
                if (!Directory.Exists(LogFolder))
                {
                    Directory.CreateDirectory(LogFolder);
                }
            }
            catch
            {
                // Fallback to console if we can't create directory
            }
        }

        private static void WriteLog(string level, string message, Exception ex = null)
        {
            try
            {
                string date = DateTime.Now.ToString("yyyy-MM-dd");
                string logFile = Path.Combine(LogFolder, $"translation_{date}.log");
                string time = DateTime.Now.ToString("HH:mm:ss.fff");
                
                string logLine = $"[{time}] [{level}] {message}";
                if (ex != null)
                {
                    logLine += Environment.NewLine + ex.ToString();
                }

                lock (_lock)
                {
                    File.AppendAllText(logFile, logLine + Environment.NewLine);
                }
            }
            catch
            {
                // In absolute worst case, fallback to console so it's not totally lost
                Console.WriteLine($"[{level}] {message}");
                if (ex != null) Console.WriteLine(ex.ToString());
            }
        }

        public static void Info(string message)
        {
            WriteLog("INFO", message);
        }

        public static void Warn(string message)
        {
            WriteLog("WARN", message);
        }

        public static void Error(Exception ex, string message = "An error occurred")
        {
            WriteLog("ERROR", message, ex);
        }
    }
}
