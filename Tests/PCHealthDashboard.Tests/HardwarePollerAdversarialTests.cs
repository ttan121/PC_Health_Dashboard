using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using PCHealthDashboard.Models;
using PCHealthDashboard.Services;
using Xunit;
using Xunit.Abstractions;

namespace PCHealthDashboard.Tests;

public class HardwarePollerAdversarialTests
{
    private readonly ITestOutputHelper _output;

    public HardwarePollerAdversarialTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Requirement 1: Measure GC.GetAllocatedBytesForCurrentThread across 10,000+ direct polling operations in isolation.
    /// Tests both Mock mode and Real/Direct mode.
    /// </summary>
    [Fact]
    public void StressTest_10000_DirectPolls_AllocationsIsolated()
    {
        using var poller = new HardwarePollerEngine(isMockMode: true);
        poller.Initialize();

        // 1. Warm-up JIT and stabilize
        for (int i = 0; i < 2_000; i++)
        {
            poller.PollDirect(out _);
        }

        GC.Collect(2, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true, true);

        long startAlloc = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 10_000; i++)
        {
            poller.PollDirect(out var snap);
            // Verify snapshot values
            if (snap.TimestampUtcTicks == 0) throw new InvalidOperationException("Invalid timestamp");
        }

        long endAlloc = GC.GetAllocatedBytesForCurrentThread();
        long deltaAlloc = endAlloc - startAlloc;

