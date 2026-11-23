// miPDFconvert - virtual PDF printer
// Copyright (C) 2026 Wolfgang Mitterbucher (mitterbucher.com)
// Licensed under the Apache License, Version 2.0 (see LICENSE).
//
// Installs and removes the "miPDFconvert" printer:
//   PrinterSetup.exe install <folder of miPDFconvert.exe>
//   PrinterSetup.exe uninstall
//
// The printer uses the Microsoft PostScript class driver (pscript5) of the local Windows
// installation with the miPDFconvert PPD, and the miPDFport print monitor.

using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using Microsoft.Win32;

internal static class Program
{
    private const string PrinterName = "miPDFconvert";
    private const string DriverName = "miPDFconvert";
    private const string MonitorName = "miPDFconvert";
    private const string PortName = "miPDFconvert:";
    private const string MonitorDll = "miPDFport.dll";
    private const string PpdFile = "miPDFconvert.ppd";
    private const string MonitorKey = @"SYSTEM\CurrentControlSet\Control\Print\Monitors\" + MonitorName;

    // Printer, driver and monitor of miPDFconvert 1.x, removed on upgrade.
    private const string LegacyDriverName = "miPDFconvert Virtual Printer";
    private const string LegacyMonitorName = "MIMONITOR";
    private const string LegacyPortName = "MIMONITOR:";
    private static readonly string[] LegacyMonitorDlls = { "miMonitor.dll", "miMonitorUI.dll", "miMonitor.log" };

