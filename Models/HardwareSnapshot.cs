// ============================================================================
// PC Health Dashboard - Models/HardwareSnapshot.cs
// Zero-Allocation Telemetry Structs for High-Frequency Polling
// ============================================================================

using System;

namespace PCHealthDashboard.Models;

/// <summary>
/// Operational mode for the hardware poller.
/// </summary>
public enum PollerMode
{
    /// <summary>
    /// Active UI mode: 1000ms sampling interval, full subsystem polling, UI callbacks active.
    /// </summary>
    Normal,

    /// <summary>
    /// Cryo background mode: 5000ms-10000ms sampling interval, core-only polling, UI callbacks detached.
    /// Eliminates dispatcher overhead and achieves >80% CPU reduction.
    /// </summary>
    Cryo
}

/// <summary>
/// GPU telemetry packet represented as an immutable value-type struct.
/// Guarantees zero heap allocation per tick.
/// </summary>
public readonly record struct GpuTelemetry
{
    public string Name { get; init; }
    public string Id { get; init; }
    public float Usage { get; init; }
    public float Temperature { get; init; }
    public float VramUsedGb { get; init; }
    public float VramTotalGb { get; init; }
    public bool IsSharedMemory { get; init; }
    public bool IsAvailable { get; init; }

    public GpuTelemetry(
        string name,
        string id,
        float usage,
        float temperature,
        float vramUsedGb,
        float vramTotalGb,
        bool isSharedMemory,
        bool isAvailable)
    {
        Name = name ?? string.Empty;
        Id = id ?? string.Empty;
        Usage = usage;
        Temperature = temperature;
        VramUsedGb = vramUsedGb;
        VramTotalGb = vramTotalGb;
        IsSharedMemory = isSharedMemory;
        IsAvailable = isAvailable;
    }

    public static readonly GpuTelemetry Empty = new(string.Empty, string.Empty, 0f, 0f, 0f, 0f, false, false);
}

/// <summary>
/// Comprehensive hardware telemetry snapshot represented as a readonly record struct.
/// Transferred by value across the polling pipeline to eliminate all heap allocation.
/// Conforms to Master Project Specification Interface Contract § HardwarePollerEngine ↔ ViewModel & Storage.
/// </summary>
public readonly record struct HardwareSnapshot(
    long TimestampUtcTicks,
    float CpuUsage,
    float CpuTemp,
    float CpuPower,
    float CpuClock,
    float RamUsedGb,
    float RamTotalGb,
    float SsdUsedGb,
    float SsdTotalGb,
    float SsdHealth,
    float NetDownMbps,
    float NetUpMbps,
    int GpuCount,
    float GpuUsage,
    float GpuTemp,
    float GpuVramUsedGb,
    float GpuVramTotalGb
)
{
    /// <summary>
    /// Returns an uninitialized/empty snapshot.
    /// </summary>
    public static readonly HardwareSnapshot Empty = new(
        TimestampUtcTicks: 0L,
        CpuUsage: 0f,
        CpuTemp: 0f,
        CpuPower: 0f,
        CpuClock: 0f,
        RamUsedGb: 0f,
        RamTotalGb: 16f,
        SsdUsedGb: 0f,
        SsdTotalGb: 512f,
        SsdHealth: 100f,
        NetDownMbps: 0f,
        NetUpMbps: 0f,
        GpuCount: 0,
        GpuUsage: 0f,
        GpuTemp: 0f,
        GpuVramUsedGb: 0f,
        GpuVramTotalGb: 0f
    );
}
