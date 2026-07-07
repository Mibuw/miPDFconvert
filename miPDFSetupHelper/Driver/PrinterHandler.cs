using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Windows.Forms;
using miMonitor.SetupHelper.Helper;
using Microsoft.Win32;

namespace miMonitor.SetupHelper.Driver
{
    public class miPDFconvertInstaller
    {
        private const uint DPD_DELETE_UNUSED_FILES = 0x00000001;

        private const int WIN32_FILE_ALREADY_EXISTS = 183; // Returned by XcvData "AddPort" if the port already exists

        private const string ENVIRONMENT = null;
        private const string PRINTERNAME = "miPDFconvert";
        private const string DRIVERNAME = "miPDFconvert Virtual Printer";
        private const string HARDWAREID = "miPDFconvert_Driver";
        private const string PORTMONITOR = "MIMONITOR";
        private const string MONITORDLL = "miMonitor.dll";
        private const string MONITORUIDLL = "miMonitorUI.dll";
        private const string PORTNAME = "MIMONITOR:";
        private const string PRINTPROCESOR = "winprint";

        private const string DRIVERMANUFACTURER = "Wolfgang Mitterbucher // miPDFconvert";

        private const string DRIVERFILE = "PSCRIPT5.DLL";
        private const string DRIVERUIFILE = "PS5UI.DLL";
        private const string DRIVERHELPFILE = "PSCRIPT.HLP";
        private const string DRIVERDATAFILE = "ghostpdf.ppd";

        private readonly String[] printerDriverFiles = new String[] { DRIVERFILE, DRIVERUIFILE, DRIVERHELPFILE, DRIVERDATAFILE };
        private readonly String[] printerDriverDependentFiles = new String[] { "PSCRIPT.NTF" };

        #region Error messages for Trace/Debug

        private const string FILENOTDELETED_INUSE = "{0} is being used by another process. File was not deleted.";

        private const string FILENOTCOPIED_PRINTERDRIVER = "Printer driver file was not copied. Exception message: {0}";
        private const string FILENOTCOPIED_ALREADYEXISTS = "Destination file {0} was not copied/created - it already exists.";

        private const string WIN32ERROR = "Win32 error code {0}.";

        private const string NATIVE_COULDNOTENABLE64REDIRECTION = "Could not enable 64-bit file system redirection.";
        private const string NATIVE_COULDNOTREVERT64REDIRECTION = "Could not revert 64-bit file system redirection.";

        private const string REGISTRYCONFIG_NOT_ADDED = "Could not add port configuration to registry. Exception message: {0}";
        private const string REGISTRYCONFIG_NOT_DELETED = "Could not delete port configuration from registry. Exception message: {0}";

        private const String INFO_INSTALLPORTMONITOR_FAILED = "Port monitor installation failed.";
        private const String INFO_INSTALLCOPYDRIVER_FAILED = "Could not copy printer driver files.";
        private const String INFO_INSTALLPORT_FAILED = "Could not add redirected port.";
        private const String INFO_INSTALLPRINTERDRIVER_FAILED = "Printer driver installation failed.";
        private const String INFO_INSTALLPRINTER_FAILED = "Could not add printer.";
        private const String INFO_INSTALLCONFIGPORT_FAILED = "Port configuration failed.";

        #endregion Error messages for Trace/Debug

        #region Port operations

        private bool AddmiPDFconvertPort()
        {
            int portAddResult = DoXcvDataPortOperation(PORTNAME, PORTMONITOR, "AddPort");
            // Port already exists - this is OK, we'll just keep using it
            return portAddResult == 0 || portAddResult == WIN32_FILE_ALREADY_EXISTS;
        }

        private bool DeletemiPDFconvertPort()
        {
            return DoXcvDataPortOperation(PORTNAME, PORTMONITOR, "DeletePort") == 0;
        }

