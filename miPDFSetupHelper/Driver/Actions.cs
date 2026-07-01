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
            miPDFConvertInstaller installer = new miPDFConvertInstaller();
            try
            {
                if (installer.IsmiPDFConvertPrinterInstalled())
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
            miPDFConvertInstaller installer = new miPDFConvertInstaller();
            try
            {
                if (installer.AddCustommiPDFConvertPrinter(name))
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
            miPDFConvertInstaller installer = new miPDFConvertInstaller();
            try
            {
                if (installer.DeleteCustommiPDFConvertPrinter(name))
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
            return !printerHelper.GetmiPDFConvertPrinters().Any();
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

        public static bool InstallmiPDFConvertPrinter()
        {
            bool printerInstalled;
            string miPDFConvertPath;
            Utilities.OsHelper osHelper = new Utilities.OsHelper();
            miPDFConvertInstaller installer = new miPDFConvertInstaller();
            try
            {
                if (Environment.Is64BitOperatingSystem && !osHelper.IsArm64())
                {
                    miPDFConvertPath = Path.GetDirectoryName(Application.ExecutablePath) + @"\miMonitor\x64\";
                }
                else if (!Environment.Is64BitOperatingSystem && !osHelper.IsArm64())
                {
                    miPDFConvertPath = Path.GetDirectoryName(Application.ExecutablePath) + @"\miMonitor\x86\";
                }
                else
                {
                    miPDFConvertPath = Path.GetDirectoryName(Application.ExecutablePath) + @"\miMonitor\x64\";
                }

                if (installer.InstallmiPDFConvertPrinter(miPDFConvertPath, "miPDFConvertBase.exe"))
                    printerInstalled = true;
                else
                    printerInstalled = false;
            }
            finally
            { }
            return printerInstalled;
        }

        public static bool UninstallmiPDFConvertPrinter()
        {
            bool printerUninstalled;
            miPDFConvertInstaller installer = new miPDFConvertInstaller();
            try
            {
                if (installer.UninstallmiPDFConvertPrinter())
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