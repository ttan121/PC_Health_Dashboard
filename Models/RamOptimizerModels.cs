using CommunityToolkit.Mvvm.ComponentModel;

namespace PCHealthDashboard.Models;

public enum OptimizationStatus
{
    Optimized,
    Skipped,
    AccessDenied,
    ProcessExited,
    NotEligible,
    Failed
}

public class OptimizationResult
{
    public string ProcessName { get; set; } = string.Empty;
    public int Pid { get; set; }
    public long InitialWorkingSet { get; set; }
    public long FinalWorkingSet { get; set; }
    public long WorkingSetReduction => InitialWorkingSet - FinalWorkingSet;
    public OptimizationStatus Status { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

public partial class ProcessItem : ObservableObject
{
    [ObservableProperty] private int _pid;
    [ObservableProperty] private string _processName = string.Empty;
    [ObservableProperty] private long _workingSet64;
    [ObservableProperty] private bool _isSelected;
}
