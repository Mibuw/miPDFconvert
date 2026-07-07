// miPDFconvert - virtual PDF printer
// Copyright (C) 2026 Wolfgang Mitterbucher (mitterbucher.com)
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU Affero General Public License as published by
// the Free Software Foundation, either version 3 of the License, or (at your
// option) any later version. This program is distributed WITHOUT ANY WARRANTY.
// See the GNU AGPL v3 (LICENSE file) for details and THIRD-PARTY-NOTICES.md
// for the licenses of bundled components.

using log4net;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

//PInvoke CreateProcessAsUser
//The following code demonstrates how to PInvoke CreateProcessAsUser
//without the logon credentials.
//This code works best when run as an Admin user.
//credits to https://veryblue.wordpress.com/code-snippets/pinvoke-createprocessasuser/

namespace miPDFconvertBase
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SECURITY_ATTRIBUTES
    {
        public uint nLength;
        public IntPtr lpSecurityDescriptor;
        public bool bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct STARTUPINFO
    {
        public uint cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public uint dwX;
        public uint dwY;
        public uint dwXSize;
        public uint dwYSize;
        public uint dwXCountChars;
        public uint dwYCountChars;
        public uint dwFillAttribute;
        public uint dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    internal enum SECURITY_IMPERSONATION_LEVEL
    {
        SecurityAnonymous,
        SecurityIdentification,
        SecurityImpersonation,
        SecurityDelegation
    }

    internal enum TOKEN_TYPE
    {
        TokenPrimary = 1,
        TokenImpersonation
    }

    internal class ProcessUtils
    {
        const int STD_INPUT_HANDLE = -10;

        private static readonly ILog LOGGER = LogManager.GetLogger(typeof(ProcessUtils));

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool CreateProcessAsUser(
            IntPtr hToken,
            string? lpApplicationName,
            string lpCommandLine,
            ref SECURITY_ATTRIBUTES lpProcessAttributes,
            ref SECURITY_ATTRIBUTES lpThreadAttributes,
            bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string? lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("advapi32.dll", EntryPoint = "DuplicateTokenEx", SetLastError = true)]
        private static extern bool DuplicateTokenEx(
            IntPtr hExistingToken,
            uint dwDesiredAccess,
            ref SECURITY_ATTRIBUTES lpThreadAttributes,
            Int32 ImpersonationLevel,
            Int32 dwTokenType,
            ref IntPtr phNewToken);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(
            IntPtr ProcessHandle,
            UInt32 DesiredAccess,
            ref IntPtr TokenHandle);

        [DllImport("userenv.dll", SetLastError = true)]
        private static extern bool CreateEnvironmentBlock(
                ref IntPtr lpEnvironment,
                IntPtr hToken,
                bool bInherit);

        [DllImport("userenv.dll", SetLastError = true)]
        private static extern bool DestroyEnvironmentBlock(
                IntPtr lpEnvironment);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        public static extern IntPtr GetStdHandle(int whichHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(
            IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern UInt32 WaitForSingleObject(IntPtr handle, UInt32 milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetExitCodeProcess(IntPtr process, ref UInt32 exitCode);

        private const short SW_SHOW = 5;
        private const uint TOKEN_QUERY = 0x0008;
        private const uint TOKEN_DUPLICATE = 0x0002;
        private const uint TOKEN_ASSIGN_PRIMARY = 0x0001;
        private const int STARTF_USESHOWWINDOW = 0x00000001;
        private const int STARTF_FORCEONFEEDBACK = 0x00000040;
        private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

        private const string EXPLORER_PROCESS_NAME = "explorer";

        public static void LaunchUsingExplorerProcessOfUser(string strCommandLine, string strUserName)
        {
            Launch(strCommandLine, strUserName, EXPLORER_PROCESS_NAME);
        }

        private static void Launch(string strCommandLine, string strUserName, string strProcessName)
        {
            LOGGER.Debug($"Trying to use environment of process \"{strProcessName}\" of user \"{strUserName}\"...");
            try
            {
                Process? matchingProcess = FindProcessOfUser(strUserName, strProcessName);

                if (matchingProcess == null)
                {
                    LOGGER.Error($"No \"{strProcessName}\" process found. Cannot launch miPDFconvert as logged on Windows user.");
                    return;
                }

                int nMatchingProcessId = matchingProcess.Id;
                LOGGER.Debug($"  Matching \"{strProcessName}\" process of user \"{strUserName}\" found with ID {matchingProcess.Id}.");
                Launch(strCommandLine, nMatchingProcessId);
            }
            catch (Exception ex)
            {
                LOGGER.Error($"Exception in {nameof(Launch)} executing \"{strCommandLine}\" using process \"{strProcessName}\" of user \"{strUserName}\":", ex);
            }
        }

        private static void Launch(string strCommandLine, int nProcessId)
        {
            LOGGER.Info($"Now executing '{strCommandLine}' using environment of process {nProcessId}...");

            if (nProcessId <= 1)
            {
                LOGGER.Error($"Invalid process ID {nProcessId}! Cannot use it for launching miPDFconvert with \"{strCommandLine}\".");
                return;
            }

            IntPtr token = GetPrimaryToken(nProcessId);
            if (token == IntPtr.Zero)
            {
                LOGGER.Error($"Process {nProcessId} has no primary token! Cannot use it for launching miPDFconvert with \"{strCommandLine}\".");
                return;
            }

            IntPtr envBlock = GetEnvironmentBlock(token);
            LaunchProcessAsUser(strCommandLine, token, envBlock);
            if (envBlock != IntPtr.Zero)
                DestroyEnvironmentBlock(envBlock);

            CloseHandle(token);
        }

        private static bool LaunchProcessAsUser(string strCommandLine, IntPtr token, IntPtr envBlock)
        {
            bool result = false;

            var processInfo = new PROCESS_INFORMATION();
            var saProcess = new SECURITY_ATTRIBUTES();
            var saThread = new SECURITY_ATTRIBUTES();
            saProcess.nLength = (uint)Marshal.SizeOf(saProcess);
            saThread.nLength = (uint)Marshal.SizeOf(saThread);

            var startupInfo = new STARTUPINFO();
            startupInfo.cb = (uint)Marshal.SizeOf(startupInfo);

            //if this member is NULL, the new process inherits the desktop 
            //and window station of its parent process. If this member is 
            //an empty string, the process does not inherit the desktop and 
            //window station of its parent process; instead, the system 
            //determines if a new desktop and window station need to be created. 
            //If the impersonated user already has a desktop, the system uses the 
            //existing desktop.

            IntPtr stdinHandle = GetStdHandle(STD_INPUT_HANDLE);

            startupInfo.lpDesktop = @"WinSta0\Default"; //Modify as needed
            startupInfo.dwFlags = STARTF_USESHOWWINDOW | STARTF_FORCEONFEEDBACK;
            startupInfo.wShowWindow = SW_SHOW;
            startupInfo.hStdInput = stdinHandle;
            //Set other si properties as required.

            result = CreateProcessAsUser(
                token,
                null,
                strCommandLine,
                ref saProcess,
                ref saThread,
                true,
                CREATE_UNICODE_ENVIRONMENT,
                envBlock,
                null,
                ref startupInfo,
                out processInfo);

            if (!result)
            {
                int error = Marshal.GetLastWin32Error();
                LOGGER.Error($"Error in {nameof(CreateProcessAsUser)}: Win32 error {error} for command line \"{strCommandLine}\".");
            }
            else
            {
                LOGGER.Info($"{nameof(CreateProcessAsUser)} succeeded. Child process id {processInfo.dwProcessId} created for command line \"{strCommandLine}\".");
            }

            UInt32 exitCode = 123456;
            if (processInfo.hProcess != IntPtr.Zero)
            {
                LOGGER.Debug($"Waiting (max 180 s) for child process {processInfo.dwProcessId} to exit...");
                UInt32 waitResult = WaitForSingleObject(processInfo.hProcess, 180000);
                if (GetExitCodeProcess(processInfo.hProcess, ref exitCode))
                    LOGGER.Info($"Child process {processInfo.dwProcessId} finished (wait result {waitResult}, exit code {exitCode}).");
                else
                    LOGGER.Warn($"Could not read exit code of child process {processInfo.dwProcessId} (wait result {waitResult}).");

                CloseHandle(processInfo.hProcess);
                if (processInfo.hThread != IntPtr.Zero)
                    CloseHandle(processInfo.hThread);
            }

            CloseHandle(stdinHandle);

            return result;
        }

        private static IntPtr GetEnvironmentBlock(IntPtr token)
        {
            IntPtr envBlock = IntPtr.Zero;
            CreateEnvironmentBlock(ref envBlock, token, false);
            return envBlock;
        }

        private static Process? FindProcessOfUser(string strUsername, string strProcessName)
        {
            strUsername = strUsername.Trim();

            try
            {
                Process[] processesToCheck;
                if (string.IsNullOrEmpty(strProcessName))
                {
                    LOGGER.Debug($"  Getting all processes of user \"{strUsername}\"...");
                    processesToCheck = Process.GetProcesses();
                }
                else
                {
                    LOGGER.Debug($"  Getting all \"{strProcessName}\" processes of user \"{strUsername}\"...");
                    processesToCheck = Process.GetProcessesByName(strProcessName);
                }

                for (int i = 0; i < processesToCheck.Length; i++)
                {
                    Process process = processesToCheck[i];

                    LOGGER.Debug($"    Checking process {process.Id}...");

                    try
                    {
                        IntPtr primaryToken = GetPrimaryToken(process.Id);
                        if (primaryToken != IntPtr.Zero)
                        {
                            var windowsIdentity = new WindowsIdentity(primaryToken);
                            string strOwner = windowsIdentity.Name.Trim();

                            if (strOwner.EndsWith(strUsername, StringComparison.OrdinalIgnoreCase))
                            {
                                LOGGER.Info($"Owner \"{strOwner}\" of process {process.Id} matches required user name \"{strUsername}\". Using this process for launching miPDFconvert.");
                                return process;
                            }

                            LOGGER.Debug($"      Owner \"{strOwner}\" does not match required user name \"{strUsername}\". Cannot use this process for launching miPDFconvert.");
                        }
                        else
                            LOGGER.Debug($"      Primary token of process is 0. Cannot use this process for launching miPDFconvert.");
                    }
                    catch (Exception ex)
                    {
                        LOGGER.Debug($"      Could not determine primary token or owner of process {process.Id}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                LOGGER.Error($"Failed to check all{(string.IsNullOrEmpty(strProcessName) ? "" : $" \"{strProcessName}\"")} processes of user \"{strUsername}\": {ex.Message}");
            }

            return null;
        }

        private static IntPtr GetPrimaryToken(int processId)
        {
            IntPtr token = IntPtr.Zero;
            IntPtr primaryToken = IntPtr.Zero;

            Process process = Process.GetProcessById(processId);

            bool retVal = OpenProcessToken(process.Handle, TOKEN_DUPLICATE, ref token); // Get impersonation token

            if (retVal)
            {
                var securityAttributes = new SECURITY_ATTRIBUTES();
                securityAttributes.nLength = (uint)Marshal.SizeOf(securityAttributes);

                // Convert the impersonation token into Primary token
                retVal = DuplicateTokenEx(
                    token,
                    TOKEN_ASSIGN_PRIMARY | TOKEN_DUPLICATE | TOKEN_QUERY,
                    ref securityAttributes,
                    (int)SECURITY_IMPERSONATION_LEVEL.SecurityIdentification,
                    (int)TOKEN_TYPE.TokenPrimary,
                    ref primaryToken);

                // Close the Token that was previously opened.
                CloseHandle(token);
            }

            // We'll close this token after it is used.
            return primaryToken;
        }
    }
}