        /// <remarks>I can't remember the name/link of the developer who wrote this code originally,
        /// so I can't provide a link or credit.</remarks>
        private int DoXcvDataPortOperation(string portName, string portMonitor, string xcvDataOperation)
        {
            int win32ErrorCode;

            PRINTER_DEFAULTS def = new PRINTER_DEFAULTS();

            def.pDatatype = null;
            def.pDevMode = IntPtr.Zero;
            def.DesiredAccess = 1; //Server Access Administer

            IntPtr hPrinter = IntPtr.Zero;

            if (NativeMethods.OpenPrinter(",XcvMonitor " + portMonitor, ref hPrinter, def) != 0)
            {
                if (!portName.EndsWith("\0"))
                    portName += "\0"; // Must be a null terminated string

                // Must get the size in bytes. Rememeber .NET strings are formed by 2-byte characters
                uint size = (uint)(portName.Length * 2);

                // Alloc memory in HGlobal to set the portName
                IntPtr portPtr = Marshal.AllocHGlobal((int)size);
                Marshal.Copy(portName.ToCharArray(), 0, portPtr, portName.Length);

                uint needed; // Not that needed in fact...
                uint xcvResult; // Will receive de result here

                NativeMethods.XcvData(hPrinter, xcvDataOperation, portPtr, size, IntPtr.Zero, 0, out needed, out xcvResult);

                NativeMethods.ClosePrinter(hPrinter);
                Marshal.FreeHGlobal(portPtr);
                win32ErrorCode = (int)xcvResult;
            }
            else
            {
                win32ErrorCode = Marshal.GetLastWin32Error();
            }
            return win32ErrorCode;
        }

        #endregion Port operations

        #region Port Monitor

        /// <summary>
        /// Adds the miPDFconvert port monitor
        /// </summary>
        /// <param name="monitorFilePath">Directory where the uninstalled monitor dll is located</param>
        /// <returns>true if the monitor is installed, false if install failed</returns>
        private bool AddmiPDFconvertPortMonitor(String monitorFilePath)
        {
            bool monitorAdded = false;

            IntPtr oldRedirectValue = IntPtr.Zero;

            try
            {
                oldRedirectValue = DisableWow64Redirection();
                Console.WriteLine("Path: " + monitorFilePath);

                // Copy the monitor DLLs to the system directory
                String monitorfileSourcePath = Path.Combine(monitorFilePath, MONITORDLL);
                String monitorfileDestinationPath = Path.Combine(Environment.SystemDirectory, MONITORDLL);
                String monitoruifileSourcePath = Path.Combine(monitorFilePath, MONITORUIDLL);
                String monitoruifileDestinationPath = Path.Combine(Environment.SystemDirectory, MONITORUIDLL);

                Spooler.stop();

                try
                {
                    File.Copy(monitoruifileSourcePath, monitoruifileDestinationPath, true);
                    Console.WriteLine("MonitorUISourcePath: " + monitoruifileSourcePath + " DEST: " + monitoruifileDestinationPath);
                }
                catch { }

                try
                {
                    File.Copy(monitorfileSourcePath, monitorfileDestinationPath, true);
                    Console.WriteLine("MonitorSourcePath: " + monitorfileSourcePath + " DEST: " + monitorfileDestinationPath);
                }
                catch { }

                Spooler.start();

                MONITOR_INFO_2 newMonitor = new MONITOR_INFO_2();
                newMonitor.pName = PORTMONITOR;
                newMonitor.pEnvironment = ENVIRONMENT;
                newMonitor.pDLLName = MONITORDLL;
                if (NativeMethods.AddMonitor(null, 2, ref newMonitor) != 0)
                    monitorAdded = true;
                else
                    Console.WriteLine(String.Format("Could not add port monitor {0}", PORTMONITOR) + Environment.NewLine +
                                              String.Format(WIN32ERROR, Marshal.GetLastWin32Error().ToString()));
            }
            finally
            {
                if (oldRedirectValue != IntPtr.Zero) RevertWow64Redirection(oldRedirectValue);
            }

            return monitorAdded;
        }

        /// <summary>
        /// Disables WOW64 system directory file redirection
        /// if the current process is both
        /// 32-bit, and running on a 64-bit OS
        /// </summary>
        /// <returns>A Handle, which should be retained to reenable redirection</returns>
        private IntPtr DisableWow64Redirection()
        {
            IntPtr oldValue = IntPtr.Zero;
            if (Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess)
                if (!NativeMethods.Wow64DisableWow64FsRedirection(ref oldValue))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not disable Wow64 file system redirection.");
            return oldValue;
        }

        /// <summary>
        /// Reenables WOW64 system directory file redirection
        /// if the current process is both
        /// 32-bit, and running on a 64-bit OS
        /// </summary>
        /// <param name="oldValue">A Handle value - should be retained from call to <see cref="DisableWow64Redirection"/></param>
        private void RevertWow64Redirection(IntPtr oldValue)
        {
            if (Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess)
            {
                if (!NativeMethods.Wow64RevertWow64FsRedirection(oldValue))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not reenable Wow64 file system redirection.");
                }
            }
        }

