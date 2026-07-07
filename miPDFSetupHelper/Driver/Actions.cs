using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Threading;
using System.Windows.Forms;
using miMonitor.SetupHelper.Helper;
using miMonitor.SetupHelper.Utilities;

namespace miMonitor.SetupHelper.Driver
{
    internal class Actions
    {
        public static bool AddPrinter(string name)
        {
            var installer = new miPDFconvertInstaller();
            if (!installer.AddCustommiPDFconvertPrinter(name))
                return false;

            Spooler.stop();
            Spooler.start();
            return true;
        }

        public static bool RemovePrinter(string name)
        {
            return new miPDFconvertInstaller().DeleteCustommiPDFconvertPrinter(name);
        }

        public static bool IsRepairRequired()
        {
            var printerHelper = new PrinterHelper();
            return !printerHelper.GetmiPDFconvertPrinters().Any();
        }

        public static void WaitForPrintSpooler()
        {
            using (var printSpooler = new ServiceController("Spooler"))
            {
                Stopwatch stopwatch = Stopwatch.StartNew();

                while (printSpooler.Status != ServiceControllerStatus.Running && stopwatch.ElapsedMilliseconds < 120000)
                {
                    printSpooler.Refresh();
                    Thread.Sleep(3000);
                }
            }
        }

        public static bool InstallmiPDFconvertPrinter()
        {
            // ARM64 wird (wie x64) ueber die x64-Treiberdateien bedient
            var osHelper = new OsHelper();
            string architecture = !Environment.Is64BitOperatingSystem && !osHelper.IsArm64() ? "x86" : "x64";
            string driverPath = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "miMonitor", architecture);

            return new miPDFconvertInstaller().InstallmiPDFconvertPrinter(driverPath);
        }

        public static void UninstallmiPDFconvertPrinter()
        {
            new miPDFconvertInstaller().UninstallmiPDFconvertPrinter();
        }
    }
}
