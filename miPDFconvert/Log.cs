// miPDFconvert - virtual PDF printer
// Copyright (C) 2026 Wolfgang Mitterbucher (mitterbucher.com)
//
// Licensed under the Apache License, Version 2.0 (see LICENSE).

using System.Configuration;
using System.IO;
using System.Text;
using System.Threading;

namespace miPDFconvert
{
    /// <summary>
    /// Writes the log to the file given by the LOG_FILE app setting, by default
    /// %ProgramData%\miPDFconvert\miPDFconvert.log. When miPDFconvert.dll.config is broken, the log
    /// still goes to the default file. Several print jobs may run at the same time, so the file is
    /// opened for each line and shared with other processes; above 1 MB it is moved to
    /// &lt;log file&gt;.old. Never throws.
    /// </summary>
    internal static class Log
    {
        private const string LOG_FILE_KEY = "LOG_FILE";
        private const string FILE_NAME = "miPDFconvert.log";
        private const long MaxSize = 1024 * 1024;

        private static readonly string DefaultFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "miPDFconvert", FILE_NAME);
        private static string LogFile = ResolveLogFile();
        private static readonly int ProcessId = Environment.ProcessId;
        private static readonly object Sync = new object();

        public static void Debug(string message) => Write("DEBUG", message, null);

        public static void Info(string message) => Write("INFO", message, null);

        public static void Warn(string message) => Write("WARN", message, null);

        public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

        private static void Write(string level, string message, Exception? exception)
        {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss,fff} [{ProcessId}] {level,-5} {message}{Environment.NewLine}";
            if (exception != null)
                line += exception + Environment.NewLine;
            byte[] bytes = Encoding.UTF8.GetBytes(line);

            lock (Sync)
            {
                if (TryWrite(bytes) || LogFile == DefaultFile)
                    return;
                // The configured log file cannot be written (no permission, invalid path, ...):
                // log to the default file from now on.
                string unusable = LogFile;
                LogFile = DefaultFile;
                TryWrite(Encoding.UTF8.GetBytes($"{DateTime.Now:yyyy-MM-dd HH:mm:ss,fff} [{ProcessId}] WARN  Cannot write the log file \"{unusable}\" ({LOG_FILE_KEY}); using \"{DefaultFile}\".{Environment.NewLine}"));
                TryWrite(bytes);
            }
        }

        private static bool TryWrite(byte[] bytes)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
                    RotateIfTooLarge();
                    using var stream = new FileStream(LogFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                    stream.Write(bytes, 0, bytes.Length);
                    return true;
                }
                catch (IOException) when (attempt < 4)
                {
                    Thread.Sleep(20);   // another process may just be rotating the file
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"miPDFconvert: cannot write the log: {ex.Message}");
                    return false;       // logging must never break printing
                }
            }
            return false;
        }

        /// <summary>
        /// The configured log file: environment variables are expanded, a folder gets the file
        /// miPDFconvert.log, a relative path is taken from the application folder. Empty, missing or
        /// unreadable configuration: the default file.
        /// </summary>
        private static string ResolveLogFile()
        {
            try
            {
                string? configured = ConfigurationManager.AppSettings[LOG_FILE_KEY]?.Trim();
                if (String.IsNullOrEmpty(configured))
                    return DefaultFile;
                string path = Environment.ExpandEnvironmentVariables(configured);
                if (Directory.Exists(path) || path.EndsWith('\\') || path.EndsWith('/'))
                    path = Path.Combine(path, FILE_NAME);
                return Path.GetFullPath(path, AppContext.BaseDirectory);
            }
            catch (Exception)
            {
                return DefaultFile;   // broken configuration or invalid path
            }
        }

        private static void RotateIfTooLarge()
        {
            var info = new FileInfo(LogFile);
            if (info.Exists && info.Length > MaxSize)
                File.Move(LogFile, LogFile + ".old", true);
        }
    }
}
