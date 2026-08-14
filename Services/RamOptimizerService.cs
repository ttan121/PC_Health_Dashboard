using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using PCHealthDashboard.Models;

namespace PCHealthDashboard.Services;

public class RamOptimizerService
{
    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private readonly HashSet<string> _blacklistedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "svchost", "dwm", "csrss", "smss", "lsass", "services", "wininit", 
        "winlogon", "explorer", "spoolsv", "taskmgr", "MsMpEng", "NisSrv", "SecurityHealthService",
        "smartscreen", "SearchIndexer", "sihost", "fontdrvhost", "WUDFHost", "WmiPrvSE",
        "PCHealthDashboard" // Self
    };

    public Task<List<ProcessItem>> GetEligibleProcessesAsync()
    {
        return Task.Run(() =>
        {
            var list = new List<ProcessItem>();
            var currentPid = Process.GetCurrentProcess().Id;
            var foregroundPid = GetForegroundProcessId();

            var processes = Process.GetProcesses();
            foreach (var p in processes)
            {
                try
                {
                    if (p.Id == currentPid || p.Id == foregroundPid) continue;
                    if (_blacklistedProcesses.Contains(p.ProcessName)) continue;
                    if (p.SessionId == 0) continue; // Skip session 0 (system services usually)
                    
                    // Filter out very small processes to avoid clutter (< 20MB)
                    if (p.WorkingSet64 < 20 * 1024 * 1024) continue;

                    // Ensure we can access it (throws if protected/access denied)
                    var handle = p.Handle;

                    list.Add(new ProcessItem
                    {
                        Pid = p.Id,
                        ProcessName = p.ProcessName,
                        WorkingSet64 = p.WorkingSet64,
                        IsSelected = false
                    });
                }
                catch
                {
                    // Access denied, exited, or protected - skip safely
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

                // Re-validate safety immediately before execution
                var currentPid = Process.GetCurrentProcess().Id;
                var foregroundPid = GetForegroundProcessId();

                if (p.Id == currentPid || p.Id == foregroundPid || _blacklistedProcesses.Contains(p.ProcessName) || p.SessionId == 0)
                {
                    result.Status = OptimizationStatus.NotEligible;
                    result.ErrorMessage = "Failed safety re-check.";
                    return result;
                }

                result.InitialWorkingSet = p.WorkingSet64;
                
                bool success = EmptyWorkingSet(p.Handle);
                
                if (success)
                {
                    // Allow time for OS to trim memory
                    System.Threading.Thread.Sleep(50);
                    p.Refresh();
                    result.FinalWorkingSet = p.WorkingSet64;
                    result.Status = OptimizationStatus.Optimized;
                }
                else
                {
                    result.Status = OptimizationStatus.Failed;
                    result.ErrorMessage = "EmptyWorkingSet returned false.";
                }
            }
            catch (ArgumentException)
            {
                result.Status = OptimizationStatus.ProcessExited;
                result.ErrorMessage = "Process has exited.";
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

    private uint GetForegroundProcessId()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return 0;
        GetWindowThreadProcessId(hwnd, out uint pid);
        return pid;
    }
}
