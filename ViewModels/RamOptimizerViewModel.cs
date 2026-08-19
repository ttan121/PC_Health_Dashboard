using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCHealthDashboard.Models;
using PCHealthDashboard.Services;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace PCHealthDashboard.ViewModels;

public partial class RamOptimizerViewModel : ObservableObject
{
    private readonly RamOptimizerService _optimizerService;

    [ObservableProperty] private ObservableCollection<ProcessItem> _processes = new();
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isOptimizing;
    
    private bool _isAllSelected;
    public bool IsAllSelected
    {
        get => _isAllSelected;
        set
        {
            if (SetProperty(ref _isAllSelected, value))
            {
                foreach (var p in Processes)
                {
                    p.IsSelected = value;
                }
            }
        }
    }

    [ObservableProperty] private string _statusMessage = "Loading processes...";
    [ObservableProperty] private string _resultSummary = string.Empty;

    public RamOptimizerViewModel()
    {
        _optimizerService = new RamOptimizerService();
        _ = LoadProcessesAsync();
    }

    [RelayCommand]
    private async Task LoadProcessesAsync()
    {
        if (IsLoading || IsOptimizing) return;
        IsLoading = true;
        StatusMessage = "Scanning memory for eligible processes...";
        ResultSummary = string.Empty;

        var list = await _optimizerService.GetEligibleProcessesAsync();
        
        Processes.Clear();
        foreach (var p in list)
        {
            Processes.Add(p);
        }

        StatusMessage = $"{Processes.Count} eligible processes found.";
        IsAllSelected = false; // reset selection state
        IsLoading = false;
    }

    [RelayCommand]
    private async Task OptimizeAsync()
    {
        var selected = Processes.Where(p => p.IsSelected).ToList();
        if (!selected.Any())
        {
            StatusMessage = "No processes selected.";
            return;
        }

        if (IsOptimizing || IsLoading) return;
        IsOptimizing = true;
        StatusMessage = "Optimizing working sets...";

        long totalInitial = 0;
        long totalFinal = 0;
        int successCount = 0;
        int skipCount = 0;

        foreach (var p in selected)
        {
            var result = await _optimizerService.OptimizeProcessAsync(p.Pid);
            
            if (result.Status == OptimizationStatus.Optimized)
            {
                successCount++;
                totalInitial += result.InitialWorkingSet;
                totalFinal += result.FinalWorkingSet;
            }
            else
            {
                skipCount++;
            }
        }

        long reduced = totalInitial - totalFinal;

        ResultSummary = $"Optimization completed\n\n{successCount} processes optimized" + 
            (skipCount > 0 ? $"\n{skipCount} skipped (e.g. process exited or access denied)" : "") +
            "\n\nWorking Set\n" +
            $"{totalInitial / 1048576.0:F1} MB → {totalFinal / 1048576.0:F1} MB\n" +
            $"−{reduced / 1048576.0:F1} MB\n\n" +
            "ⓘ This reduction is temporary.\nWindows may restore memory pages when needed.";

        if (reduced < 5 * 1024 * 1024 && successCount > 0)
        {
            ResultSummary = "Optimization completed\n\nNo significant working-set reduction was observed.";
        }

        StatusMessage = "Ready";
        IsOptimizing = false;
        
        // Reload list to show updated values
        await LoadProcessesAsync();
    }
}
