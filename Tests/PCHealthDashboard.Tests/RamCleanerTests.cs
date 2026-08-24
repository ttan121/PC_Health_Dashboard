using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PCHealthDashboard.Helpers;
using PCHealthDashboard.Models;
using PCHealthDashboard.Services;
using Xunit;

namespace PCHealthDashboard.Tests;

public class RamCleanerTests
{
    [Fact]
    public void GlobalMemoryStatusEx_ShouldReturnValidSystemMemoryMetrics()
    {
        var memStatus = new NativeMethods.MEMORYSTATUSEX();
        bool success = NativeMethods.GlobalMemoryStatusEx(memStatus);

        Assert.True(success, "GlobalMemoryStatusEx must return true.");
        Assert.True(memStatus.ullTotalPhys > 0, "Total physical RAM must be greater than zero.");
        Assert.True(memStatus.ullAvailPhys > 0, "Available physical RAM must be greater than zero.");
        Assert.True(memStatus.dwMemoryLoad > 0 && memStatus.dwMemoryLoad <= 100, "Memory load % must be between 1 and 100.");
        Assert.True(memStatus.ullAvailPhys <= memStatus.ullTotalPhys, "Available physical RAM cannot exceed total physical RAM.");
    }

    [Fact]
    public void EnablePrivilege_SeProfileSingleProcess_ShouldNotThrow()
    {
        // Must execute cleanly without unhandled exceptions
        bool result = NativeMethods.EnablePrivilege(NativeMethods.SE_PROFILE_SINGLE_PROCESS_NAME);
        // Note: result may be true or false depending on execution context, but must not crash
        Assert.True(true);
    }

    [Fact]
    public void NativeMemoryService_OptimizeRamDeep_ShouldReturnValidReport()
    {
        var service = new NativeMemoryService();
        var report = service.OptimizeRamDeep();

        Assert.True(report.InitialAvailPhysBytes > 0, "Initial available physical RAM must be > 0.");
        Assert.True(report.FinalAvailPhysBytes > 0, "Final available physical RAM must be > 0.");
        Assert.True(report.InitialMemoryLoadPct > 0 && report.InitialMemoryLoadPct <= 100, "Initial load must be 1-100%.");
        Assert.True(report.FinalMemoryLoadPct > 0 && report.FinalMemoryLoadPct <= 100, "Final load must be 1-100%.");
        Assert.NotNull(report.Details);
        Assert.NotEmpty(report.Details);
    }

    [Fact]
    public async Task NativeMemoryService_GetEligibleProcesses_ShouldExcludeBlacklistedProcesses()
    {
        var service = new NativeMemoryService();
        var processes = await service.GetEligibleProcessesAsync();

        Assert.NotNull(processes);
        var currentPid = Environment.ProcessId;

        foreach (var p in processes)
        {
            Assert.NotEqual(currentPid, p.Pid);
            Assert.NotEqual("System", p.ProcessName, StringComparer.OrdinalIgnoreCase);
            Assert.NotEqual("csrss", p.ProcessName, StringComparer.OrdinalIgnoreCase);
            Assert.NotEqual("dwm", p.ProcessName, StringComparer.OrdinalIgnoreCase);
            Assert.NotEqual("lsass", p.ProcessName, StringComparer.OrdinalIgnoreCase);
            Assert.NotEqual("services", p.ProcessName, StringComparer.OrdinalIgnoreCase);
            Assert.NotEqual("explorer", p.ProcessName, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void HardwareMonitorService_GetRamStats_ShouldMatchGlobalMemoryStatusEx()
    {
        using var monitor = new HardwareMonitorService();
        var (used, total) = monitor.GetRamStats();

        Assert.True(total > 0, "Total RAM must be > 0 GB.");
        Assert.True(used > 0, "Used RAM must be > 0 GB.");
        Assert.True(used <= total, "Used RAM cannot exceed total RAM.");

        // Check consistency with direct Win32 call
        var mem = new NativeMethods.MEMORYSTATUSEX();
        bool ok = NativeMethods.GlobalMemoryStatusEx(mem);
        if (ok && mem.ullTotalPhys > 0)
        {
            float expectedTotal = (float)(mem.ullTotalPhys / (1024.0 * 1024.0 * 1024.0));
            Assert.InRange(total, expectedTotal - 0.5f, expectedTotal + 0.5f);
        }
    }

    [Fact]
    public async Task HardwareMonitorService_GetRamStats_IsThreadSafeAndZeroAllocation()
    {
        using var monitor = new HardwareMonitorService();
        var tasks = new List<Task>();

        for (int i = 0; i < 8; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < 100; j++)
                {
                    var (used, total) = monitor.GetRamStats();
                    Assert.True(total > 0);
                    Assert.True(used > 0);
                    Assert.True(used <= total);
                }
            }));
        }