        /// <summary>
        /// Removes the miPDFconvert port monitor
        /// </summary>
        /// <returns>true if monitor successfully removed, false if removal failed</returns>
        private bool RemovemiPDFconvertPortMonitor()
        {
            bool monitorRemoved = false;
            if ((NativeMethods.DeleteMonitor(null, ENVIRONMENT, PORTMONITOR)) != 0)
            {
                monitorRemoved = true;
                // Try to remove the monitor DLL now
                if (!DeletemiPDFconvertPortMonitorDll())
                {
                    Console.WriteLine("Could not remove port monitor dll.");
                }
            }
            return monitorRemoved;
        }

        private bool DeletemiPDFconvertPortMonitorDll()
        {
            Spooler.stop();

            bool monitorDllRemoved = false;

            String monitorDllFullPathname = String.Empty;
            IntPtr oldRedirectValue = IntPtr.Zero;
            try
            {
                oldRedirectValue = DisableWow64Redirection();

                monitorDllFullPathname = Path.Combine(Environment.SystemDirectory, MONITORDLL);

                File.Delete(monitorDllFullPathname);
                monitorDllRemoved = true;
                File.Delete(Path.Combine(Environment.SystemDirectory, MONITORUIDLL));
            }
            catch (Win32Exception windows32Ex)
            {
                // This one is likely very bad -
                // log and rethrow so we don't continue
                // to try to uninstall
                Console.WriteLine(NATIVE_COULDNOTENABLE64REDIRECTION + String.Format(WIN32ERROR, windows32Ex.NativeErrorCode.ToString()));
                throw;
            }
            catch (IOException)
            {
                // File still in use
                Console.WriteLine(String.Format(FILENOTDELETED_INUSE, monitorDllFullPathname));
            }
            catch (UnauthorizedAccessException)
            {
                // File is readonly, or file permissions do not allow delete
                Console.WriteLine(String.Format(FILENOTDELETED_INUSE, monitorDllFullPathname));
            }
            finally
            {
                try
                {
                    if (oldRedirectValue != IntPtr.Zero) RevertWow64Redirection(oldRedirectValue);
                }
                catch (Win32Exception windows32Ex)
                {
                    // Couldn't turn file redirection back on -
                    // this is not good
                    Console.WriteLine(NATIVE_COULDNOTREVERT64REDIRECTION + String.Format(WIN32ERROR, windows32Ex.NativeErrorCode.ToString()));
                    throw;
                }
            }

            Spooler.start();

            return monitorDllRemoved;
        }

        #endregion Port Monitor

        #region Printer Install

        private String RetrievePrinterDriverDirectory()
        {
            StringBuilder driverDirectory = new StringBuilder(1024);
            int dirSizeInBytes = 0;
            if (!NativeMethods.GetPrinterDriverDirectory(null,
                                                         null,
                                                         1,
                                                         driverDirectory,
                                                         1024,
                                                         ref dirSizeInBytes))
                throw new DirectoryNotFoundException("Could not retrieve printer driver directory.");
            return driverDirectory.ToString();
        }

        /// <summary>
        /// Installs the port monitor, port,
        /// printer drivers, and miPDF virtual printer
        /// </summary>
        /// <param name="driverSourceDirectory">Directory where the uninstalled printer driver files are located</param>
        /// <returns>true if installation suceeds, false if failed</returns>
        public bool InstallmiPDFconvertPrinter(String driverSourceDirectory)
        {
            bool printerInstalled = false;

            // Write the port configuration before the monitor DLL is loaded by the spooler
            ConfiguremiPDFconvertPort();
            if (AddmiPDFconvertPortMonitor(driverSourceDirectory))
            {
                Console.WriteLine("Port monitor successfully installed.");
                if (CopyPrinterDriverFiles(driverSourceDirectory, printerDriverFiles.Concat(printerDriverDependentFiles).ToArray()))
                {
                    Console.WriteLine("Printer drivers copied or already exist.");
                    if (AddmiPDFconvertPort())
                    {
                        Console.WriteLine("Redirection port added.");
                        if (InstallmiPDFconvertPrinterDriver())
                        {
                            Console.WriteLine("Printer driver installed.");
                            if (AddCustommiPDFconvertPrinter(PRINTERNAME))
                            {
                                Console.WriteLine("Virtual printer installed.");
                                if (ConfiguremiPDFconvertPort())
                                {
                                    Console.WriteLine("Printer configured.");
                                    printerInstalled = true;
                                }
                                else
                                    Console.WriteLine(INFO_INSTALLCONFIGPORT_FAILED);
                            }
                            else
                                Console.WriteLine(INFO_INSTALLPRINTER_FAILED);
                        }
                        else
                            Console.WriteLine(INFO_INSTALLPRINTERDRIVER_FAILED);
                    }
                    else
                        Console.WriteLine(INFO_INSTALLPORT_FAILED);
                }
                else
                    Console.WriteLine(INFO_INSTALLCOPYDRIVER_FAILED);
            }
            else
                Console.WriteLine(INFO_INSTALLPORTMONITOR_FAILED);

            return printerInstalled;
        }

