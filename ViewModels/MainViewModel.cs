// ============================================================================
// PC Health Dashboard - ViewModels/MainViewModel.cs
// MVVM ViewModel with Asymmetric EWMA Health Engine & Zero-Disk-Wear RingBuffers
// ============================================================================

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCHealthDashboard.Helpers;
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
    private readonly IHealthScoreCalculator _healthCalculator;
    private readonly DispatcherTimer _timer;
    private readonly System.Threading.SemaphoreSlim _telemetrySemaphore = new(1, 1);
    private int _ramStatusVersion = 0;

    // Health Score & Status
    [ObservableProperty] private int _healthScore = 100;
    [ObservableProperty] private string _healthStatus = "Healthy";
    [ObservableProperty] private string _healthStatusColor = "#10b981"; // Healthy green
    [ObservableProperty] private float _thermalScore = 100f;
    [ObservableProperty] private float _loadScore = 100f;
    [ObservableProperty] private float _ramScore = 100f;
    [ObservableProperty] private float _storageScore = 100f;
    [ObservableProperty] private float _networkScore = 100f;

    public ObservableCollection<string> HealthIssues { get; } = new();
    public ObservableCollection<AlertModel> SystemAlerts { get; } = new();

    // Zero-Disk-Wear In-Memory Circular Buffers (60 seconds history)
    public RingBuffer<MetricPoint> CpuUsageHistory { get; } = new(60);
    public RingBuffer<MetricPoint> CpuTempHistory { get; } = new(60);
    public RingBuffer<MetricPoint> GpuUsageHistory { get; } = new(60);
    public RingBuffer<MetricPoint> GpuTempHistory { get; } = new(60);
    public RingBuffer<MetricPoint> RamUsageHistory { get; } = new(60);
    public RingBuffer<MetricPoint> NetSpeedHistory { get; } = new(60);
    public RingBuffer<MetricPoint> HealthScoreHistory { get; } = new(60);

    // UI Configuration & State
    [ObservableProperty] private bool _isPopupVisible;
    [ObservableProperty] private bool _isCompactMode;
    [ObservableProperty] private string _osdColor = "Orange"; // Default to orange
    public event EventHandler? DataPolled;
    [ObservableProperty] private bool _isEfficiencyMode;
    [ObservableProperty] private bool _isCleaningRam;
    [ObservableProperty] private string _ramCleanStatus = string.Empty;

    // CPU Telemetry
    [ObservableProperty] private float _cpuUsage;
    [ObservableProperty] private float _cpuTemp;
    [ObservableProperty] private float _cpuPower;
    [ObservableProperty] private float _cpuClock;

    // GPU Telemetry
    public ObservableCollection<GpuStatModel> Gpus { get; } = new();

    // RAM Telemetry
    [ObservableProperty] private float _ramUsed;
    [ObservableProperty] private float _ramTotal = 16f;

    // Storage Telemetry
    [ObservableProperty] private float _ssdUsedSpace;
    [ObservableProperty] private float _ssdTotalSpace;
    [ObservableProperty] private float _ssdHealth = 100f;

    // Network Telemetry
    [ObservableProperty] private float _downloadMbps;
    [ObservableProperty] private float _uploadMbps;
    [ObservableProperty] private int _pingLatency = 15;
    [ObservableProperty] private double _packetLoss = 0.0;
    public ObservableCollection<double> DownloadSpeedHistory { get; } = new();
    public ObservableCollection<double> UploadSpeedHistory { get; } = new();
    public ObservableCollection<double> NetworkSpeedHistory { get; } = new();

    public MainViewModel() : this(new HealthScoreCalculator())
    {
    }

    public MainViewModel(IHealthScoreCalculator healthCalculator)
    {
        _healthCalculator = healthCalculator ?? throw new ArgumentNullException(nameof(healthCalculator));
        _hardwareMonitor = new HardwareMonitorService();

        var initialRam = _hardwareMonitor.GetRamStats();
        if (initialRam.total > 0)
        {
            _ramTotal = initialRam.total;
            _ramUsed = initialRam.used;
        }

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
        // Try to acquire telemetry lock without waiting. If already in progress, drop this tick.
        if (!await _telemetrySemaphore.WaitAsync(0))
        {
            return;
        }

        try
        {
            await RefreshTelemetryCoreAsync();
        }
        catch
        {
            // Safeguard against unhandled background poll errors
        }
        finally
        {
            _telemetrySemaphore.Release();
        }
    }

    public async System.Threading.Tasks.Task RefreshTelemetryAsync()
    {
        await _telemetrySemaphore.WaitAsync();
        try
        {
            await RefreshTelemetryCoreAsync();
        }
        catch
        {
            // Safeguard against unhandled background poll errors
        }
        finally
        {
            _telemetrySemaphore.Release();
        }
    }

    private async System.Threading.Tasks.Task RefreshTelemetryCoreAsync()
    {
        (float usage, float temp, float power, float clock) cpu = default;
        System.Collections.Generic.List<GpuStatModel> gpuStats = null!;
        (float used, float total) ram = default;
        (float read, float write, float usedSpace, float totalSpace) storage = default;
        (float download, float upload) net = default;

        await System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                _hardwareMonitor.Update();
                cpu = _hardwareMonitor.GetCpuStats();
                gpuStats = _hardwareMonitor.GetGpusStats();
                ram = _hardwareMonitor.GetRamStats();
                storage = _hardwareMonitor.GetStorageStats();
                net = _hardwareMonitor.GetNetworkStats();
            }
            catch
            {
                // Prevent background polling failure from killing telemetry task
            }
        });

        CpuUsage = cpu.usage;
        CpuTemp = cpu.temp;
        CpuPower = cpu.power;
        CpuClock = cpu.clock;

        if (gpuStats != null)
        {
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
        }

        RamTotal = ram.total;
        RamUsed = ram.used;

        SsdUsedSpace = storage.usedSpace;
        SsdTotalSpace = storage.totalSpace;

        DownloadMbps = net.download;
        UploadMbps = net.upload;

        // Sparkline history (max 30 points for UI sparkline)
        DownloadSpeedHistory.Add(DownloadMbps);
        if (DownloadSpeedHistory.Count > 30)
            DownloadSpeedHistory.RemoveAt(0);

        UploadSpeedHistory.Add(UploadMbps);
        if (UploadSpeedHistory.Count > 30)
            UploadSpeedHistory.RemoveAt(0);

        NetworkSpeedHistory.Add(DownloadMbps + UploadMbps);
        if (NetworkSpeedHistory.Count > 30)
            NetworkSpeedHistory.RemoveAt(0);

        // Build Telemetry Snapshot
        var primaryGpu = Gpus.FirstOrDefault();
        long nowTicks = DateTime.UtcNow.Ticks;
        var snapshot = new HardwareSnapshot(
            TimestampUtcTicks: nowTicks,
            CpuUsage: CpuUsage,
            CpuTemp: CpuTemp,
            CpuPower: CpuPower,
            CpuClock: CpuClock,
            RamUsedGb: RamUsed,
            RamTotalGb: RamTotal,
            SsdUsedGb: SsdUsedSpace,
            SsdTotalGb: SsdTotalSpace,
            SsdHealth: SsdHealth,
            NetDownMbps: DownloadMbps,
            NetUpMbps: UploadMbps,
            GpuCount: Gpus.Count,
            GpuUsage: primaryGpu?.Usage ?? 0f,
            GpuTemp: primaryGpu?.Temperature ?? 0f,
            GpuVramUsedGb: primaryGpu?.VramUsed ?? 0f,
            GpuVramTotalGb: primaryGpu?.VramTotal ?? 0f
        );

        // Evaluate via Asymmetric EWMA Health Engine
        var evaluation = _healthCalculator.Evaluate(in snapshot);
        HealthScore = evaluation.Score;
        HealthStatus = evaluation.StatusBand;
        HealthStatusColor = evaluation.StatusColor;
        ThermalScore = evaluation.ThermalScore;
        LoadScore = evaluation.LoadScore;
        RamScore = evaluation.RamScore;
        StorageScore = evaluation.StorageScore;
        NetworkScore = evaluation.NetworkScore;

        // Push into Zero-Disk-Wear In-Memory RingBuffers
        CpuUsageHistory.Push(new MetricPoint(nowTicks, CpuUsage));
        CpuTempHistory.Push(new MetricPoint(nowTicks, CpuTemp));
        GpuUsageHistory.Push(new MetricPoint(nowTicks, snapshot.GpuUsage));
        GpuTempHistory.Push(new MetricPoint(nowTicks, snapshot.GpuTemp));
        RamUsageHistory.Push(new MetricPoint(nowTicks, RamUsed));
        NetSpeedHistory.Push(new MetricPoint(nowTicks, DownloadMbps + UploadMbps));
        HealthScoreHistory.Push(new MetricPoint(nowTicks, evaluation.Score));

        // Update Alerts and Issues
        HealthIssues.Clear();
        SystemAlerts.Clear();

        if (evaluation.ActiveAlerts.Count == 0)
        {
            SystemAlerts.Add(new AlertModel
            {
                Title = "System Healthy",
                Description = "All hardware metrics operating within optimal parameters.",
                Metric = "Optimal",
                Recommendation = "No action required.",
                Severity = AlertSeverity.Info
            });
        }
        else
        {
            foreach (var alert in evaluation.ActiveAlerts)
            {
                HealthIssues.Add(alert);

                var parts = alert.Split('\n');
                string title = parts.Length > 0 ? parts[0] : "Hardware Alert";
                string desc = parts.Length > 1 ? parts[1] : "";

                var severity = AlertSeverity.Warning;
                if (title.Contains("High CPU Temperature", StringComparison.OrdinalIgnoreCase) ||
                    title.Contains("High GPU Temperature", StringComparison.OrdinalIgnoreCase) ||
                    evaluation.Score < 60)
                {
                    severity = AlertSeverity.Critical;
                }

                SystemAlerts.Add(new AlertModel
                {
                    Title = title,
                    Description = desc,
                    Metric = $"{HealthScore}/100",
                    Recommendation = "Check hardware cooler / terminate high-load tasks.",
                    Severity = severity
                });
            }
        }

        DataPolled?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public async System.Threading.Tasks.Task CleanRamAsync()
    {
        if (IsCleaningRam) return;
        IsCleaningRam = true;
        RamCleanStatus = "Đang dọn RAM...";

        try
        {
            var memoryService = new NativeMemoryService();
            var report = await System.Threading.Tasks.Task.Run(() => memoryService.OptimizeRamDeep());
            
            // Immediately force a hardware update on UI thread so RAM metrics reflect lowered usage instantly
            await RefreshTelemetryAsync();

            RamCleanStatus = report.FreedMB > 0 
                ? $"Đã dọn {report.FreedMB:N0} MB" 
                : "Đã tối ưu RAM";

            ScheduleRamStatusClear();
        }
        catch
        {
            RamCleanStatus = "Lỗi khi dọn RAM";
            ScheduleRamStatusClear();
        }
        finally
        {
            IsCleaningRam = false;
        }
    }

    private void ScheduleRamStatusClear()
    {
        int currentVersion = System.Threading.Interlocked.Increment(ref _ramStatusVersion);
        _ = System.Threading.Tasks.Task.Delay(4000).ContinueWith(_ =>
        {
            if (_ramStatusVersion != currentVersion) return;
            if (!IsCleaningRam && (RamCleanStatus.StartsWith("Đã") || RamCleanStatus.StartsWith("Lỗi")))
            {
                if (System.Windows.Application.Current?.Dispatcher != null)
                {
                    System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (_ramStatusVersion == currentVersion && !IsCleaningRam)
                        {
                            RamCleanStatus = string.Empty;
                        }
                    });
                }
                else
                {
                    if (_ramStatusVersion == currentVersion && !IsCleaningRam)
                    {
                        RamCleanStatus = string.Empty;
                    }
                }
            }
        });
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task OpenRamOptimizerAsync()
    {
        var window = new RamOptimizerWindow
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.ShowDialog();

        // Immediately update telemetry when optimizer window closes
        await RefreshTelemetryAsync();
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task CleanJunkAsync()
    {
        var window = new JunkCleanerWindow
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.ShowDialog();

        // Immediately update telemetry when junk cleaner closes
        await RefreshTelemetryAsync();
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