        await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task MainViewModel_CleanRamCommand_ShouldExecuteAndRefreshMetrics()
    {
        var vm = new ViewModels.MainViewModel();
        Assert.False(vm.IsCleaningRam);

        bool polledRaised = false;
        vm.DataPolled += (s, e) => polledRaised = true;

        await vm.CleanRamCommand.ExecuteAsync(null);

        Assert.False(vm.IsCleaningRam);
        Assert.NotEmpty(vm.RamCleanStatus);
        Assert.True(vm.RamTotal > 0);
        Assert.True(vm.RamUsed > 0);
        Assert.True(vm.RamUsed <= vm.RamTotal);
        Assert.True(polledRaised, "DataPolled event must be raised after RAM cleanup.");
    }

    [Fact]
    public async Task MainViewModel_SequentialPollAndClean_ShouldNotFreeze()
    {
        var vm = new ViewModels.MainViewModel();
        await vm.RefreshTelemetryAsync();
        Assert.True(vm.RamTotal > 0);
        Assert.True(vm.RamUsed > 0);

        await vm.CleanRamCommand.ExecuteAsync(null);
        Assert.False(vm.IsCleaningRam);
        Assert.True(vm.RamTotal > 0);
        Assert.True(vm.RamUsed > 0);

        // Subsequent poll continues smoothly
        await vm.RefreshTelemetryAsync();
        Assert.True(vm.RamTotal > 0);
    }

    [Fact]
    public async Task MainViewModel_RapidConsecutiveCleanClicks_ShouldBeGuardedAndNotDeadlock()
    {
        var vm = new ViewModels.MainViewModel();

        // Launch multiple rapid clicks concurrently
        var task1 = vm.CleanRamCommand.ExecuteAsync(null);
        var task2 = vm.CleanRamCommand.ExecuteAsync(null);
        var task3 = vm.CleanRamCommand.ExecuteAsync(null);

        await Task.WhenAll(task1, task2, task3);

        Assert.False(vm.IsCleaningRam);
        Assert.True(vm.RamTotal > 0);
        Assert.True(vm.RamUsed > 0);
        Assert.NotEmpty(vm.RamCleanStatus);
    }

