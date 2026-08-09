using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using PCHealthDashboard.Services;
using PCHealthDashboard.Models;

namespace PCHealthDashboard.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly HardwareMonitorService _hardwareService;
    private readonly NetworkMonitorService _networkService;
    private readonly HealthAnalyzerService _healthAnalyzer;
    private readonly DispatcherTimer _pollTimer;

    [ObservableProperty] private string _currentView = "Tổng quan";

    [ObservableProperty]
    private string _osdColor = "Yellow";
    
    partial void OnOsdColorChanged(string value)
    {
        try {
            var dict = new System.Collections.Generic.Dictionary<string, string> { { "OsdColor", value } };
            var json = System.Text.Json.JsonSerializer.Serialize(dict);
            System.IO.File.WriteAllText("settings.json", json);
        } catch { }
    }

    [ObservableProperty]
    private bool _isEfficiencyMode;

    [ObservableProperty]
    private bool _isPopupVisible;

    [ObservableProperty]
    private bool _isCompactMode;

    // Update both flags to adjust polling interval
    partial void OnIsEfficiencyModeChanged(bool value)
    {
        UpdatePollingInterval();
    }

    partial void OnIsCompactModeChanged(bool value)
    {
        UpdatePollingInterval();
    }

    private void UpdatePollingInterval()
    {
        if (_pollTimer != null)
        {
            if (!_pollTimer.IsEnabled)
            {
                _pollTimer.Start();
            }

            // If app is hidden/minimized but popup is visible, it's either widget or compact
            if (IsCompactMode)
            {
                _pollTimer.Interval = TimeSpan.FromSeconds(2); // Giảm tần suất cho Compact mode (nhẹ nhất)
            }
            else if (IsEfficiencyMode && !IsPopupVisible)
            {
                _pollTimer.Interval = TimeSpan.FromSeconds(3); // Cryo mode (nhưng bị block ở PollData)
            }
            else
            {
                _pollTimer.Interval = TimeSpan.FromSeconds(1); // Dashboard hoặc Popup lớn
            }
        }
    }

    [ObservableProperty] private int _healthScore;
    [ObservableProperty] private float _cpuTemp;
    [ObservableProperty] private float _cpuUsage;
    [ObservableProperty] private float _gpuTemp;
    [ObservableProperty] private float _gpuUsage;
    [ObservableProperty] private float _gpuVram;
    [ObservableProperty] private float _ramUsed;
    [ObservableProperty] private float _ramTotal;
    [ObservableProperty] private float _ssdHealth;
    [ObservableProperty] private float _ssdTemp;
    [ObservableProperty] private float _ssdFreeSpace;
    [ObservableProperty] private float _ssdTotalSpace;
    [ObservableProperty] private float _ssdUsedSpace;
    [ObservableProperty] private long _pingLatency;
    [ObservableProperty] private double _packetLoss;
    [ObservableProperty] private double _downloadMbps;
    [ObservableProperty] private double _uploadMbps;
    [ObservableProperty] private bool _isAppActive = true;

    [ObservableProperty] private ObservableCollection<AlertModel> _systemAlerts = new();
    
    private readonly System.Collections.Generic.Dictionary<string, DateTime> _alertDebounce = new();
    private readonly System.Collections.Generic.Dictionary<string, AlertModel> _activeAlerts = new();

    public ObservableCollection<DriveStatus> Drives { get; } = new();

    public ObservableCollection<double> CpuHistory { get; } = new();
    public ObservableCollection<double> GpuHistory { get; } = new();
    public ObservableCollection<double> RamHistory { get; } = new();
    public ObservableCollection<double> StorageHistory { get; } = new();
    public ObservableCollection<double> PingHistory { get; } = new();
    public ObservableCollection<double> LossHistory { get; } = new();
    public ObservableCollection<double> NetworkSpeedHistory { get; } = new();

    public MainViewModel()
    {
        _hardwareService = new HardwareMonitorService();
        _networkService = new NetworkMonitorService();
        _healthAnalyzer = new HealthAnalyzerService();
        
        // Load settings
        if (System.IO.File.Exists("settings.json"))
        {
            try {
                var json = System.IO.File.ReadAllText("settings.json");
                var dict = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, string>>(json);
                if (dict != null && dict.TryGetValue("OsdColor", out string? color))
                {
                    _osdColor = color;
                }
            } catch { }
        }

        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _pollTimer.Tick += async (s, e) => await PollDataAsync();
        _pollTimer.Start();

        // Subscribe to main window state changes for auto efficiency mode
        if (System.Windows.Application.Current?.MainWindow != null)
        {
            System.Windows.Application.Current.MainWindow.StateChanged += MainWindow_StateChanged;
            // Initial check
            IsEfficiencyMode = System.Windows.Application.Current.MainWindow.WindowState == WindowState.Minimized;
        }
    }

    private bool _isPolling;
    public event Action? DataPolled;

    private async Task PollDataAsync()
    {
        if (_isPolling) return;
        if (IsEfficiencyMode && !IsPopupVisible) return; // "chết đông" (tự động) khi app ẩn hoàn toàn

        try
        {
            _isPolling = true;
            bool isFullUpdate = IsAppActive || !IsEfficiencyMode || (IsPopupVisible && !IsCompactMode);
            await Task.Run(() => { _hardwareService.Update(isFullUpdate); });

            var cpuStats = _hardwareService.GetCpuStats();
            CpuTemp = cpuStats.Temp > 0 ? cpuStats.Temp : 45f; // Safe default if sensor unavailable on some VMs
            CpuUsage = cpuStats.Usage;

            var gpuStats = _hardwareService.GetGpuStats();
            GpuTemp = gpuStats.Temp > 0 ? gpuStats.Temp : 40f;
            GpuUsage = gpuStats.Load;
            GpuVram = gpuStats.VramUsed;

            var ramStats = _hardwareService.GetRamStats();
            RamUsed = ramStats.UsedGB;
            RamTotal = ramStats.TotalGB > 0 ? ramStats.TotalGB : 16f;

            if (isFullUpdate)
            {
                var drives = _hardwareService.GetDrivesStats();
                
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (Drives.Count == drives.Count)
                    {
                        for (int i = 0; i < drives.Count; i++)
                        {
                            Drives[i].Name = drives[i].Name;
                            Drives[i].Type = drives[i].Type;
                            Drives[i].Interface = drives[i].Interface;
                            Drives[i].TotalGB = drives[i].TotalGB;
                            Drives[i].FreeGB = drives[i].FreeGB;
                            Drives[i].Health = drives[i].Health;
                            Drives[i].Temp = drives[i].Temp;
                        }
                    }
                    else
                    {
                        Drives.Clear();
                        foreach (var d in drives) Drives.Add(d);
                    }
                });

                if (drives.Count > 0)
                {
                    var systemDrive = drives.FirstOrDefault(d => d.Name.Contains("C")) ?? drives[0];
                    SsdHealth = systemDrive.Health;
                    SsdTemp = systemDrive.Temp;
                    SsdFreeSpace = systemDrive.FreeGB;
                    SsdTotalSpace = systemDrive.TotalGB;
                    SsdUsedSpace = systemDrive.UsedGB;
                }
            }

            if (isFullUpdate)
            {
                var netStats = await _networkService.GetNetworkStatusAsync();
                PingLatency = netStats.Latency;
                PacketLoss = netStats.PacketLoss;
                DownloadMbps = netStats.DownloadMbps;
                UploadMbps = netStats.UploadMbps;
            }

            HealthScore = _healthAnalyzer.CalculateHealthScore(
                SsdHealth, SsdFreeSpace, SsdTotalSpace,
                CpuTemp, GpuTemp,
                RamUsed, RamTotal,
                PacketLoss);

            UpdateAlerts();

            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                if (IsAppActive || !IsEfficiencyMode || (IsPopupVisible && !IsCompactMode))
                {
                    CpuHistory.Add(CpuUsage);
                    GpuHistory.Add(GpuUsage);
                    RamHistory.Add(RamTotal > 0 ? (RamUsed / RamTotal) * 100 : 0);
                    StorageHistory.Add(SsdTotalSpace > 0 ? ((SsdTotalSpace - SsdFreeSpace) / SsdTotalSpace) * 100 : 0);
                    PingHistory.Add(PingLatency);
                    LossHistory.Add(PacketLoss);
                }
                
                // Always update NetworkSpeedHistory as KittyWindow's sparkline depends on it
                NetworkSpeedHistory.Add(DownloadMbps);

                const int maxPoints = 60; // 60 seconds of history — lightweight for software rendering
                if (CpuHistory.Count > maxPoints) CpuHistory.RemoveAt(0);
                if (GpuHistory.Count > maxPoints) GpuHistory.RemoveAt(0);
                if (RamHistory.Count > maxPoints) RamHistory.RemoveAt(0);
                if (StorageHistory.Count > maxPoints) StorageHistory.RemoveAt(0);
                if (PingHistory.Count > maxPoints) PingHistory.RemoveAt(0);
                if (LossHistory.Count > maxPoints) LossHistory.RemoveAt(0);
                if (NetworkSpeedHistory.Count > maxPoints) NetworkSpeedHistory.RemoveAt(0);
                
                DataPolled?.Invoke();
            });

        }
        finally
        {
            _isPolling = false;
        }
    }

    private void UpdateAlerts()
    {
        var newAlerts = new System.Collections.Generic.List<AlertModel>();
        
        void TryAddAlert(string id, AlertSeverity severity, string title, string desc, string metric, string rec)
        {
            // Simple hysteresis: Require condition to be clear for 30s before alerting again?
            // Actually, we just keep the alert active as long as the condition is true.
            if (!_activeAlerts.ContainsKey(id))
            {
                var alert = new AlertModel { Id = id, Severity = severity, Title = title, Description = desc, Metric = metric, Recommendation = rec };
                newAlerts.Add(alert);
                _activeAlerts[id] = alert;
            }
            else
            {
                newAlerts.Add(_activeAlerts[id]);
            }
        }

        // 1. Storage
        if (SsdTotalSpace > 0)
        {
            float freePercent = (SsdFreeSpace / SsdTotalSpace) * 100f;
            if (freePercent < 15)
            {
                TryAddAlert("storage_full", AlertSeverity.Warning, "Storage bottleneck",
                    "Your system drive is nearly full. Low free space can reduce storage performance and leave less room for temporary files, caches, and paging.",
                    $"{SsdFreeSpace:F1} GB free ({freePercent:F0}%)",
                    "Keep at least 10–20% free space when practical.");
            }
        }
        
        if (SsdHealth < 80)
        {
            TryAddAlert("storage_health", AlertSeverity.Critical, "Storage health warning",
                "The system drive reports degraded health.",
                $"{SsdHealth:F0}% Health",
                "Back up important data and consider replacing the drive if the condition persists.");
        }

        // 2. Memory
        if (RamTotal > 0)
        {
            float ramUsage = (RamUsed / RamTotal) * 100f;
            if (ramUsage > 95 && SsdUsedSpace > 0 /* Assuming disk activity implies paging */)
            {
                 TryAddAlert("mem_pressure", AlertSeverity.Critical, "High memory pressure",
                    "RAM usage is high and paging activity may have increased. Windows may be moving memory pages between RAM and storage, which can significantly increase latency.",
                    $"{ramUsage:F0}% Used",
                    "Close memory-intensive applications.");
            }
            else if (ramUsage > 90)
            {
                 TryAddAlert("mem_high", AlertSeverity.Warning, "High memory usage",
                    "Memory usage is above 90%. Available memory is becoming limited.",
                    $"{ramUsage:F0}% Used",
                    "Close unused applications.");
            }
        }

        // 3. CPU/GPU Thermal
        if (CpuTemp > 88)
        {
            TryAddAlert("cpu_temp", AlertSeverity.Warning, "High CPU temperature",
                "CPU temperature is high. Sustained high temperatures may cause the processor to reduce operating frequency to control heat.",
                $"{CpuTemp:F0}°C",
                "Check cooling and airflow if this persists.");
        }
        
        if (GpuTemp > 88)
        {
            TryAddAlert("gpu_temp", AlertSeverity.Warning, "High GPU temperature",
                "GPU temperature is high.",
                $"{GpuTemp:F0}°C",
                "Check cooling and airflow if this persists.");
        }

        // Sync observable collection
        var toRemove = SystemAlerts.Where(a => !newAlerts.Any(na => na.Id == a.Id)).ToList();
        foreach (var r in toRemove)
        {
            SystemAlerts.Remove(r);
            _activeAlerts.Remove(r.Id);
        }
        
        foreach (var na in newAlerts)
        {
            if (!SystemAlerts.Contains(na)) SystemAlerts.Add(na);
        }
        
        // Add a "healthy" pseudo-alert if empty for UI binding simplicity
        if (SystemAlerts.Count == 0)
        {
            if (!_activeAlerts.ContainsKey("healthy"))
            {
                var healthy = new AlertModel { Id = "healthy", Severity = AlertSeverity.Info, Title = "System Healthy", Description = "No issues detected.", Metric = "All OK", Recommendation = "" };
                SystemAlerts.Add(healthy);
                _activeAlerts["healthy"] = healthy;
            }
        }
        else if (SystemAlerts.Count > 1 && _activeAlerts.ContainsKey("healthy"))
        {
            var h = SystemAlerts.FirstOrDefault(a => a.Id == "healthy");
            if (h != null) SystemAlerts.Remove(h);
            _activeAlerts.Remove("healthy");
        }
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void SwitchView(string viewName)
    {
        CurrentView = viewName;
    }

    public void Dispose()
    {
        _pollTimer.Stop();
        _hardwareService.Dispose();
        // Unsubscribe to avoid memory leaks
        if (System.Windows.Application.Current?.MainWindow != null)
        {
            System.Windows.Application.Current.MainWindow.StateChanged -= MainWindow_StateChanged;
        }
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (System.Windows.Application.Current?.MainWindow?.WindowState == WindowState.Minimized)
        {
            IsEfficiencyMode = true;
            IsAppActive = false;
        }
        else
        {
            IsEfficiencyMode = false;
            IsAppActive = true;
        }
    }
}
