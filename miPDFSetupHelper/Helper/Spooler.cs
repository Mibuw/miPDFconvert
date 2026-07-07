using System.Diagnostics;

namespace miMonitor.SetupHelper.Helper
{
    internal class Spooler
    {
        public static void stop()
        {
            RunNetCommand("stop spooler");
        }

        public static void start()
        {
            RunNetCommand("start spooler");
        }

        private static void RunNetCommand(string arguments)
        {
            ProcessStartInfo processStartInfo = new ProcessStartInfo
            {
                FileName = "net.exe",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using (var process = Process.Start(processStartInfo))
                process.WaitForExit(1000 * 60);
        }
    }
}