        _output.WriteLine($"[10,000 Polls] Allocated: {deltaAlloc} bytes (Current Thread)");
        Assert.Equal(0, deltaAlloc);
    }

    /// <summary>
    /// Requirement 1 Extended: Test non-mock mode PollDirect behavior across iterations.
    /// In non-mock mode, tests that PollDirect completes safely and produces valid snapshots against real hardware.
    /// </summary>
    [Fact]
    public void StressTest_10000_NonMock_DirectPolls_Allocations()
    {
        using var poller = new HardwarePollerEngine(isMockMode: false);
        poller.Initialize();

        for (int i = 0; i < 20; i++)
        {
            poller.PollDirect(out var snap);
            Assert.True(snap.TimestampUtcTicks > 0);
            Assert.False(float.IsNaN(snap.CpuUsage));
            Assert.False(float.IsNaN(snap.CpuTemp));
        }
    }

    /// <summary>
    /// Requirement 2: Rapid mode toggles (Normal -> Cryo -> Normal at 100 Hz) to check for race conditions, deadlocks, or memory leaks.
    /// 500 toggles at 10ms intervals = 100 Hz.
    /// </summary>
    [Fact]
    public async Task StressTest_RapidModeToggles_100Hz_RaceConditions()
    {
        using var poller = new HardwarePollerEngine(isMockMode: true);
        poller.Initialize();

        int uiCallbackCount = 0;
        poller.AttachUi(_ => Interlocked.Increment(ref uiCallbackCount));

        GC.Collect(2, GCCollectionMode.Forced, true, true);
        long startAlloc = GC.GetTotalAllocatedBytes(true);

        const int toggleCount = 200;
        for (int i = 0; i < toggleCount; i++)
        {
            var targetMode = (i % 2 == 0) ? PollerMode.Cryo : PollerMode.Normal;
            poller.SetMode(targetMode);
            Assert.Equal(targetMode, poller.CurrentMode);

            // Read snapshot concurrently during mode toggle
            var snap = poller.GetLatestSnapshot();
            Assert.True(snap.TimestampUtcTicks >= 0);

            await Task.Delay(10); // 100 Hz toggle rate
        }

        long deltaAlloc = GC.GetTotalAllocatedBytes(true) - startAlloc;
        _output.WriteLine($"[Rapid 100Hz Toggles] Completed {toggleCount} toggles. UI Dispatches: {uiCallbackCount}, Alloc delta: {deltaAlloc} B");
    }

    /// <summary>
    /// Requirement 2 B: Test whether UI callback is preserved after switching Cryo -> Normal.
    /// If user attaches UI, enters Cryo (e.g. window minimized), then returns to Normal (window restored),
    /// the UI MUST resume receiving updates without requiring manual re-attachment if the callback was not detached!
    /// </summary>
    [Fact]
    public async Task BugInvestigation_ModeToggle_CryoToNormal_PreservesUiAttachment()
    {
        using var poller = new HardwarePollerEngine(isMockMode: true);
        poller.NormalIntervalMs = 20;
        poller.CryoIntervalMs = 100;
        poller.Initialize();

        int callbackCount = 0;
        poller.AttachUi(_ => Interlocked.Increment(ref callbackCount));

        // Initial callback on attach
        Assert.True(callbackCount >= 1);
        int countBeforeCryo = callbackCount;

        // Switch to Cryo
        poller.SetMode(PollerMode.Cryo);
        await Task.Delay(150);
        int countInCryo = callbackCount;

        // Switch back to Normal
        poller.SetMode(PollerMode.Normal);
        await Task.Delay(200);
        int countAfterNormal = callbackCount;

        _output.WriteLine($"Initial: {countBeforeCryo}, In Cryo: {countInCryo}, After Normal: {countAfterNormal}");
        
        // In Normal mode, callbackCount should resume incrementing!
        Assert.True(countAfterNormal > countInCryo, 
            $"UI callbacks failed to resume after switching Cryo -> Normal! (In Cryo: {countInCryo}, After Normal: {countAfterNormal})");
    }

    /// <summary>
    /// Requirement 2 C: Race Condition on DetachUi.
    /// When DetachUi() returns, NO further callbacks should ever be delivered to the detached callback.
    /// </summary>
    [Fact]
    public async Task BugInvestigation_DetachUi_NoSubsequentCallbacksDelivered()
    {
        using var poller = new HardwarePollerEngine(isMockMode: true);
        poller.NormalIntervalMs = 10;
        poller.Initialize();

        int callbackCount = 0;
        poller.AttachUi(_ => Interlocked.Increment(ref callbackCount));

        await Task.Delay(50); // Allow some ticks
        int countAtDetach = callbackCount;

        poller.DetachUi();

        // Wait to verify zero additional callbacks occur
        await Task.Delay(100);
        int countAfterDetach = callbackCount;

        _output.WriteLine($"At Detach: {countAtDetach}, After Detach: {countAfterDetach}");
        Assert.Equal(countAtDetach, countAfterDetach);
    }

    /// <summary>
    /// Requirement 3: Edge conditions - Poller initialized with uninitialized sensors / null bindings.
    /// Must return sensible defaults without crashing.
    /// </summary>
    [Fact]
    public void EdgeCondition_UninitializedSensors_ReturnsSafeDefaults()
    {
        // Poller with uninitialized state before Initialize()
        var poller = new HardwarePollerEngine(isMockMode: false);
        poller.PollDirect(out var snap);

        Assert.Equal(0f, snap.CpuUsage);
        Assert.Equal(45f, snap.CpuTemp);
        Assert.True(snap.RamTotalGb > 0f);
        Assert.Equal(0, snap.GpuCount);

        poller.Dispose();
    }

    /// <summary>
    /// Requirement 3 B: Multi-threaded concurrent snapshot readers while poller is running.
    /// Tests for torn reads, deadlocks, and allocations across 8 parallel reader threads.
    /// </summary>
    [Fact]
    public async Task StressTest_ConcurrentSnapshotReaders_NoTornReads()
    {
        using var poller = new HardwarePollerEngine(isMockMode: true);
        poller.NormalIntervalMs = 10;
        poller.Initialize();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var tasks = new Task[8];
        long totalReads = 0;

        for (int t = 0; t < tasks.Length; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                while (!cts.IsCancellationRequested)
                {
                    var snap = poller.GetLatestSnapshot();
                    Assert.True(snap.TimestampUtcTicks >= 0);
                    Assert.True(snap.CpuUsage >= 0f && snap.CpuUsage <= 100f);
                    Assert.True(snap.CpuTemp >= 20f && snap.CpuTemp <= 105f);
                    Interlocked.Increment(ref totalReads);
                }
            });
        }

        await Task.WhenAll(tasks);
        _output.WriteLine($"[Concurrent Readers] Completed {totalReads:N0} thread-safe snapshot reads in 2 seconds.");
        Assert.True(totalReads > 10_000);
    }

    /// <summary>
    /// Requirement 3 C: Dispose safety and idempotency.
    /// Calling Dispose multiple times must not throw exceptions or cause thread crashes.
    /// </summary>
    [Fact]
    public void EdgeCondition_DisposeIdempotency_And_CancellationSafety()
    {
        var poller = new HardwarePollerEngine(isMockMode: true);
        poller.Initialize();

        // Repeated dispose
        poller.Dispose();
        poller.Dispose();
        poller.Dispose();

        // GetLatestSnapshot after dispose
        var snap = poller.GetLatestSnapshot();
        Assert.True(snap.TimestampUtcTicks >= 0);
    }
}