    private static readonly string[] PscriptFiles = { "PSCRIPT5.DLL", "PS5UI.DLL", "PSCRIPT.HLP", "PSCRIPT.NTF" };

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "install")
                Install(Path.GetFullPath(args[1]));
            else if (args.Length == 1 && args[0] == "uninstall")
                Uninstall();
            else
            {
                Console.Error.WriteLine("usage: PrinterSetup install <folder of miPDFconvert.exe> | uninstall");
                return 2;
            }
            return 0;
        }
        catch (Exception ex)
        {
            Log("ERROR: " + ex.Message);
            return 1;
        }
    }

    private static void Install(string appDirectory)
    {
        Log($"Installing the printer for {appDirectory}");
        Uninstall();

        bool arm64 = RuntimeInformation.OSArchitecture == Architecture.Arm64;
        string environment = arm64 ? "Windows ARM64" : "Windows x64";

        string monitorTarget = Path.Combine(Environment.SystemDirectory, MonitorDll);
        ReplaceFile(Path.Combine(appDirectory, "printer", arm64 ? "ARM64" : "x64", MonitorDll), monitorTarget);
        var monitor = new MONITOR_INFO_2 { pName = MonitorName, pDLLName = MonitorDll };
        Check(AddMonitor(null, 2, ref monitor), "AddMonitor");
        using (RegistryKey key = Registry.LocalMachine.CreateSubKey(MonitorKey))
            key.SetValue("Application", Path.Combine(appDirectory, "miPDFconvert.exe"));

        // The driver files go into the printer driver directory first: the PostScript class
        // driver of this Windows installation and the PPD.
        var driverDirectory = new StringBuilder(260);
        Check(GetPrinterDriverDirectory(null, environment, 1, driverDirectory, driverDirectory.Capacity * 2, out _), "GetPrinterDriverDirectory");
        string pscript = FindPscriptDirectory(arm64);
        foreach (string file in PscriptFiles)
            File.Copy(Path.Combine(pscript, file), Path.Combine(driverDirectory.ToString(), file), true);
        File.Copy(Path.Combine(appDirectory, "printer", PpdFile), Path.Combine(driverDirectory.ToString(), PpdFile), true);

        var driver = new DRIVER_INFO_3
        {
            cVersion = 3,
            pName = DriverName,
            pEnvironment = environment,
            pDriverPath = "PSCRIPT5.DLL",
            pDataFile = PpdFile,
            pConfigFile = "PS5UI.DLL",
            pHelpFile = "PSCRIPT.HLP",
            pDependentFiles = "PSCRIPT.NTF\0",
            pDefaultDataType = "RAW"
        };
        Check(AddPrinterDriver(null, 3, ref driver), "AddPrinterDriver");

        var printer = new PRINTER_INFO_2
        {
            pPrinterName = PrinterName,
            pPortName = PortName,
            pDriverName = DriverName,
            pPrintProcessor = "winprint",
            pDatatype = "RAW",
            Attributes = PRINTER_ATTRIBUTE_LOCAL
        };
        IntPtr handle = AddPrinter(null, 2, ref printer);
        Check(handle != IntPtr.Zero, "AddPrinter");
        ClosePrinter(handle);
        Log("Printer installed.");
    }

    // Removes the printer, driver and monitor (also those of miPDFconvert 1.x). Missing parts are fine.
    private static void Uninstall()
    {
        string environment = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "Windows ARM64" : "Windows x64";

        if (OpenPrinter(PrinterName, out IntPtr handle, new PRINTER_DEFAULTS { DesiredAccess = PRINTER_ALL_ACCESS }))
        {
            SetPrinter(handle, 0, IntPtr.Zero, PRINTER_CONTROL_PURGE);
            Log(DeletePrinter(handle) ? "Printer removed." : $"DeletePrinter failed: {LastError()}");
            ClosePrinter(handle);
        }

        foreach (string driver in new[] { DriverName, LegacyDriverName })
            Remove($"Driver \"{driver}\"", () => DeletePrinterDriverEx(null, environment, driver, DPD_DELETE_UNUSED_FILES, 0), ERROR_UNKNOWN_PRINTER_DRIVER);
        DeleteLegacyPort();
        foreach (string monitor in new[] { MonitorName, LegacyMonitorName })
            Remove($"Monitor \"{monitor}\"", () => DeleteMonitor(null, null, monitor), ERROR_UNKNOWN_PRINT_MONITOR);
        Registry.LocalMachine.DeleteSubKeyTree(MonitorKey, false);

        foreach (string dll in LegacyMonitorDlls.Append(MonitorDll))
            DeleteFile(Path.Combine(Environment.SystemDirectory, dll));
    }

    // The 1.x monitor keeps its port registered; a monitor with ports cannot be removed.
    private static void DeleteLegacyPort()
    {
        if (!OpenPrinter(",XcvMonitor " + LegacyMonitorName, out IntPtr xcv, new PRINTER_DEFAULTS { DesiredAccess = SERVER_ACCESS_ADMINISTER }))
            return;
        byte[] port = Encoding.Unicode.GetBytes(LegacyPortName + "\0");
        if (XcvData(xcv, "DeletePort", port, (uint)port.Length, IntPtr.Zero, 0, out _, out uint status) && status == 0)
            Log($"Port \"{LegacyPortName}\" removed.");
        ClosePrinter(xcv);
    }

    // A deleted printer takes a moment to go away; until then its driver and monitor are in use.
    private static void Remove(string what, Func<bool> delete, int notInstalledError)
    {
        for (int attempt = 1; ; attempt++)
        {
            if (delete())
            {
                Log($"{what} removed.");
                return;
            }
            int error = Marshal.GetLastWin32Error();
            if (error == notInstalledError)
                return;
            if (attempt == 10)
            {
                Log($"Removing {what} failed: {new Win32Exception(error).Message}");
                return;
            }
            Thread.Sleep(500);
        }
    }

    private static string FindPscriptDirectory(bool arm64)
    {
        string repository = Path.Combine(Environment.SystemDirectory, "DriverStore", "FileRepository");
        string folder = Directory.GetDirectories(repository, arm64 ? "ntprint.inf_arm64_*" : "ntprint.inf_amd64_*")
            .SelectMany(package => Directory.GetDirectories(package))
            .Where(directory => PscriptFiles.All(file => File.Exists(Path.Combine(directory, file))))
            .OrderByDescending(Directory.GetLastWriteTimeUtc)
            .FirstOrDefault();
        return folder ?? throw new FileNotFoundException("The Microsoft PostScript class driver (pscript5) was not found in the Windows driver store.");
    }

    private static void ReplaceFile(string source, string target)
    {
        // The spooler would keep using a still loaded monitor of the same name instead of
        // loading the new file: restart it in that case.
        if (!DeleteFile(target))
            RestartSpooler();
        File.Copy(source, target, false);
    }

    // Returns false if the file is still in use: it is then renamed and deleted at the next restart.
    private static bool DeleteFile(string path)
    {
        if (!File.Exists(path))
            return true;
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            string old = path + "." + DateTime.Now.Ticks + ".old";
            File.Move(path, old);
            MoveFileEx(old, null, MOVEFILE_DELAY_UNTIL_REBOOT);
            return false;
        }
    }

    private static void RestartSpooler()
    {
        Log("Restarting the print spooler.");
        using (var spooler = new ServiceController("Spooler"))
        {
            spooler.Stop();
            spooler.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            spooler.Start();
            spooler.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
        }
    }

    private static void Check(bool success, string function)
    {
        if (!success)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"{function} failed: {LastError()}");
    }

    private static string LastError() => new Win32Exception(Marshal.GetLastWin32Error()).Message;

    private static void Log(string message)
    {
        Console.WriteLine(message);
        try
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "miPDFconvert");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "PrinterSetup.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}\r\n");
        }
        catch (Exception)
        {
            // logging must never break the setup
        }
    }

    // ---- Win32 -------------------------------------------------------------------------------------

    private const uint DPD_DELETE_UNUSED_FILES = 0x1;
    private const uint PRINTER_ATTRIBUTE_LOCAL = 0x40;
    private const uint PRINTER_CONTROL_PURGE = 3;
    private const int PRINTER_ALL_ACCESS = 0xF000C;
    private const int SERVER_ACCESS_ADMINISTER = 0x1;
    private const int ERROR_UNKNOWN_PRINTER_DRIVER = 1797;
    private const int ERROR_UNKNOWN_PRINT_MONITOR = 3000;
    private const int MOVEFILE_DELAY_UNTIL_REBOOT = 0x4;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITOR_INFO_2
    {
        public string pName, pEnvironment, pDLLName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DRIVER_INFO_3
    {
        public uint cVersion;
        public string pName, pEnvironment, pDriverPath, pDataFile, pConfigFile, pHelpFile, pDependentFiles, pMonitorName, pDefaultDataType;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PRINTER_INFO_2
    {
        public string pServerName, pPrinterName, pShareName, pPortName, pDriverName, pComment, pLocation;
        public IntPtr pDevMode;
        public string pSepFile, pPrintProcessor, pDatatype, pParameters;
        public IntPtr pSecurityDescriptor;
        public uint Attributes, Priority, DefaultPriority, StartTime, UntilTime, Status, cJobs, AveragePPM;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private class PRINTER_DEFAULTS
    {
        public string pDatatype;
        public IntPtr pDevMode;
        public int DesiredAccess;
    }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool AddMonitor(string pName, uint level, ref MONITOR_INFO_2 pMonitors);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool DeleteMonitor(string pName, string pEnvironment, string pMonitorName);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetPrinterDriverDirectory(string pName, string pEnvironment, uint level, StringBuilder pDriverDirectory, int cbBuf, out int pcbNeeded);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool AddPrinterDriver(string pName, uint level, ref DRIVER_INFO_3 pDriverInfo);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool DeletePrinterDriverEx(string pName, string pEnvironment, string pDriverName, uint dwDeleteFlag, uint dwVersionFlag);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr AddPrinter(string pName, uint level, ref PRINTER_INFO_2 pPrinter);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, PRINTER_DEFAULTS pDefault);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool SetPrinter(IntPtr hPrinter, uint level, IntPtr pPrinter, uint command);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool DeletePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool XcvData(IntPtr hXcv, string pszDataName, byte[] pInputData, uint cbInputData, IntPtr pOutputData, uint cbOutputData, out uint pcbOutputNeeded, out uint pdwStatus);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileEx(string lpExistingFileName, string lpNewFileName, int dwFlags);
}
