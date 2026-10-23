using System;
using System.IO;
using System.Linq;

namespace IfcViewer.Infrastructure
{
    /// <summary>
    /// Simple log file per day in %LocalAppData%\IfcViewer\logs (opened files, timings, errors with stack trace).
    /// Logging never throws: if the file can't be written, the entry is dropped.
    /// </summary>
    public static class Log
    {
        private const int KeepDays = 14;
        private static readonly object Gate = new object();

        public static string Folder { get; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IfcViewer", "logs");

        public static void Info(string message) => Write("INFO ", message);

        public static void Error(string message, Exception exception) => Write("ERROR", $"{message}{Environment.NewLine}{exception}");

        // Removes log files older than two weeks; called once at startup.
        public static void DeleteOldFiles()
        {
            try
            {
                foreach (var file in Directory.GetFiles(Folder, "IfcViewer-*.log")
                                              .Where(f => File.GetLastWriteTime(f) < DateTime.Now.AddDays(-KeepDays)))
                {
                    File.Delete(file);
                }
            }
            catch (Exception)
            {
                // No folder yet, or a file in use: nothing to clean
            }
        }

        private static void Write(string level, string text)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Folder);
                    var file = Path.Combine(Folder, $"IfcViewer-{DateTime.Now:yyyy-MM-dd}.log");
                    File.AppendAllText(file, $"{DateTime.Now:HH:mm:ss.fff} {level} {text}{Environment.NewLine}");
                }
            }
            catch (Exception)
            {
                // Logging must never break the app
            }
        }
    }
}
