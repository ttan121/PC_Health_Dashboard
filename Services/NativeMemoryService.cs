using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using PCHealthDashboard.Helpers;
using PCHealthDashboard.Models;

namespace PCHealthDashboard.Services;

/// <summary>
/// Service interface for deep NT kernel RAM optimization and working set management.
/// </summary>
public interface INativeMemoryService
{
    /// <summary>
    /// Executes deep RAM optimization equivalent to Sysinternals RAMMap (Empty Standby List, Flush Modified List, Trim Working Sets).
    /// </summary>
    RamOptimizationReport OptimizeRamDeep();

    /// <summary>
    /// Enumerates candidate background processes eligible for safe working set trimming.
    /// </summary>
    Task<List<ProcessItem>> GetEligibleProcessesAsync();

    /// <summary>
    /// Trims the private working set of a specific process.
    /// </summary>
    Task<OptimizationResult> OptimizeProcessAsync(int pid);
}

/// <summary>
/// High-performance NT kernel memory optimizer utilizing NtSetSystemInformation and AdjustTokenPrivileges.
/// </summary>
public class NativeMemoryService : INativeMemoryService
{
    private static readonly HashSet<string> BlacklistedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "svchost", "dwm", "csrss", "smss", "lsass", "services", "wininit", 
        "winlogon", "explorer", "spoolsv", "taskmgr", "MsMpEng", "NisSrv", "SecurityHealthService",
        "smartscreen", "SearchIndexer", "sihost", "fontdrvhost", "WUDFHost", "WmiPrvSE",
        "audiodg", "Registry", "Memory Compression", "PCHealthDashboard"
    };

    public RamOptimizationReport OptimizeRamDeep()
    {
        // 1. Elevate process token privileges
        bool hasProfilePrivilege = NativeMethods.EnablePrivilege(NativeMethods.SE_PROFILE_SINGLE_PROCESS_NAME);
        bool hasQuotaPrivilege = NativeMethods.EnablePrivilege(NativeMethods.SE_INCREASE_QUOTA_NAME);

        // 2. Capture baseline memory metrics
        NativeMethods.MEMORYSTATUSEX memBefore = new();
        NativeMethods.GlobalMemoryStatusEx(memBefore);

        bool standbyPurged = false;
        bool modifiedFlushed = false;
        bool workingSetsTrimmed = false;
        var logDetails = new List<string>();

        // 3. Flush Modified Page List to disk (moves dirty pages to standby)
        int cmdFlushModified = (int)NativeMethods.SystemMemoryListCommand.MemoryFlushModifiedList;
        int ntStatusFlush = NativeMethods.NtSetSystemInformation(
            NativeMethods.SystemMemoryListInformation,
            ref cmdFlushModified,
            sizeof(int)
        );
        if (ntStatusFlush == 0)
        {
            modifiedFlushed = true;
            logDetails.Add("Flushed modified page list to disk.");
        }
        else
        {
            logDetails.Add($"Modified page flush returned NTSTATUS: 0x{ntStatusFlush:X8}.");
        }

        // 4. Purge Standby Page List (RAMMap Deep Clean - Priorities 0 to 7)
        int cmdPurgeStandby = (int)NativeMethods.SystemMemoryListCommand.MemoryPurgeStandbyList;
        int ntStatusStandby = NativeMethods.NtSetSystemInformation(
            NativeMethods.SystemMemoryListInformation,
            ref cmdPurgeStandby,
            sizeof(int)
        );
        if (ntStatusStandby == 0)
        {
            standbyPurged = true;
            logDetails.Add("Purged all standby memory lists (Priorities 0-7).");
        }
        else
        {
            // Fallback to low-priority standby list if full purge is restricted
            int cmdPurgeLow = (int)NativeMethods.SystemMemoryListCommand.MemoryPurgeLowPriorityStandbyList;
            int ntStatusLow = NativeMethods.NtSetSystemInformation(
                NativeMethods.SystemMemoryListInformation,
                ref cmdPurgeLow,
                sizeof(int)
            );
            if (ntStatusLow == 0)
            {
                standbyPurged = true;
                logDetails.Add("Purged low-priority standby memory list (Priority 0).");
            }
            else
            {
                logDetails.Add($"Standby purge returned NTSTATUS: 0x{ntStatusStandby:X8}.");
            }
        }

        // 5. Trim System Working Sets via NT API
        int cmdEmptyWS = (int)NativeMethods.SystemMemoryListCommand.MemoryEmptyWorkingSets;
        int ntStatusWS = NativeMethods.NtSetSystemInformation(
            NativeMethods.SystemMemoryListInformation,
            ref cmdEmptyWS,
            sizeof(int)
        );
        if (ntStatusWS == 0)
        {
            workingSetsTrimmed = true;
            logDetails.Add("System working sets trimmed via NT kernel API.");
        }

        // 6. Trim Eligible User Process Working Sets
        int trimmedProcessCount = TrimEligibleProcessesWorkingSets();
        if (trimmedProcessCount > 0)
        {
            workingSetsTrimmed = true;
            logDetails.Add($"Trimmed working sets for {trimmedProcessCount} background processes.");
        }

        // 7. Allow NT Memory Manager page frame transitions to complete
        Thread.Sleep(60);

        // 8. Capture post-optimization memory metrics
        NativeMethods.MEMORYSTATUSEX memAfter = new();
        NativeMethods.GlobalMemoryStatusEx(memAfter);

        ulong freedBytes = memAfter.ullAvailPhys > memBefore.ullAvailPhys
            ? memAfter.ullAvailPhys - memBefore.ullAvailPhys
            : 0;

        string summary = string.Join(" ", logDetails);

        return new RamOptimizationReport(
            InitialAvailPhysBytes: memBefore.ullAvailPhys,
            FinalAvailPhysBytes: memAfter.ullAvailPhys,
            FreedBytes: freedBytes,
            InitialMemoryLoadPct: memBefore.dwMemoryLoad,
            FinalMemoryLoadPct: memAfter.dwMemoryLoad,
            StandbyPurged: standbyPurged,
            ModifiedFlushed: modifiedFlushed,
            WorkingSetsTrimmed: workingSetsTrimmed,
            Details: summary
        );
    }

    public Task<List<ProcessItem>> GetEligibleProcessesAsync()
    {
        return Task.Run(() =>
        {
            var list = new List<ProcessItem>();
            int currentPid = Environment.ProcessId;
            uint foregroundPid = GetForegroundProcessId();

            var processes = Process.GetProcesses();
            foreach (var p in processes)
            {
                try
                {
                    if (p.Id == currentPid || p.Id == foregroundPid) continue;
                    string name = p.ProcessName;
                    if (BlacklistedProcesses.Contains(name)) continue;
                    if (p.SessionId == 0) continue; // Skip session 0 system processes
                    
                    // Filter out processes under 15MB to avoid noise
                    long ws = p.WorkingSet64;
                    if (ws < 15 * 1024 * 1024) continue;

                    // Ensure handle accessibility with minimal rights without throwing AccessDenied
                    IntPtr hProcess = NativeMethods.OpenProcess(NativeMethods.PROCESS_SET_QUOTA | NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, p.Id);
                    if (hProcess == IntPtr.Zero) continue;
                    NativeMethods.CloseHandle(hProcess);

                    list.Add(new ProcessItem
                    {
                        Pid = p.Id,
                        ProcessName = name,
                        WorkingSet64 = ws,
                        IsSelected = false
                    });
                }
                catch
                {
                    // Access denied, protected, or exited
                }
                finally
                {
                    p.Dispose();
                }
            }

            return list.OrderByDescending(x => x.WorkingSet64).ToList();
        });
    }

    public async Task<OptimizationResult> OptimizeProcessAsync(int pid)
    {
        return await Task.Run(() =>
        {
            var result = new OptimizationResult { Pid = pid };
            try
            {
                using var p = Process.GetProcessById(pid);
                result.ProcessName = p.ProcessName;

                int currentPid = Environment.ProcessId;
                uint foregroundPid = GetForegroundProcessId();

                if (p.Id == currentPid || p.Id == foregroundPid || BlacklistedProcesses.Contains(p.ProcessName) || p.SessionId == 0)
                {
                    result.Status = OptimizationStatus.NotEligible;
                    result.ErrorMessage = "Safety check prevented trimming of active or critical process.";
                    return result;
                }

                result.InitialWorkingSet = p.WorkingSet64;
                
                bool success = false;
                IntPtr hProcess = NativeMethods.OpenProcess(NativeMethods.PROCESS_SET_QUOTA | NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (hProcess != IntPtr.Zero)
                {
                    try
                    {
                        success = NativeMethods.EmptyWorkingSet(hProcess);
                        if (!success)
                        {
                            success = NativeMethods.SetProcessWorkingSetSize(hProcess, (IntPtr)(-1), (IntPtr)(-1));
                        }
                    }
                    finally
                    {
                        NativeMethods.CloseHandle(hProcess);
                    }
                }
                else
                {
                    result.Status = OptimizationStatus.AccessDenied;
                    result.ErrorMessage = "Access denied when opening process handle.";
                    return result;
                }

                if (success)
                {
                    Thread.Sleep(40);
                    p.Refresh();
                    result.FinalWorkingSet = p.WorkingSet64;
                    result.Status = OptimizationStatus.Optimized;
                }
                else
                {
                    result.Status = OptimizationStatus.Failed;
                    result.ErrorMessage = "EmptyWorkingSet failed.";
                }
            }
            catch (ArgumentException)
            {
                result.Status = OptimizationStatus.ProcessExited;
                result.ErrorMessage = "Process has already exited.";
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                result.Status = OptimizationStatus.AccessDenied;
                result.ErrorMessage = ex.Message;
            }
            catch (Exception ex)
            {
                result.Status = OptimizationStatus.Failed;
                result.ErrorMessage = ex.Message;
            }

            return result;
        });
    }

    private int TrimEligibleProcessesWorkingSets()
    {
        int trimmed = 0;
        int currentPid = Environment.ProcessId;
        uint foregroundPid = GetForegroundProcessId();

        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch
        {
            return 0;
        }

        foreach (var p in processes)
        {
            try
            {
                if (p.Id == currentPid || p.Id == foregroundPid) continue;
                string name = p.ProcessName;
                if (BlacklistedProcesses.Contains(name)) continue;
                if (p.SessionId == 0) continue;
                long ws = p.WorkingSet64;
                if (ws < 15 * 1024 * 1024) continue;

                bool success = false;
                IntPtr hProcess = NativeMethods.OpenProcess(NativeMethods.PROCESS_SET_QUOTA | NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, p.Id);
                if (hProcess != IntPtr.Zero)
                {
                    try
                    {
                        success = NativeMethods.EmptyWorkingSet(hProcess);
                        if (!success)
                        {
                            success = NativeMethods.SetProcessWorkingSetSize(hProcess, (IntPtr)(-1), (IntPtr)(-1));
                        }
                    }
                    finally
                    {
                        NativeMethods.CloseHandle(hProcess);
                    }
                }

                if (success) trimmed++;
            }
            catch
            {
                // Skip safely
            }
            finally
            {
                p.Dispose();
            }
        }

        return trimmed;
    }

    private static uint GetForegroundProcessId()
    {
        try
        {
            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return 0;
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            return pid;
        }
        catch
        {
            return 0;
        }
    }
}
