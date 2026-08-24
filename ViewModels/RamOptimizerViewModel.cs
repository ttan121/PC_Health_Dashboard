using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCHealthDashboard.Helpers;
using PCHealthDashboard.Models;
using PCHealthDashboard.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace PCHealthDashboard.ViewModels;

public partial class RamOptimizerViewModel : ObservableObject
{
    private readonly INativeMemoryService _memoryService;

    [ObservableProperty] private ObservableCollection<ProcessItem> _processes = new();
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isOptimizing;
    [ObservableProperty] private string _totalSelectedSize = "0 MB";
    
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
                UpdateTotalSelected();
            }
        }
    }

    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private string _resultSummary = string.Empty;

    public RamOptimizerViewModel() : this(new NativeMemoryService())
    {
    }

    public RamOptimizerViewModel(INativeMemoryService memoryService)
    {
        _memoryService = memoryService ?? throw new ArgumentNullException(nameof(memoryService));
        _ = LoadProcessesAsync();
    }

    public void UpdateTotalSelected()
    {
        double selectedMb = Processes.Where(p => p.IsSelected).Sum(p => p.WorkingSetMB);
        TotalSelectedSize = ByteSizeFormatter.FormatMb(selectedMb);

        bool allSelected = Processes.Count > 0 && Processes.All(p => p.IsSelected);
        if (_isAllSelected != allSelected)
        {
            _isAllSelected = allSelected;
            OnPropertyChanged(nameof(IsAllSelected));
        }
    }

    [RelayCommand]
    public async Task LoadProcessesAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusMessage = "Scanning memory for eligible background processes...";

        try
        {
            var list = await _memoryService.GetEligibleProcessesAsync();
            
            Processes.Clear();
            foreach (var p in list)
            {
                p.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(ProcessItem.IsSelected))
                    {
                        UpdateTotalSelected();
                    }
                };
                Processes.Add(p);
            }

            StatusMessage = $"{Processes.Count} eligible background processes found.";
            _isAllSelected = false;
            OnPropertyChanged(nameof(IsAllSelected));
            UpdateTotalSelected();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task DeepOptimizeAsync()
    {
        if (IsOptimizing) return;
        IsOptimizing = true;
        StatusMessage = "Executing Deep NT RAM Cleanup (RAMMap Standby Purge & Working Sets)...";

        try
        {
            RamOptimizationReport report = await Task.Run(() => _memoryService.OptimizeRamDeep());

            string standbyStatus = report.StandbyPurged ? "✔ Purged" : "Skipped/Restricted";
            string modifiedStatus = report.ModifiedFlushed ? "✔ Flushed" : "Skipped/Restricted";
            string wsStatus = report.WorkingSetsTrimmed ? "✔ Trimmed" : "Skipped";

            ResultSummary = $"Deep RAM Optimization Complete\n\n" +
                            $"Available Physical RAM:\n" +
                            $"{report.InitialAvailPhysMB:N0} MB → {report.FinalAvailPhysMB:N0} MB (+{report.FreedMB:N0} MB Freed)\n\n" +
                            $"System Memory Load:\n" +
                            $"{report.InitialMemoryLoadPct}% → {report.FinalMemoryLoadPct}% (Δ −{report.DeltaLoadPct}%)\n\n" +
                            $"NT Kernel Operations:\n" +
                            $"• Standby List (Priority 0-7): {standbyStatus}\n" +
                            $"• Modified List: {modifiedStatus}\n" +
                            $"• Working Sets: {wsStatus}\n\n" +
                            $"ⓘ {report.Details}";

            StatusMessage = $"Deep optimization completed. Freed {report.FreedMB:N0} MB.";
        }
        finally
        {
            IsOptimizing = false;
        }

        await LoadProcessesAsync();
    }

    [RelayCommand]
    public async Task OptimizeAsync()
    {
        var selected = Processes.Where(p => p.IsSelected).ToList();
        if (!selected.Any())
        {
            // If no individual processes selected, execute full deep optimization!
            await DeepOptimizeAsync();
            return;
        }

        if (IsOptimizing) return;
        IsOptimizing = true;
        StatusMessage = $"Trimming working sets for {selected.Count} processes...";

        try
        {
            long totalInitial = 0;
            long totalFinal = 0;
            int successCount = 0;
            int skipCount = 0;

            foreach (var p in selected)
            {
                var result = await _memoryService.OptimizeProcessAsync(p.Pid);
                
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

            ResultSummary = $"Process Working Set Trim Complete\n\n" +
                            $"{successCount} processes optimized" + 
                            (skipCount > 0 ? $", {skipCount} skipped (active/protected)" : "") +
                            "\n\nWorking Set Total:\n" +
                            $"{totalInitial / 1048576.0:F1} MB → {totalFinal / 1048576.0:F1} MB\n" +
                            $"−{reduced / 1048576.0:F1} MB\n\n" +
                            "ⓘ Pages were released to the Standby pool. Use Deep Clean to purge Standby memory.";

            if (reduced < 5 * 1024 * 1024 && successCount > 0)
            {
                ResultSummary = "Optimization completed.\nWorking sets were already minimized.";
            }

            StatusMessage = "Ready";
        }
        finally
        {
            IsOptimizing = false;
        }
        
        await LoadProcessesAsync();
    }
}