        public bool UninstallmiPDFconvertPrinter()
        {
            bool printerUninstalledCleanly = true;

            if (!DeleteCustommiPDFconvertPrinter(PRINTERNAME))
                printerUninstalledCleanly = false;
            if (!RemovemiPDFconvertPrinterDriver())
                printerUninstalledCleanly = false;
            if (!DeletemiPDFconvertPort())
                printerUninstalledCleanly = false;
            if (!RemovemiPDFconvertPortMonitor())
                printerUninstalledCleanly = false;
            if (!RemovemiPDFconvertPortConfig())
                printerUninstalledCleanly = false;
            DeletemiPDFconvertPortMonitorDll();
            return printerUninstalledCleanly;
        }

        private bool CopyPrinterDriverFiles(String driverSourceDirectory,
                                            String[] filesToCopy)
        {
            bool filesCopied = false;
            String driverDestinationDirectory = RetrievePrinterDriverDirectory();
            try
            {
                for (int loop = 0; loop < filesToCopy.Length; loop++)
                {
                    String fileSourcePath = Path.Combine(driverSourceDirectory, filesToCopy[loop]);
                    String fileDestinationPath = Path.Combine(driverDestinationDirectory, filesToCopy[loop]);
                    try
                    {
                        File.Copy(fileSourcePath, fileDestinationPath, true);
                    }
                    catch (PathTooLongException)
                    {
                        // Will be caught by outer
                        // IOException catch block
                        throw;
                    }
                    catch (DirectoryNotFoundException)
                    {
                        // Will be caught by outer
                        // IOException catch block
                        throw;
                    }
                    catch (FileNotFoundException)
                    {
                        // Will be caught by outer
                        // IOException catch block
                        throw;
                    }
                    catch (IOException)
                    {
                        // Just keep going - file was already there
                        // Not really a problem
                        Console.WriteLine(String.Format(FILENOTCOPIED_ALREADYEXISTS, fileDestinationPath));
                        continue;
                    }
                }
                filesCopied = true;
            }
            catch (IOException ioEx)
            {
                Console.WriteLine(String.Format(FILENOTCOPIED_PRINTERDRIVER, ioEx.Message));
            }
            catch (UnauthorizedAccessException unauthorizedEx)
            {
                Console.WriteLine(String.Format(FILENOTCOPIED_PRINTERDRIVER, unauthorizedEx.Message));
            }
            catch (NotSupportedException notSupportedEx)
            {
                Console.WriteLine(String.Format(FILENOTCOPIED_PRINTERDRIVER, notSupportedEx.Message));
            }

            return filesCopied;
        }

        private bool IsPrinterDriverInstalled(String driverName)
        {
            return EnumeratePrinterDrivers().Any(printerDriver => printerDriver.pName == driverName);
        }

        private List<DRIVER_INFO_6> EnumeratePrinterDrivers()
        {
            List<DRIVER_INFO_6> installedPrinterDrivers = new List<DRIVER_INFO_6>();

            uint pcbNeeded = 0;
            uint pcReturned = 0;

            if (!NativeMethods.EnumPrinterDrivers(null, ENVIRONMENT, 6, IntPtr.Zero, 0, ref pcbNeeded, ref pcReturned))
            {
                IntPtr pDrivers = Marshal.AllocHGlobal((int)pcbNeeded);
                if (NativeMethods.EnumPrinterDrivers(null, ENVIRONMENT, 6, pDrivers, pcbNeeded, ref pcbNeeded, ref pcReturned))
                {
                    IntPtr currentDriver = pDrivers;
                    for (int loop = 0; loop < pcReturned; loop++)
                    {
                        installedPrinterDrivers.Add((DRIVER_INFO_6)Marshal.PtrToStructure(currentDriver, typeof(DRIVER_INFO_6)));
                        currentDriver = IntPtr.Add(currentDriver, Marshal.SizeOf(typeof(DRIVER_INFO_6)));
                    }
                    Marshal.FreeHGlobal(pDrivers);
                }
                else
                {
                    // Failed to enumerate printer drivers
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not enumerate printer drivers.");
                }
            }
            else
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Call to EnumPrinterDrivers in winspool.drv succeeded with a zero size buffer - unexpected error.");
            }

