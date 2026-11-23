// miPDFconvert - virtual PDF printer
// Copyright (C) 2026 Wolfgang Mitterbucher (mitterbucher.com)
// Licensed under the Apache License, Version 2.0 (see LICENSE).
//
// miPDFport: print monitor with exactly one port, "miPDFconvert:". It writes each print job
// (the PostScript produced by the printer driver) to a file that only the printing user can
// read, and starts miPDFconvert.exe in that user's session to turn it into a PDF.
//
// Configuration (written by PrinterSetup):
//   HKLM\SYSTEM\CurrentControlSet\Control\Print\Monitors\miPDFconvert
//     Application (REG_SZ)  full path of miPDFconvert.exe

#include <windows.h>
#include <winspool.h>
#include <winsplp.h>
#include <wtsapi32.h>
#include <userenv.h>
#include <sddl.h>
#include <cstdarg>
#include <cstdio>
#include <string>
#include <vector>

namespace
{
    const wchar_t PORT_NAME[] = L"miPDFconvert:";
    const wchar_t MONITOR_NAME[] = L"miPDFconvert";
    const wchar_t PORT_DESCRIPTION[] = L"miPDFconvert PDF port";
    const wchar_t CONFIG_KEY[] = L"SYSTEM\\CurrentControlSet\\Control\\Print\\Monitors\\miPDFconvert";

    struct Port
    {
        HANDLE file = INVALID_HANDLE_VALUE;
        HANDLE printer = nullptr;
        HANDLE userToken = nullptr;   // primary token of the printing user's session
        DWORD jobId = 0;
        bool hasData = false;
        std::wstring path;
        std::wstring title;
    };

    // The spooler calls the port functions while impersonating the printing user. File
    // system work and process creation must run as the spooler itself (LocalSystem).
    class AsSpooler
    {
    public:
        AsSpooler() : client(RevertToPrinterSelf()) {}
        ~AsSpooler() { if (client) ImpersonatePrinterClient(client); }
        AsSpooler(const AsSpooler&) = delete;
        AsSpooler& operator=(const AsSpooler&) = delete;
    private:
        HANDLE client;
    };

    std::wstring DataDirectory()
    {
        wchar_t buffer[MAX_PATH];
        DWORD n = ExpandEnvironmentStringsW(L"%ProgramData%\\miPDFconvert", buffer, MAX_PATH);
        return (n > 0 && n <= MAX_PATH) ? buffer : L"C:\\ProgramData\\miPDFconvert";
    }