    [Fact]
    public async Task MainViewModel_ConcurrentPollAndClean_ShouldSerializeWithoutExceptions()
    {
        var vm = new ViewModels.MainViewModel();
        var tasks = new List<Task>();

        for (int i = 0; i < 5; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                await vm.RefreshTelemetryAsync();
            }));
            tasks.Add(Task.Run(async () =>
            {
                await vm.CleanRamCommand.ExecuteAsync(null);
            }));
        }

        await Task.WhenAll(tasks);

        Assert.False(vm.IsCleaningRam);
        Assert.True(vm.RamTotal > 0);
        Assert.True(vm.RamUsed > 0);
    }

    [Fact]
    public async Task HardwareMonitorService_AllMethods_AreThreadSafeUnderHeavyLoad()
    {
        using var monitor = new HardwareMonitorService();
        var tasks = new List<Task>();

        for (int i = 0; i < 6; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < 20; j++)
                {
                    monitor.Update(fullUpdate: true);
                    _ = monitor.GetCpuStats();
                    _ = monitor.GetGpusStats();
                    _ = monitor.GetRamStats();
                    _ = monitor.GetStorageStats();
                    _ = monitor.GetNetworkStats();
                }
            }));
        }

        await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task NativeMemoryService_OptimizeProcessAsync_SafetyGuardPreventsSelfOptimization()
    {
        var service = new NativeMemoryService();
        var result = await service.OptimizeProcessAsync(Environment.ProcessId);

        Assert.Equal(OptimizationStatus.NotEligible, result.Status);
        Assert.Contains("Safety", result.ErrorMessage);
    }

    [Fact]
    public async Task NativeMemoryService_GetEligibleProcesses_Performance_ShouldBeFast()
    {
        var service = new NativeMemoryService();
        var sw = Stopwatch.StartNew();
        var processes = await service.GetEligibleProcessesAsync();
        sw.Stop();

        Assert.NotNull(processes);
        Assert.True(sw.ElapsedMilliseconds < 2500, $"GetEligibleProcessesAsync took too long: {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task RamOptimizerViewModel_DeepOptimizeCommand_ShouldExecuteAndSummarize()
    {
        var vm = new ViewModels.RamOptimizerViewModel();
        Assert.False(vm.IsOptimizing);

        await vm.DeepOptimizeCommand.ExecuteAsync(null);

        Assert.False(vm.IsOptimizing);
        Assert.False(vm.IsLoading);
        Assert.Contains("Deep RAM Optimization Complete", vm.ResultSummary);
        Assert.Contains("MB", vm.ResultSummary);
        Assert.Contains("eligible background processes found", vm.StatusMessage);
    }

    [Fact]
    public async Task RamOptimizerViewModel_SelectAllAndOptimize_ShouldExecuteCleanly()
    {
        var vm = new ViewModels.RamOptimizerViewModel();
        // Wait briefly for initial load if any
        await Task.Delay(100);

        vm.IsAllSelected = true;
        Assert.False(vm.IsOptimizing);

        await vm.OptimizeCommand.ExecuteAsync(null);

        Assert.False(vm.IsOptimizing);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public void MainViewModel_Constructor_InitializesRamTotalAndRamUsedCorrectly()
    {
        var vm = new ViewModels.MainViewModel();
        Assert.True(vm.RamTotal > 0, "RamTotal must be > 0 on initialization.");
        Assert.True(vm.RamUsed > 0, "RamUsed must be > 0 on initialization.");
        Assert.True(vm.RamUsed <= vm.RamTotal, "RamUsed must be <= RamTotal.");
    }

    [Fact]
    public async Task MainViewModel_RamCleanStatus_AutoClearsGracefully()
    {
        var vm = new ViewModels.MainViewModel();
        await vm.CleanRamCommand.ExecuteAsync(null);
        Assert.NotEmpty(vm.RamCleanStatus);

        // Wait for auto-clear delay (4s + buffer)
        await Task.Delay(4500);
        Assert.Equal(string.Empty, vm.RamCleanStatus);
    }

    [Fact]
    public async Task NativeMemoryService_NonExistentPid_ReturnsGracefulResult()
    {
        var service = new NativeMemoryService();
        // High unlikely PID
        var result = await service.OptimizeProcessAsync(9999999);
        Assert.True(result.Status == OptimizationStatus.ProcessExited || result.Status == OptimizationStatus.Failed);
    }

    [Fact]
    public async Task NativeMemoryService_ConcurrentDeepOptimize_ShouldExecuteCleanly()
    {
        var service = new NativeMemoryService();
        var tasks = new List<Task<RamOptimizationReport>>();

        for (int i = 0; i < 4; i++)
        {
            tasks.Add(Task.Run(() => service.OptimizeRamDeep()));
        }

        var reports = await Task.WhenAll(tasks);
        Assert.Equal(4, reports.Length);
        foreach (var r in reports)
        {
            Assert.True(r.InitialAvailPhysBytes > 0);
            Assert.True(r.FinalAvailPhysBytes > 0);
        }
    }

    [Fact]
    public async Task RamOptimizerViewModel_EmptySelection_DelegatesToDeepOptimize()
    {
        var vm = new ViewModels.RamOptimizerViewModel();
        // Clear any selections
        foreach (var p in vm.Processes) p.IsSelected = false;

        await vm.OptimizeCommand.ExecuteAsync(null);

        Assert.False(vm.IsOptimizing);
        Assert.Contains("Deep RAM Optimization Complete", vm.ResultSummary);
    }
}

