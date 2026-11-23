// miPDFconvert - virtual PDF printer
// Copyright (C) 2026 Wolfgang Mitterbucher (mitterbucher.com)
//
// Licensed under the Apache License, Version 2.0 (see LICENSE).

using System.IO;
using System.Text;
using System.Threading;

namespace miPDFconvert
{
    /// <summary>
    /// Writes the log to %ProgramData%\miPDFconvert\miPDFconvert.log. Independent of the application
    /// configuration, so it also works when miPDFconvert.dll.config is broken. Several print jobs may
    /// run at the same time, so the file is opened for each line and shared with other processes;
    /// above 1 MB it is moved to miPDFconvert.log.old. Never throws.
    /// </summary>
    internal static class Log
    {
        private const long MaxSize = 1024 * 1024;

        private static readonly string LogFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "miPDFconvert", "miPDFconvert.log");
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
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
                        RotateIfTooLarge();
                        using var stream = new FileStream(LogFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                        stream.Write(bytes, 0, bytes.Length);
                        return;
                    }
                    catch (IOException)
                    {
                        Thread.Sleep(20);   // another process is just rotating the file
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"miPDFconvert: cannot write the log: {ex.Message}");
                        return;             // logging must never break printing
                    }
                }
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