    void Log(const wchar_t* format, ...)
    {
        wchar_t line[1024];
        SYSTEMTIME t;
        GetLocalTime(&t);
        int n = swprintf_s(line, L"%04u-%02u-%02u %02u:%02u:%02u ", t.wYear, t.wMonth, t.wDay, t.wHour, t.wMinute, t.wSecond);
        va_list args;
        va_start(args, format);
        _vsnwprintf_s(line + n, _countof(line) - n, _TRUNCATE, format, args);
        va_end(args);

        char utf8[3200];
        int len = WideCharToMultiByte(CP_UTF8, 0, line, -1, utf8, sizeof(utf8) - 2, nullptr, nullptr);
        if (len <= 1)
            return;
        utf8[len - 1] = '\r';
        utf8[len] = '\n';

        std::wstring path = DataDirectory() + L"\\miPDFport.log";
        HANDLE h = CreateFileW(path.c_str(), FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (h == INVALID_HANDLE_VALUE)
            return;
        DWORD written;
        WriteFile(h, utf8, len + 1, &written, nullptr);
        CloseHandle(h);
    }

    std::wstring ApplicationPath()
    {
        wchar_t value[MAX_PATH];
        DWORD size = sizeof(value);
        if (RegGetValueW(HKEY_LOCAL_MACHINE, CONFIG_KEY, L"Application", RRF_RT_REG_SZ, nullptr, value, &size) != ERROR_SUCCESS)
            return L"";
        return value;
    }

    // Spool files of jobs whose conversion failed are never picked up again.
    void DeleteOldSpoolFiles(const std::wstring& directory)
    {
        WIN32_FIND_DATAW data;
        HANDLE find = FindFirstFileW((directory + L"\\*.ps").c_str(), &data);
        if (find == INVALID_HANDLE_VALUE)
            return;
        FILETIME now;
        GetSystemTimeAsFileTime(&now);
        ULONGLONG limit = (static_cast<ULONGLONG>(now.dwHighDateTime) << 32 | now.dwLowDateTime) - 864000000000ULL;  // 1 day
        do
        {
            ULONGLONG written = static_cast<ULONGLONG>(data.ftLastWriteTime.dwHighDateTime) << 32 | data.ftLastWriteTime.dwLowDateTime;
            if (written < limit)
                DeleteFileW((directory + L"\\" + data.cFileName).c_str());
        } while (FindNextFileW(find, &data));
        FindClose(find);
    }

    // Session of the printing user, taken from the impersonation token of the calling thread.
    DWORD ClientSession()
    {
        HANDLE token;
        if (!OpenThreadToken(GetCurrentThread(), TOKEN_QUERY, TRUE, &token))
            return 0;
        DWORD session = 0, size;
        if (!GetTokenInformation(token, TokenSessionId, &session, sizeof(session), &size))
            session = 0;
        CloseHandle(token);
        return session;
    }

    std::wstring UserSid(HANDLE token)
    {
        BYTE buffer[SECURITY_MAX_SID_SIZE + sizeof(TOKEN_USER)];
        DWORD size;
        if (!GetTokenInformation(token, TokenUser, buffer, sizeof(buffer), &size))
            return L"";
        LPWSTR sid;
        if (!ConvertSidToStringSidW(reinterpret_cast<TOKEN_USER*>(buffer)->User.Sid, &sid))
            return L"";
        std::wstring result = sid;
        LocalFree(sid);
        return result;
    }

    // Creates the spool file readable only by the user (plus SYSTEM and administrators).
    HANDLE CreateSpoolFile(const std::wstring& path, const std::wstring& userSid)
    {
        std::wstring sddl = L"D:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FA;;;" + userSid + L")";
        PSECURITY_DESCRIPTOR descriptor;
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl.c_str(), SDDL_REVISION_1, &descriptor, nullptr))
            return INVALID_HANDLE_VALUE;
        SECURITY_ATTRIBUTES attributes = { sizeof(attributes), descriptor, FALSE };
        HANDLE file = CreateFileW(path.c_str(), GENERIC_WRITE, 0, &attributes, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        LocalFree(descriptor);
        return file;
    }

    void Launch(Port& port)
    {
        std::wstring application = ApplicationPath();
        if (application.empty())
        {
            Log(L"Job %lu: application path not configured.", port.jobId);
            DeleteFileW(port.path.c_str());
            return;
        }

        // The title is one quoted argument: no quotes inside, no trailing backslash.
        std::wstring title = port.title;
        for (wchar_t& c : title)
            if (c == L'"')
                c = L'\'';
        while (!title.empty() && title.back() == L'\\')
            title.pop_back();

        std::wstring commandLine = L"\"" + application + L"\" -psfile \"" + port.path + L"\" -title \"" + title + L"\"";
        std::vector<wchar_t> commandBuffer(commandLine.begin(), commandLine.end());
        commandBuffer.push_back(L'\0');
        std::wstring directory = application.substr(0, application.find_last_of(L'\\'));

        void* environment = nullptr;
        CreateEnvironmentBlock(&environment, port.userToken, FALSE);
        STARTUPINFOW startup = { sizeof(startup) };
        startup.lpDesktop = const_cast<LPWSTR>(L"winsta0\\default");
        PROCESS_INFORMATION process = {};
        if (CreateProcessAsUserW(port.userToken, application.c_str(), commandBuffer.data(), nullptr, nullptr, FALSE,
                                 CREATE_UNICODE_ENVIRONMENT, environment, directory.c_str(), &startup, &process))
        {
            Log(L"Job %lu: started miPDFconvert (process %lu).", port.jobId, process.dwProcessId);
            CloseHandle(process.hThread);
            CloseHandle(process.hProcess);
        }
        else
        {
            Log(L"Job %lu: CreateProcessAsUser failed (error %lu).", port.jobId, GetLastError());
            DeleteFileW(port.path.c_str());
        }
        if (environment)
            DestroyEnvironmentBlock(environment);
    }

