using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCHealthDashboard.Models;
using PCHealthDashboard.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace PCHealthDashboard.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly HardwareMonitorService _hardwareMonitor;
    private readonly DispatcherTimer _timer;

    [ObservableProperty] private int _healthScore = 100;
    [ObservableProperty] private string _healthStatus = "Healthy";
    [ObservableProperty] private string _healthStatusColor = "#10b981"; // Healthy green
    public ObservableCollection<string> HealthIssues { get; } = new();
    public ObservableCollection<PCHealthDashboard.Models.AlertModel> SystemAlerts { get; } = new();
    [ObservableProperty] private bool _isPopupVisible;
    [ObservableProperty] private bool _isCompactMode;
    [ObservableProperty] private string _osdColor = "#f59e0b"; // Default to orange
    public event EventHandler? DataPolled;
    [ObservableProperty] private bool _isEfficiencyMode;
    
    // CPU
    [ObservableProperty] private float _cpuUsage;
    [ObservableProperty] private float _cpuTemp;
    
    // GPUs
    public ObservableCollection<GpuStatModel> Gpus { get; } = new();

    // RAM
    [ObservableProperty] private float _ramUsed;
    [ObservableProperty] private float _ramTotal;

    // Storage
    [ObservableProperty] private float _ssdUsedSpace;
    [ObservableProperty] private float _ssdTotalSpace;
    [ObservableProperty] private float _ssdHealth = 100f; // Mocked

    // Network
    [ObservableProperty] private float _downloadMbps;
    [ObservableProperty] private float _uploadMbps;
    public ObservableCollection<double> NetworkSpeedHistory { get; } = new();

    public MainViewModel()
    {
        _hardwareMonitor = new HardwareMonitorService();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += Timer_Tick;
        _timer.Start();

        // Initial update
        Timer_Tick(null, EventArgs.Empty);
    }

    private async void Timer_Tick(object? sender, EventArgs e)
    {
        (float usage, float temp, float power, float clock) cpu = default;
        System.Collections.Generic.List<GpuStatModel> gpuStats = null!;
        (float used, float total) ram = default;
        (float read, float write, float usedSpace, float totalSpace) storage = default;
        (float download, float upload) net = default;

        await System.Threading.Tasks.Task.Run(() =>
        {
            _hardwareMonitor.Update();
            cpu = _hardwareMonitor.GetCpuStats();
            gpuStats = _hardwareMonitor.GetGpusStats();
            ram = _hardwareMonitor.GetRamStats();
            storage = _hardwareMonitor.GetStorageStats();
            net = _hardwareMonitor.GetNetworkStats();
        });

        CpuUsage = cpu.usage;
        CpuTemp = cpu.temp;

        foreach (var stat in gpuStats)
        {
            // Sync onboard GPU temperature to CPU
            if (stat.IsSharedMemory || stat.Name.Contains("Intel") || stat.Name.Contains("Radeon Graphics"))
            {
                stat.Temperature = cpu.temp;
            }

            var existing = Gpus.FirstOrDefault(g => g.Id == stat.Id);
            if (existing != null)
            {
                existing.Temperature = stat.Temperature;
                existing.Usage = stat.Usage;
                
                // Only trigger property changes if significant to reduce UI overhead
                if (Math.Abs(existing.VramUsed - stat.VramUsed) > 0.05f) existing.VramUsed = stat.VramUsed;
                
                existing.VramTotal = stat.VramTotal;
                existing.IsVramAvailable = stat.IsVramAvailable;
                existing.IsSharedMemory = stat.IsSharedMemory;
            }
            else
            {
                Gpus.Add(stat);
            }
        }
        
        var toRemove = Gpus.Where(g => !gpuStats.Any(s => s.Id == g.Id)).ToList();
        foreach (var r in toRemove) Gpus.Remove(r);

        RamUsed = ram.used;
        RamTotal = ram.total;

        SsdUsedSpace = storage.usedSpace;
        SsdTotalSpace = storage.totalSpace;

        DownloadMbps = net.download;
        UploadMbps = net.upload;

        // Sparkline history (max 30 points)
        NetworkSpeedHistory.Add(DownloadMbps + UploadMbps);
        if (NetworkSpeedHistory.Count > 30)
            NetworkSpeedHistory.RemoveAt(0);

        UpdateHealthScore();
        DataPolled?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateHealthScore()
    {
        int score = 100;
        var issues = new System.Collections.Generic.List<string>();

        if (CpuTemp > 85)
        {
            score -= 15;
            issues.Add("High CPU temperature\nCooling system may be inadequate or under heavy load.");
        }
        else if (CpuTemp > 75)
        {
            score -= 5;
        }

        foreach (var gpu in Gpus)
        {
            if (gpu.Temperature > 85)
            {
                score -= 15;
                issues.Add($"High GPU temperature\n{gpu.Name} reached {gpu.Temperature:F0}°C.");
            }
            else if (gpu.Temperature > 80)
            {
                score -= 5;
            }

            if (gpu.Usage > 95)
            {
                // Usage isn't strictly unhealthy, just load, but we can note it
            }
        }

        if (RamTotal > 0 && (RamUsed / RamTotal) > 0.9)
        {
            score -= 10;
            issues.Add("High memory usage\nMemory usage is above 90%. Available memory is becoming limited.\n92% Used Close unused applications.");
        }
        
        if (SsdTotalSpace > 0 && (SsdTotalSpace - SsdUsedSpace) < 10)
        {
            score -= 5;
            issues.Add("Low storage space\nLess than 10GB of free space remaining on primary drive.");
        }

        HealthScore = Math.Max(0, score);
        HealthIssues.Clear();
        SystemAlerts.Clear();

        foreach (var issue in issues) HealthIssues.Add(issue);

        if (issues.Count == 0)
        {
            SystemAlerts.Add(new PCHealthDashboard.Models.AlertModel
            {
                Title = "System Healthy",
                Description = "No issues detected.",
                Metric = "All OK",
                Recommendation = "",
                Severity = PCHealthDashboard.Models.AlertSeverity.Info
            });
        }
        else
        {
            foreach (var issue in issues)
            {
                var parts = issue.Split('\n');
                string title = parts.Length > 0 ? parts[0] : "Issue";
                string desc = parts.Length > 1 ? parts[1] : "";
                
                var severity = PCHealthDashboard.Models.AlertSeverity.Warning;
                if (title.Contains("temperature", StringComparison.OrdinalIgnoreCase)) severity = PCHealthDashboard.Models.AlertSeverity.Critical;

                SystemAlerts.Add(new PCHealthDashboard.Models.AlertModel
                {
                    Title = title,
                    Description = desc,
                    Severity = severity
                });
            }
        }

        if (score >= 90)
        {
            HealthStatus = "Healthy";
            HealthStatusColor = "#10b981"; // semantic green
        }
        else if (score >= 70)
        {
            HealthStatus = "Warning";
            HealthStatusColor = "#f59e0b"; // semantic yellow
        }
        else
        {
            HealthStatus = "Critical";
            HealthStatusColor = "#ef4444"; // semantic red
        }
    }
    
    [RelayCommand]
    private void OpenRamOptimizer()
    {
        var window = new RamOptimizerWindow
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    [RelayCommand]
    private void CleanJunk()
    {
        var window = new JunkCleanerWindow
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    [RelayCommand]
    private void ChangeOsdColor(string color)
    {
        OsdColor = color;
    }

    partial void OnIsEfficiencyModeChanged(bool value)
    {
        if (_timer != null)
        {
            _timer.Interval = value ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(1);
        }
    }
}


