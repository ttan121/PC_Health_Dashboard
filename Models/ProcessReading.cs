namespace PCHealthDashboard.Models;

public sealed record ProcessReading(
    int ProcessId,
    string Name,
    float CpuPercent,
    double MemoryMegabytes,
    long StartTimeUtcTicks,
    bool CanTerminate);