    void CloseJob(Port& port)
    {
        if (port.file != INVALID_HANDLE_VALUE)
        {
            CloseHandle(port.file);
            port.file = INVALID_HANDLE_VALUE;
        }
        if (port.printer)
        {
            ClosePrinter(port.printer);
            port.printer = nullptr;
        }
        if (port.userToken)
        {
            CloseHandle(port.userToken);
            port.userToken = nullptr;
        }
    }

    // ---- port monitor functions --------------------------------------------------------------

    BOOL WINAPI MonEnumPorts(HANDLE, LPWSTR, DWORD level, LPBYTE ports, DWORD size, LPDWORD needed, LPDWORD returned)
    {
        *returned = 0;
        DWORD structSize = level == 1 ? sizeof(PORT_INFO_1W) : level == 2 ? sizeof(PORT_INFO_2W) : 0;
        if (structSize == 0)
        {
            SetLastError(ERROR_INVALID_LEVEL);
            return FALSE;
        }
        *needed = structSize + sizeof(PORT_NAME) + (level == 2 ? sizeof(MONITOR_NAME) + sizeof(PORT_DESCRIPTION) : 0);
        if (ports == nullptr || size < *needed)
        {
            SetLastError(ERROR_INSUFFICIENT_BUFFER);
            return FALSE;
        }

        LPBYTE strings = ports + structSize;
        auto put = [&strings](const wchar_t* text, size_t bytes)
        {
            memcpy(strings, text, bytes);
            LPWSTR result = reinterpret_cast<LPWSTR>(strings);
            strings += bytes;
            return result;
        };
        if (level == 1)
        {
            reinterpret_cast<PORT_INFO_1W*>(ports)->pName = put(PORT_NAME, sizeof(PORT_NAME));
        }
        else
        {
            auto info = reinterpret_cast<PORT_INFO_2W*>(ports);
            info->pPortName = put(PORT_NAME, sizeof(PORT_NAME));
            info->pMonitorName = put(MONITOR_NAME, sizeof(MONITOR_NAME));
            info->pDescription = put(PORT_DESCRIPTION, sizeof(PORT_DESCRIPTION));
            info->fPortType = PORT_TYPE_WRITE;
            info->Reserved = 0;
        }
        *returned = 1;
        return TRUE;
    }

    BOOL WINAPI MonOpenPort(HANDLE, LPWSTR name, PHANDLE handle)
    {
        if (name == nullptr || _wcsicmp(name, PORT_NAME) != 0)
        {
            SetLastError(ERROR_INVALID_PARAMETER);
            return FALSE;
        }
        *handle = new Port();
        return TRUE;
    }

    BOOL WINAPI MonStartDocPort(HANDLE handle, LPWSTR printerName, DWORD jobId, DWORD level, LPBYTE docInfo)
    {
        Port& port = *static_cast<Port*>(handle);
        DWORD session = ClientSession();

        AsSpooler asSpooler;
        CloseJob(port);
        port.jobId = jobId;
        port.hasData = false;
        port.title = (level == 1 && docInfo && reinterpret_cast<DOC_INFO_1W*>(docInfo)->pDocName)
            ? reinterpret_cast<DOC_INFO_1W*>(docInfo)->pDocName : L"";
        OpenPrinterW(printerName, &port.printer, nullptr);

        if (session == 0 || !WTSQueryUserToken(session, &port.userToken))
        {
            // No interactive user to show the PDF to (e.g. a print from a service or another
            // computer): the job is accepted and discarded.
            Log(L"Job %lu: no interactive session of the printing user (session %lu, error %lu) - job discarded.", jobId, session, GetLastError());
            port.userToken = nullptr;
            return TRUE;
        }

        std::wstring directory = DataDirectory();
        CreateDirectoryW(directory.c_str(), nullptr);
        directory += L"\\Spool";
        CreateDirectoryW(directory.c_str(), nullptr);
        DeleteOldSpoolFiles(directory);

        wchar_t name[64];
        swprintf_s(name, L"\\job%lu_%llu.ps", jobId, GetTickCount64());
        port.path = directory + name;
        port.file = CreateSpoolFile(port.path, UserSid(port.userToken));
        if (port.file == INVALID_HANDLE_VALUE)
            Log(L"Job %lu: cannot create spool file %s (error %lu) - job discarded.", jobId, port.path.c_str(), GetLastError());
        return TRUE;
    }

