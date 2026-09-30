using CommunityToolkit.Mvvm.ComponentModel;

namespace PCHealthDashboard.Models;

public sealed class ProcessEntry : ObservableObject
{
    private float _cpuPercent;
    private double _memoryMegabytes;

    public ProcessEntry(ProcessReading reading)
    {
        ProcessId = reading.ProcessId;
        Name = reading.Name;
        StartTimeUtcTicks = reading.StartTimeUtcTicks;
        CanTerminate = reading.CanTerminate;
        Update(reading);
    }

    public int ProcessId { get; }
    public string Name { get; }
    public long StartTimeUtcTicks { get; }
    public bool CanTerminate { get; }
    public string IdentityDisplay => $"{Name} · PID {ProcessId}";
    public string CpuDisplay => $"{CpuPercent:F1}%";
    public string MemoryDisplay => MemoryMegabytes >= 1024d
        ? $"{MemoryMegabytes / 1024d:F1} GB"
        : $"{MemoryMegabytes:F0} MB";
    public float CpuPercent => _cpuPercent;
    public double MemoryMegabytes => _memoryMegabytes;

    internal void Update(ProcessReading reading)
    {
        if (SetProperty(ref _cpuPercent, reading.CpuPercent, nameof(CpuPercent)))
            OnPropertyChanged(nameof(CpuDisplay));
        if (SetProperty(ref _memoryMegabytes, reading.MemoryMegabytes, nameof(MemoryMegabytes)))
            OnPropertyChanged(nameof(MemoryDisplay));
    }
}