            return installedPrinterDrivers;
        }

        private bool InstallmiPDFconvertPrinterDriver()
        {
            if (IsPrinterDriverInstalled(DRIVERNAME))
                return true; // Driver is already installed, we'll just use the installed driver

            String driverSourceDirectory = RetrievePrinterDriverDirectory();

            StringBuilder nullTerminatedDependentFiles = new StringBuilder();
            if (printerDriverDependentFiles.Length > 0)
            {
                for (int loop = 0; loop <= printerDriverDependentFiles.GetUpperBound(0); loop++)
                {
                    nullTerminatedDependentFiles.Append(printerDriverDependentFiles[loop]);
                    nullTerminatedDependentFiles.Append("\0");
                }
                nullTerminatedDependentFiles.Append("\0");
            }
            else
            {
                nullTerminatedDependentFiles.Append("\0\0");
            }

            DRIVER_INFO_6 printerDriverInfo = new DRIVER_INFO_6();

            printerDriverInfo.cVersion = 3;
            printerDriverInfo.pName = DRIVERNAME;
            printerDriverInfo.pEnvironment = ENVIRONMENT;
            printerDriverInfo.pDriverPath = Path.Combine(driverSourceDirectory, DRIVERFILE);
            printerDriverInfo.pConfigFile = Path.Combine(driverSourceDirectory, DRIVERUIFILE);
            printerDriverInfo.pHelpFile = Path.Combine(driverSourceDirectory, DRIVERHELPFILE);
            printerDriverInfo.pDataFile = Path.Combine(driverSourceDirectory, DRIVERDATAFILE);
            printerDriverInfo.pDependentFiles = nullTerminatedDependentFiles.ToString();

            printerDriverInfo.pMonitorName = PORTMONITOR;
            printerDriverInfo.pDefaultDataType = String.Empty;
            printerDriverInfo.dwlDriverVersion = 0x0001000000000000U;
            printerDriverInfo.pszMfgName = DRIVERMANUFACTURER;
            printerDriverInfo.pszHardwareID = HARDWAREID;
            printerDriverInfo.pszProvider = DRIVERMANUFACTURER;

            if (!NativeMethods.AddPrinterDriver(null, 6, ref printerDriverInfo))
            {
                Console.WriteLine("Could not add miPDFconvert printer driver. " +
                                          String.Format(WIN32ERROR, Marshal.GetLastWin32Error().ToString()));
                return false;
            }
            return true;
        }

        public bool RemovemiPDFconvertPrinterDriver()
        {
            bool driverRemoved = NativeMethods.DeletePrinterDriverEx(null, ENVIRONMENT, DRIVERNAME, DPD_DELETE_UNUSED_FILES, 3);
            if (!driverRemoved)
            {
                Console.WriteLine("Could not remove miPDFconvert printer driver. " +
                                          String.Format(WIN32ERROR, Marshal.GetLastWin32Error().ToString()));
            }
            return driverRemoved;
        }

        public bool AddCustommiPDFconvertPrinter(string name)
        {
            bool printerAdded = false;
            PRINTER_INFO_2 miPDFconvertPrinter = new PRINTER_INFO_2();

            miPDFconvertPrinter.pServerName = null;
            miPDFconvertPrinter.pPrinterName = name;
            miPDFconvertPrinter.pPortName = PORTNAME;
            miPDFconvertPrinter.pDriverName = DRIVERNAME;
            miPDFconvertPrinter.pPrintProcessor = PRINTPROCESOR;
            miPDFconvertPrinter.pDatatype = "RAW";
            miPDFconvertPrinter.Attributes = 0x00000041;

            int miPDFconvertPrinterHandle = NativeMethods.AddPrinter(null, 2, ref miPDFconvertPrinter);
            if (miPDFconvertPrinterHandle != 0)
            {
                // Added ok
                NativeMethods.ClosePrinter((IntPtr)miPDFconvertPrinterHandle);
                printerAdded = true;
            }
            else
            {
                Console.WriteLine("Could not add miPDFconvert virtual printer. " +
                                  String.Format(WIN32ERROR, Marshal.GetLastWin32Error().ToString()));
            }
            return printerAdded;
        }

        public bool DeleteCustommiPDFconvertPrinter(string name)
        {
            bool printerDeleted = false;

            PRINTER_DEFAULTS miPDFconvertDefaults = new PRINTER_DEFAULTS();
            miPDFconvertDefaults.DesiredAccess = 0x000F000C; // All access
            miPDFconvertDefaults.pDatatype = null;
            miPDFconvertDefaults.pDevMode = IntPtr.Zero;

            IntPtr miPDFconvertHandle = IntPtr.Zero;
            try
            {
                if (NativeMethods.OpenPrinter(name, ref miPDFconvertHandle, miPDFconvertDefaults) != 0)
                {
                    if (NativeMethods.DeletePrinter(miPDFconvertHandle))
                        printerDeleted = true;
                }
                else
                {
                    Console.WriteLine("Could not delete miPDFconvert virtual printer. " +
                                      String.Format(WIN32ERROR, Marshal.GetLastWin32Error().ToString()));
                }
            }
            finally
            {
                if (miPDFconvertHandle != IntPtr.Zero) NativeMethods.ClosePrinter(miPDFconvertHandle);
            }
            return printerDeleted;
        }

        #endregion Printer Install

        #region Configuration and Registry changes

        private bool ConfiguremiPDFconvertPort()
        {
            bool registryChangesMade = false;
            // Add all the registry info
            // for the port and monitor
            RegistryKey portConfiguration;
            try
            {
                portConfiguration = Registry.LocalMachine.CreateSubKey("SYSTEM\\CurrentControlSet\\Control\\Print\\Monitors\\" + PORTMONITOR);
                portConfiguration.SetValue("LogLevel", 0, RegistryValueKind.DWord);
                portConfiguration = Registry.LocalMachine.CreateSubKey("SYSTEM\\CurrentControlSet\\Control\\Print\\Monitors\\" + PORTMONITOR + "\\" + PORTMONITOR + ":");
                portConfiguration.SetValue("Domain", ".", RegistryValueKind.String);
                portConfiguration.SetValue("ExecPath", Path.GetDirectoryName(Application.ExecutablePath), RegistryValueKind.String);
                portConfiguration.SetValue("FilePattern", "", RegistryValueKind.String);
                portConfiguration.SetValue("HideProcess", 0, RegistryValueKind.DWord);
                portConfiguration.SetValue("OutputPath", "", RegistryValueKind.String);
                portConfiguration.SetValue("Overwrite", 1, RegistryValueKind.DWord);
                portConfiguration.SetValue("Password", new byte[] { 0000, 00, 00, 00, 00, 00 }, RegistryValueKind.Binary);
                portConfiguration.SetValue("PipeData", 0, RegistryValueKind.DWord);
                portConfiguration.SetValue("RunAsPUser", 1, RegistryValueKind.DWord);
                portConfiguration.SetValue("User", "", RegistryValueKind.String);
                portConfiguration.SetValue("WaitTermination", 0, RegistryValueKind.DWord);
                portConfiguration.SetValue("WaitTimeout", 0, RegistryValueKind.DWord);
                portConfiguration.SetValue("Description", "miPDFconvert", RegistryValueKind.String);
                portConfiguration.SetValue("UserCommand", "\"" + Path.GetDirectoryName(Application.ExecutablePath) + @"\miPDFconvertBase.exe" + "\"", RegistryValueKind.String);
                portConfiguration.SetValue("Printer", PRINTERNAME, RegistryValueKind.String);
                registryChangesMade = true;
            }
            catch (UnauthorizedAccessException unauthorizedEx)
            {
                Console.WriteLine(String.Format(REGISTRYCONFIG_NOT_ADDED, unauthorizedEx.Message));
            }
            catch (SecurityException securityEx)
            {
                Console.WriteLine(String.Format(REGISTRYCONFIG_NOT_ADDED, securityEx.Message));
            }

            return registryChangesMade;
        }

        private bool RemovemiPDFconvertPortConfig()
        {
            bool registryEntriesRemoved = false;

            try
            {
                Registry.LocalMachine.DeleteSubKey("SYSTEM\\CurrentControlSet\\Control\\Print\\Monitors\\" +
                                                    PORTMONITOR + "\\Ports\\" + PORTNAME, false);
                registryEntriesRemoved = true;
            }
            catch (UnauthorizedAccessException unauthorizedEx)
            {
                Console.WriteLine(String.Format(REGISTRYCONFIG_NOT_DELETED, unauthorizedEx.Message));
            }

            return registryEntriesRemoved;
        }

        #endregion Configuration and Registry changes
    }
}