    BOOL WINAPI MonWritePort(HANDLE handle, LPBYTE buffer, DWORD size, LPDWORD written)
    {
        Port& port = *static_cast<Port*>(handle);
        if (port.file == INVALID_HANDLE_VALUE)
        {
            *written = size;   // discarded job
            return TRUE;
        }
        if (!WriteFile(port.file, buffer, size, written, nullptr))
            return FALSE;
        port.hasData |= *written > 0;
        return TRUE;
    }

    // The spooler requires the following functions; the port has nothing to read back and
    // nothing to configure.
    BOOL WINAPI MonReadPort(HANDLE, LPBYTE, DWORD, LPDWORD read)
    {
        *read = 0;
        SetLastError(ERROR_NOT_SUPPORTED);
        return FALSE;
    }

    BOOL WINAPI MonXcvOpenPort(HANDLE, LPCWSTR, ACCESS_MASK, PHANDLE xcv)
    {
        static int dummy;
        *xcv = &dummy;
        return TRUE;
    }

    DWORD WINAPI MonXcvDataPort(HANDLE, LPCWSTR, PBYTE, DWORD, PBYTE, DWORD, PDWORD needed)
    {
        if (needed)
            *needed = 0;
        return ERROR_NOT_SUPPORTED;
    }

    BOOL WINAPI MonXcvClosePort(HANDLE)
    {
        return TRUE;
    }

    BOOL WINAPI MonEndDocPort(HANDLE handle)
    {
        Port& port = *static_cast<Port*>(handle);
        AsSpooler asSpooler;

        bool convert = port.file != INVALID_HANDLE_VALUE && port.hasData;
        if (port.file != INVALID_HANDLE_VALUE)
        {
            CloseHandle(port.file);
            port.file = INVALID_HANDLE_VALUE;
        }
        if (port.printer)
            SetJob(port.printer, port.jobId, 0, nullptr, JOB_CONTROL_SENT_TO_PRINTER);

        if (convert)
            Launch(port);
        else if (!port.path.empty())
            DeleteFileW(port.path.c_str());

        CloseJob(port);
        port.path.clear();
        return TRUE;
    }

    BOOL WINAPI MonClosePort(HANDLE handle)
    {
        Port* port = static_cast<Port*>(handle);
        {
            AsSpooler asSpooler;
            CloseJob(*port);
        }
        delete port;
        return TRUE;
    }

    VOID WINAPI MonShutdown(HANDLE)
    {
    }
}

extern "C" LPMONITOR2 WINAPI InitializePrintMonitor2(PMONITORINIT, PHANDLE monitorHandle)
{
    static MONITOR2 monitor = {};
    monitor.cbSize = sizeof(MONITOR2);
    monitor.pfnEnumPorts = MonEnumPorts;
    monitor.pfnOpenPort = MonOpenPort;
    monitor.pfnStartDocPort = MonStartDocPort;
    monitor.pfnWritePort = MonWritePort;
    monitor.pfnReadPort = MonReadPort;
    monitor.pfnEndDocPort = MonEndDocPort;
    monitor.pfnClosePort = MonClosePort;
    monitor.pfnXcvOpenPort = MonXcvOpenPort;
    monitor.pfnXcvDataPort = MonXcvDataPort;
    monitor.pfnXcvClosePort = MonXcvClosePort;
    monitor.pfnShutdown = MonShutdown;
    *monitorHandle = &monitor;
    return &monitor;
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
        DisableThreadLibraryCalls(instance);
    return TRUE;
}
