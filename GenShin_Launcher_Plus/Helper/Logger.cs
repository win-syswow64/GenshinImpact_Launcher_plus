using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace GenShin_Launcher_Plus.Helper
{
    /// <summary>
    /// Log severity levels.
    /// </summary>
    public enum LogLevel
    {
        Debug,
        Info,
        Make,
        Warn,
        Error
    }

    /// <summary>
    /// Thread-safe global logger with daily rotation and 30-file retention.
    /// Default file threshold is Make, so Debug entries only appear in attached debuggers.
    /// </summary>
    public static class Logger
    {
        private const string LogDir = "Logs";
        private const int MaxLogFiles = 30;
        private const string DateFormat = "yyyy-MM-dd";

        private static readonly object _lock = new();
        private static LogLevel _fileThreshold = LogLevel.Make;

        /// <summary>
        /// Set the minimum level written to the log file. Default is Make.
        /// </summary>
        public static void SetFileThreshold(LogLevel level) => _fileThreshold = level;

        public static void Debug(string message, string? tag = null)
            => WriteLog(LogLevel.Debug, message, tag);

        public static void Info(string message, string? tag = null)
            => WriteLog(LogLevel.Info, message, tag);

        public static void Make(string message, string? tag = null)
            => WriteLog(LogLevel.Make, message, tag);

        public static void Warn(string message, string? tag = null)
            => WriteLog(LogLevel.Warn, message, tag);

        public static void Error(string message, string? tag = null)
            => WriteLog(LogLevel.Error, message, tag);

        private static void WriteLog(LogLevel level, string message, string? tag)
        {
            var timestamp = DateTime.Now;
            var levelStr = level.ToString().ToUpperInvariant().PadRight(5);
            var tagStr = string.IsNullOrEmpty(tag) ? "" : $"[{tag}] ";
            var line = $"[{timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{levelStr}] {tagStr}{message}";

            // Always write to Debug output when a debugger is attached
            System.Diagnostics.Debug.WriteLine(line);

            // Write to file if level >= threshold
            if (level < _fileThreshold) return;

            lock (_lock)
            {
                try
                {
                    Directory.CreateDirectory(LogDir);
                    var fileName = $"{timestamp.ToString(DateFormat)}.log";
                    var filePath = Path.Combine(LogDir, fileName);
                    File.AppendAllText(filePath, line + Environment.NewLine);
                    CleanOldLogs();
                }
                catch
                {
                    // Silently ignore logging failures
                }
            }
        }

        private static void CleanOldLogs()
        {
            try
            {
                var files = Directory.GetFiles(LogDir, "*.log")
                    .OrderByDescending(f => f)
                    .ToList();

                while (files.Count > MaxLogFiles)
                {
                    var oldest = files[files.Count - 1];
                    File.Delete(oldest);
                    files.RemoveAt(files.Count - 1);
                }
            }
            catch
            {
                // Silently ignore cleanup failures
            }
        }
    }
}
