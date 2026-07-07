using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace miMonitor.SetupHelper.Utilities
{
    public class PrinterHelper
    {
        private const int ERROR_INSUFFICIENT_BUFFER = 122;

        /// <summary>
        ///     List all printers that use the miPDFconvert printer driver
        /// </summary>
        /// <returns>A Collection of miPDFconvert printers</returns>
        public ICollection<string> GetmiPDFconvertPrinters()
        {
            var printerInfos = EnumPrinters(PrinterEnumFlags.PRINTER_ENUM_LOCAL);

            var printers = new List<string>();

            foreach (var printer in printerInfos)
                if (printer.pDriverName.Equals("miPDFconvert Virtual Printer", StringComparison.OrdinalIgnoreCase))
                    printers.Add(printer.pPrinterName);

            printers.Sort();

            return printers;
        }

        private IEnumerable<PRINTER_INFO_2> EnumPrinters(PrinterEnumFlags flags)
        {
            uint cbNeeded = 0;
            uint cReturned = 0;
            if (EnumPrinters(flags, null, 2, IntPtr.Zero, 0, ref cbNeeded, ref cReturned)) return new PRINTER_INFO_2[0];
            var lastWin32Error = Marshal.GetLastWin32Error();
            if (lastWin32Error == ERROR_INSUFFICIENT_BUFFER)
            {
                var pAddr = Marshal.AllocHGlobal((int)cbNeeded);
                try
                {
                    if (EnumPrinters(flags, null, 2, pAddr, cbNeeded, ref cbNeeded, ref cReturned))
                    {
                        var printerInfo2 = new PRINTER_INFO_2[cReturned];
                        var offset = pAddr;
                        var type = typeof(PRINTER_INFO_2);
                        var increment = Marshal.SizeOf(type);
                        for (var i = 0; i < cReturned; i++)
                        {
                            printerInfo2[i] = (PRINTER_INFO_2)Marshal.PtrToStructure(offset, type);
                            offset += increment;
                        }

                        return printerInfo2;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(pAddr);
                }
            }
            return new PRINTER_INFO_2[0];
        }

        #region Windows Spooler

        [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool EnumPrinters(PrinterEnumFlags flags, string name, uint level, IntPtr pPrinterEnum,
            uint cbBuf, ref uint pcbNeeded, ref uint pcReturned);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct PRINTER_INFO_2
        {
            [MarshalAs(UnmanagedType.LPTStr)] public string pServerName;
            [MarshalAs(UnmanagedType.LPTStr)] public string pPrinterName;
            [MarshalAs(UnmanagedType.LPTStr)] public string pShareName;
            [MarshalAs(UnmanagedType.LPTStr)] public string pPortName;
            [MarshalAs(UnmanagedType.LPTStr)] public string pDriverName;
            [MarshalAs(UnmanagedType.LPTStr)] public string pComment;
            [MarshalAs(UnmanagedType.LPTStr)] public string pLocation;
            public IntPtr pDevMode;
            [MarshalAs(UnmanagedType.LPTStr)] public string pSepFile;
            [MarshalAs(UnmanagedType.LPTStr)] public string pPrintProcessor;
            [MarshalAs(UnmanagedType.LPTStr)] public string pDatatype;
            [MarshalAs(UnmanagedType.LPTStr)] public string pParameters;
            public IntPtr pSecurityDescriptor;
            public uint Attributes;
            public uint Priority;
            public uint DefaultPriority;
            public uint StartTime;
            public uint UntilTime;
            public uint Status;
            public uint cJobs;
            public uint AveragePPM;
        }

        [Flags]
        private enum PrinterEnumFlags
        {
            PRINTER_ENUM_LOCAL = 0x00000002
        }

        #endregion Windows Spooler
    }
}
