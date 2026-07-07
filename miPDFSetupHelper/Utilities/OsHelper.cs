using System;
using System.Runtime.InteropServices;

namespace miMonitor.SetupHelper.Utilities
{
    public class OsHelper
    {
        public bool IsArm64()
        {
            try
            {
                var handle = System.Diagnostics.Process.GetCurrentProcess().Handle;
                IsWow64Process2(handle, out _, out var nativeMachine);

                return nativeMachine == 0xaa64;
            }
            catch
            {
                return false;
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool IsWow64Process2(
            IntPtr process,
            out ushort processMachine,
            out ushort nativeMachine
        );
    }
}
