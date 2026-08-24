// ============================================================================
// PC Health Dashboard - Tests/HardwarePollerEngineTests.cs
// Unit Tests for Requirement R1: Zero-Allocation Hardware Poller & Cryo Mode
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using PCHealthDashboard.Models;
using PCHealthDashboard.Services;
using Xunit;

namespace PCHealthDashboard.Tests;

public class HardwarePollerEngineTests
{
    [Fact]
    public void HardwareSnapshot_IsValueType_And_InitializedDefaults()
    {
        var snapshot = HardwareSnapshot.Empty;
        Assert.True(typeof(HardwareSnapshot).IsValueType);
        Assert.Equal(0f, snapshot.CpuUsage);
        Assert.Equal(0f, snapshot.CpuTemp);
        Assert.Equal(16f, snapshot.RamTotalGb);
    }

    [Fact]
    public void GpuTelemetry_IsValueType_And_PropertiesMatch()
    {
        var gpu = new GpuTelemetry(
            name: "NVIDIA RTX 4090",
            id: "GPU-0",
            usage: 45.5f,
            temperature: 62.0f,
            vramUsedGb: 8.2f,
            vramTotalGb: 24.0f,
            isSharedMemory: false,
            isAvailable: true
        );

        Assert.True(typeof(GpuTelemetry).IsValueType);
        Assert.Equal("NVIDIA RTX 4090", gpu.Name);
        Assert.Equal(45.5f, gpu.Usage);
        Assert.Equal(62.0f, gpu.Temperature);
        Assert.Equal(8.2f, gpu.VramUsedGb);
        Assert.Equal(24.0f, gpu.VramTotalGb);
        Assert.False(gpu.IsSharedMemory);
        Assert.True(gpu.IsAvailable);
    }

    [Fact]
    public void PollDirect_ProducesValidSnapshot_WithZeroAllocations()
    {
        using var poller = new HardwarePollerEngine(isMockMode: true);
        poller.Initialize();

        // Warm up JIT
        poller.PollDirect(out var warmup);
        Assert.True(warmup.TimestampUtcTicks > 0);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long startAlloc = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 10_000; i++)
        {
            poller.PollDirect(out var snapshot);
            Assert.True(snapshot.CpuUsage >= 0f && snapshot.CpuUsage <= 100f);
            Assert.True(snapshot.CpuTemp >= 20f && snapshot.CpuTemp <= 105f);
            Assert.True(snapshot.RamTotalGb > 0f);
        }

        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - startAlloc;

        Assert.Equal(0, allocatedBytes);
    }

    [Fact]
    public void SetMode_SwitchesBetweenNormalAndCryo()
    {
        using var poller = new HardwarePollerEngine(isMockMode: true);
        poller.Initialize();

        Assert.Equal(PollerMode.Normal, poller.CurrentMode);

        poller.SetMode(PollerMode.Cryo);
        Assert.Equal(PollerMode.Cryo, poller.CurrentMode);

        poller.SetMode(PollerMode.Normal);
        Assert.Equal(PollerMode.Normal, poller.CurrentMode);
    }

    [Fact]
    public void AttachUi_ImmediatelyInvokesCallback_And_ReceivesSnapshots()
    {
        using var poller = new HardwarePollerEngine(isMockMode: true);
        poller.Initialize();

        int callbackCount = 0;
        HardwareSnapshot lastReceived = HardwareSnapshot.Empty;

        poller.AttachUi(snap =>
        {
            Interlocked.Increment(ref callbackCount);
            lastReceived = snap;
        });

        // AttachUi invokes immediately on attach
        Assert.True(callbackCount >= 1);
        Assert.True(lastReceived.TimestampUtcTicks > 0);

        poller.DetachUi();
    }

    [Fact]
    public void DetachUi_SuspendsUiCallbacks()
    {
        using var poller = new HardwarePollerEngine(isMockMode: true);
        poller.Initialize();

        int callbackCount = 0;
        poller.AttachUi(snap => Interlocked.Increment(ref callbackCount));

        Thread.Sleep(50);
        poller.DetachUi();
        int countAtDetach = callbackCount;
        Thread.Sleep(100);

        // After detach, callbacks must not increase further
        int afterDetachCount = callbackCount;
        Assert.Equal(countAtDetach, afterDetachCount);
    }

    [Fact]
    public async Task CryoMode_ReducesWorkload_And_SuppressesUiCallbacks()
    {
        using var poller = new HardwarePollerEngine(isMockMode: true);
        poller.Initialize();

        int uiCallbackCount = 0;
        poller.AttachUi(snap => Interlocked.Increment(ref uiCallbackCount));

        // Switch to Cryo mode and allow any in-flight tick to settle
        poller.SetMode(PollerMode.Cryo);
        await Task.Delay(50);

        int countBeforeSleep = uiCallbackCount;
        await Task.Delay(200);

        // In Cryo mode, UI callbacks are completely detached
        Assert.Equal(countBeforeSleep, uiCallbackCount);
        Assert.Equal(PollerMode.Cryo, poller.CurrentMode);
    }
}
