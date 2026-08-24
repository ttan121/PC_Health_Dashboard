using System;
using System.Runtime.InteropServices;

namespace PCHealthDashboard.Helpers;

/// <summary>
/// Native Windows Win32 and NT Kernel P/Invoke definitions for low-level memory management and shell operations.
/// </summary>
public static class NativeMethods
{
    // ==========================================
    // 1. NTDLL.DLL - Memory List & Kernel APIs
    // ==========================================
    
    /// <summary>
    /// SYSTEM_INFORMATION_CLASS for memory list operations (0x50 = 80).
    /// </summary>
    public const int SystemMemoryListInformation = 80;

    /// <summary>
    /// Commands supported by NtSetSystemInformation(SystemMemoryListInformation).
    /// </summary>
    public enum SystemMemoryListCommand
    {
        MemoryCaptureAccessedBits = 0,
        MemoryCaptureAndResetMySQL = 1,
        MemoryEmptyWorkingSets = 2,           // Flush system & process working sets
        MemoryFlushModifiedList = 3,          // Flush modified pages to disk
        MemoryPurgeStandbyList = 4,           // Purge ALL standby lists (Priorities 0-7, RAMMap equivalent)
        MemoryPurgeLowPriorityStandbyList = 5 // Purge Priority 0 standby list
    }

    /// <summary>
    /// Low-level NT system call to modify system information and memory manager page lists.
    /// </summary>
    [DllImport("ntdll.dll", SetLastError = false)]
    public static extern int NtSetSystemInformation(
        int systemInformationClass,
        ref int systemInformation,
        int systemInformationLength
    );

    // ==========================================
    // 2. ADVAPI32.DLL - Token Privileges
    // ==========================================
    
    public const uint TOKEN_QUERY = 0x0008;
    public const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    public const uint SE_PRIVILEGE_ENABLED = 0x00000002;

    public const string SE_PROFILE_SINGLE_PROCESS_NAME = "SeProfileSingleProcessPrivilege";
    public const string SE_INCREASE_QUOTA_NAME = "SeIncreaseQuotaPrivilege";

    [StructLayout(LayoutKind.Sequential)]
    public struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LUID_AND_ATTRIBUTES
    {
        public LUID Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID_AND_ATTRIBUTES Privilege;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool OpenProcessToken(
        IntPtr processHandle,
        uint desiredAccess,
        out IntPtr tokenHandle
    );

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern bool LookupPrivilegeValue(
        string? lpSystemName,
        string lpName,
        out LUID lpLuid
    );

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool AdjustTokenPrivileges(
        IntPtr tokenHandle,
        bool disableAllPrivileges,
        ref TOKEN_PRIVILEGES newState,
        uint bufferLength,
        IntPtr previousState,
        IntPtr returnLength
    );

    /// <summary>
    /// Safely enables a specified privilege on the current process token.
    /// </summary>
    public static bool EnablePrivilege(string privilegeName)
    {
        IntPtr hToken = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out hToken))
            {
                return false;
            }

            if (!LookupPrivilegeValue(null, privilegeName, out LUID luid))
            {
                return false;
            }

            TOKEN_PRIVILEGES tp = new()
            {
                PrivilegeCount = 1,
                Privilege = new LUID_AND_ATTRIBUTES
                {
                    Luid = luid,
                    Attributes = SE_PRIVILEGE_ENABLED
                }
            };

            return AdjustTokenPrivileges(hToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
        }
        catch
        {
            return false;
        }
        finally
        {
            if (hToken != IntPtr.Zero)
            {
                CloseHandle(hToken);
            }
        }
    }

    // ==========================================
    // 3. KERNEL32.DLL & PSAPI.DLL - Memory Status
    // ==========================================
    
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public class MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public MEMORYSTATUSEX()
        {
            dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetProcessWorkingSetSize(
        IntPtr hProcess,
        IntPtr dwMinimumWorkingSetSize,
        IntPtr dwMaximumWorkingSetSize
    );

    public const uint PROCESS_SET_QUOTA = 0x0100;
    public const uint PROCESS_QUERY_INFORMATION = 0x0400;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(
        uint dwDesiredAccess,
        bool bInheritHandle,
        int dwProcessId
    );

    [DllImport("psapi.dll", SetLastError = true)]
    public static extern bool EmptyWorkingSet(IntPtr hProcess);

    // ==========================================
    // 4. SHELL32.DLL - Recycle Bin & Shell
    // ==========================================
    
    public const uint SHERB_NOCONFIRMATION = 0x00000001;
    public const uint SHERB_NOPROGRESSUI  = 0x00000002;
    public const uint SHERB_NOSOUND       = 0x00000004;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int SHEmptyRecycleBin(
        IntPtr hwnd,
        string? pszRootPath,
        uint dwFlags
    );

    // ==========================================
    // 5. USER32.DLL - Active Window & Foreground Process
    // ==========================================
    
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
