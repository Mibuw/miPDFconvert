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
        public static bool CheckIfPrinterNotInstalled()
        {
            bool resultCode;
            miPDFconvertInstaller installer = new miPDFconvertInstaller();
            try
            {
                if (installer.IsmiPDFconvertPrinterInstalled())
                    resultCode = true;
                else
                    resultCode = false;
            }
            finally
            { }
            return resultCode;
        }

        public static bool AddPrinter(string name)
        {
            bool resultCode;
            miPDFconvertInstaller installer = new miPDFconvertInstaller();
            try
            {
                if (installer.AddCustommiPDFconvertPrinter(name))
                {
                    resultCode = true;
                    Spooler.stop();
                    Spooler.start();
                }
                else
                    resultCode = false;
            }
            finally
            { }
            return resultCode;
        }

        public static bool RemovePrinter(string name)
        {
            bool resultCode;
            miPDFconvertInstaller installer = new miPDFconvertInstaller();
            try
            {
                if (installer.DeleteCustommiPDFconvertPrinter(name))
                    resultCode = true;
                else
                    resultCode = false;
            }
            finally
            { }
            return resultCode;
        }

        public static bool IsRepairRequired()
        {
            var printerHelper = new PrinterHelper();
            return !printerHelper.GetmiPDFconvertPrinters().Any();
        }

        public static void WaitForPrintSpooler()
        {
            ServiceController printSpooler = new ServiceController("Spooler");

            Stopwatch stopwatch = Stopwatch.StartNew();

            while (printSpooler.Status != ServiceControllerStatus.Running && stopwatch.ElapsedMilliseconds < 120000)
            {
                printSpooler.Refresh();
                Thread.Sleep(3000);
            }

            stopwatch.Stop();

            if (printSpooler.Status != ServiceControllerStatus.Running)
            {
            }
        }

        public static bool InstallmiPDFconvertPrinter()
        {
            bool printerInstalled;
            string miPDFconvertPath;
            Utilities.OsHelper osHelper = new Utilities.OsHelper();
            miPDFconvertInstaller installer = new miPDFconvertInstaller();
            try
            {
                if (Environment.Is64BitOperatingSystem && !osHelper.IsArm64())
                {
                    miPDFconvertPath = Path.GetDirectoryName(Application.ExecutablePath) + @"\miMonitor\x64\";
                }
                else if (!Environment.Is64BitOperatingSystem && !osHelper.IsArm64())
                {
                    miPDFconvertPath = Path.GetDirectoryName(Application.ExecutablePath) + @"\miMonitor\x86\";
                }
                else
                {
                    miPDFconvertPath = Path.GetDirectoryName(Application.ExecutablePath) + @"\miMonitor\x64\";
                }

                if (installer.InstallmiPDFconvertPrinter(miPDFconvertPath, "miPDFconvertBase.exe"))
                    printerInstalled = true;
                else
                    printerInstalled = false;
            }
            finally
            { }
            return printerInstalled;
        }

        public static bool UninstallmiPDFconvertPrinter()
        {
            bool printerUninstalled;
            miPDFconvertInstaller installer = new miPDFconvertInstaller();
            try
            {
                if (installer.UninstallmiPDFconvertPrinter())
                    printerUninstalled = true;
                else
                    printerUninstalled = true;
            }
            finally
            { }
            return printerUninstalled;
        }
    }
}