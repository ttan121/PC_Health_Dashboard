using System;

namespace PCHealthDashboard.Models;

/// <summary>
/// Detailed audit report generated after executing deep RAM optimization.
/// </summary>
public readonly record struct RamOptimizationReport(
    ulong InitialAvailPhysBytes,
    ulong FinalAvailPhysBytes,
    ulong FreedBytes,
    uint InitialMemoryLoadPct,
    uint FinalMemoryLoadPct,
    bool StandbyPurged,
    bool ModifiedFlushed,
    bool WorkingSetsTrimmed,
    string Details
)
{
    public double InitialAvailPhysMB => InitialAvailPhysBytes / 1024.0 / 1024.0;
    public double FinalAvailPhysMB => FinalAvailPhysBytes / 1024.0 / 1024.0;
    public double FreedMB => FreedBytes / 1024.0 / 1024.0;
    public int DeltaLoadPct => (int)InitialMemoryLoadPct - (int)FinalMemoryLoadPct;
}
