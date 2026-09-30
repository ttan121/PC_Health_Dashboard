// ============================================================================
// PC Health Dashboard - ViewModels/MainViewModel.cs
// MVVM ViewModel with Asymmetric EWMA Health Engine & Zero-Disk-Wear RingBuffers
// ============================================================================

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibreHardwareMonitor.PawnIo;
using PCHealthDashboard.Helpers;
using PCHealthDashboard.Models;
using PCHealthDashboard.Services;
using System;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;

namespace PCHealthDashboard.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly HardwareMonitorService _hardwareMonitor;
    private readonly StorageHealthService _storageHealthService;
    private readonly ProcessMonitorService _processMonitor;
    private readonly IHealthScoreCalculator _healthCalculator;
    private readonly DispatcherTimer _timer;
    private readonly System.Threading.SemaphoreSlim _telemetrySemaphore = new(1, 1);
    private int _ramStatusVersion = 0;
    private bool _isFirstTelemetryPoll = true;
    private int _slowPollCounter;
    private (float read, float write, float usedSpace, float totalSpace) _cachedStorageStats;
    private StorageHealthSnapshot _cachedStorageHealth = StorageHealthSnapshot.Empty;
    private (float download, float upload) _cachedNetworkStats;

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
    public ObservableCollection<StorageDriveReading> StorageDrives { get; } = new();
    public ObservableCollection<ProcessEntry> ProcessEntries { get; } = new();
    public ICollectionView ProcessEntriesView { get; }
    [ObservableProperty] private string _storageHealthStatus = "Đang chờ dữ liệu ổ đĩa.";
    [ObservableProperty] private string _processOperationStatus = string.Empty;
    [ObservableProperty] private string _processFilter = string.Empty;
    [ObservableProperty] private bool _isEndingProcess;
    public string ProcessSummary => $"{ProcessEntriesView.Cast<object>().Count()} / {ProcessEntries.Count}";
    public bool IsProcessFilterEmpty => string.IsNullOrWhiteSpace(ProcessFilter);

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
    [ObservableProperty] private float _cpuTempRaw;
    [ObservableProperty] private string _cpuTempSource = string.Empty;
    [ObservableProperty] private float _cpuPower;
    [ObservableProperty] private float _cpuClock;
    public string CpuTemperatureDisplay => float.IsFinite(CpuTemp) && CpuTemp > 0f ? $"{CpuTemp:F0}°C" : "N/A";
    public string CpuTemperatureTooltip => CpuTempRaw > 0f
        ? $"Nguồn: {CpuTempSource}; mẫu mới nhất {CpuTempRaw:F1}°C. Số hiển thị dùng trung vị tối đa 5 mẫu và làm mượt theo thời gian; cảnh báo dùng dữ liệu lọc và đưa mức từ 95°C lên ngay."
        : "Chưa nhận được mẫu nhiệt độ CPU hợp lệ.";
    public string CpuTemperatureStatus => CpuTemp > 0f
        ? string.Empty
        : PawnIo.IsInstalled
            ? "Windows chưa cung cấp số đo sensor CPU"
            : "Cần cài PawnIO để đọc sensor CPU";

    // GPU Telemetry
    public ObservableCollection<GpuStatModel> Gpus { get; } = new();

    // RAM Telemetry
    [ObservableProperty] private float _ramUsed;
    [ObservableProperty] private float _ramTotal = 16f;

    // Storage Telemetry
    [ObservableProperty] private float _ssdUsedSpace;
    [ObservableProperty] private float _ssdTotalSpace;
    [ObservableProperty] private float? _ssdHealth;
    public string StorageWearSummary => SsdHealth is float remaining
        ? $"Endurance ước tính thấp nhất trong các ổ: còn khoảng {remaining:F0}%"
        : "Wear/SMART: Windows hoặc driver chưa cung cấp";
    public string SystemDriveLabel => System.IO.Path.GetPathRoot(Environment.SystemDirectory) ?? "System drive";

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
        _storageHealthService = new StorageHealthService();
        _processMonitor = new ProcessMonitorService();
        ProcessEntriesView = CollectionViewSource.GetDefaultView(ProcessEntries);
        ProcessEntriesView.Filter = MatchesProcessFilter;
        ProcessEntriesView.SortDescriptions.Add(new SortDescription(nameof(ProcessEntry.Name), ListSortDirection.Ascending));
        ProcessEntriesView.SortDescriptions.Add(new SortDescription(nameof(ProcessEntry.ProcessId), ListSortDirection.Ascending));

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
        (float usage, float temp, float displayTemp, float rawTemp, string tempSource, float power, float clock) cpu = default;
        System.Collections.Generic.List<GpuStatModel> gpuStats = null!;
        (float used, float total) ram = default;
        (float read, float write, float usedSpace, float totalSpace) storage = default;
        StorageHealthSnapshot storageHealth = StorageHealthSnapshot.Empty;
        (float download, float upload) net = default;
        System.Collections.Generic.IReadOnlyList<ProcessReading>? processReadings = null;

        await System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                _hardwareMonitor.Update(fullUpdate: _isFirstTelemetryPoll);
                cpu = _hardwareMonitor.GetCpuStats();
                gpuStats = _hardwareMonitor.GetGpusStats();
                ram = _hardwareMonitor.GetRamStats();
                if (_isFirstTelemetryPoll || ++_slowPollCounter >= 3)
                {
                    _isFirstTelemetryPoll = false;
                    _slowPollCounter = 0;
                    _cachedStorageStats = _hardwareMonitor.GetStorageStats();
                    _cachedStorageHealth = _storageHealthService.GetSnapshot();
                    _cachedNetworkStats = _hardwareMonitor.GetNetworkStats();
                    processReadings = _processMonitor.GetSnapshot();
                }
                storage = _cachedStorageStats;
                storageHealth = _cachedStorageHealth;
                net = _cachedNetworkStats;
            }
            catch
            {
                // Prevent background polling failure from killing telemetry task
            }
        });

        CpuUsage = cpu.usage;
        CpuTemp = cpu.displayTemp;
        CpuTempRaw = cpu.rawTemp;
        CpuTempSource = cpu.tempSource ?? string.Empty;
        CpuPower = cpu.power;
        CpuClock = cpu.clock;
        if (processReadings != null)
            UpdateProcessEntries(processReadings);

        if (gpuStats != null)
        {
            foreach (var stat in gpuStats)
            {
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
        StorageHealthStatus = storageHealth.Status;
        if (!StorageDrives.SequenceEqual(storageHealth.Drives))
        {
            StorageDrives.Clear();
            foreach (var drive in storageHealth.Drives)
                StorageDrives.Add(drive);
        }
        var enduranceReadings = storageHealth.Drives
            .Where(drive => drive.WearPercent.HasValue)
            .Select(drive => Math.Clamp(100f - drive.WearPercent!.Value, 0f, 100f))
            .ToArray();
        SsdHealth = enduranceReadings.Length == 0 ? null : enduranceReadings.Min();

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
            // Use the robust median for scoring and warnings, with an immediate
            // path for extreme raw readings that need a fast alert.
            CpuTemp: cpu.rawTemp >= 95f ? cpu.rawTemp : cpu.temp,
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
        var storageAlerts = CreateStorageAlerts();

        if (evaluation.ActiveAlerts.Count == 0 && storageAlerts.Count == 0)
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

            foreach (var alert in storageAlerts)
            {
                HealthIssues.Add($"{alert.Title}\n{alert.Description}");
                SystemAlerts.Add(alert);
            }

            if (storageAlerts.Any(alert => alert.Severity == AlertSeverity.Critical))
            {
                HealthStatus = "Critical";
                HealthStatusColor = "#ef4444";
            }
            else if (storageAlerts.Any(alert => alert.Severity == AlertSeverity.Warning))
            {
                HealthStatus = "Warning";
                HealthStatusColor = "#f59e0b";
            }
        }

        DataPolled?.Invoke(this, EventArgs.Empty);
    }

    partial void OnCpuTempChanged(float value)
    {
        OnPropertyChanged(nameof(CpuTemperatureDisplay));
        OnPropertyChanged(nameof(CpuTemperatureStatus));
        OnPropertyChanged(nameof(CpuTemperatureTooltip));
    }

    partial void OnCpuTempRawChanged(float value)
    {
        OnPropertyChanged(nameof(CpuTemperatureTooltip));
    }

    partial void OnCpuTempSourceChanged(string value)
    {
        OnPropertyChanged(nameof(CpuTemperatureTooltip));
    }

    partial void OnIsEndingProcessChanged(bool value)
    {
        EndProcessCommand.NotifyCanExecuteChanged();
    }

    partial void OnProcessFilterChanged(string value)
    {
        ProcessEntriesView.Refresh();
        OnPropertyChanged(nameof(IsProcessFilterEmpty));
        OnPropertyChanged(nameof(ProcessSummary));
    }

    private bool MatchesProcessFilter(object item)
    {
        if (item is not ProcessEntry process)
            return false;

        var query = ProcessFilter.Trim();
        return query.Length == 0 ||
               process.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               process.ProcessId.ToString().Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateProcessEntries(System.Collections.Generic.IReadOnlyList<ProcessReading> readings)
    {
        var retainedProcessIds = new System.Collections.Generic.HashSet<int>();
        foreach (var reading in readings)
        {
            var entry = ProcessEntries.FirstOrDefault(candidate => candidate.ProcessId == reading.ProcessId);
            if (entry != null && entry.StartTimeUtcTicks != reading.StartTimeUtcTicks)
            {
                ProcessEntries.Remove(entry);
                entry = null;
            }

            if (entry == null)
            {
                entry = new ProcessEntry(reading);
                var insertionIndex = 0;
                while (insertionIndex < ProcessEntries.Count &&
                       CompareProcessEntries(ProcessEntries[insertionIndex], entry) <= 0)
                    insertionIndex++;
                ProcessEntries.Insert(insertionIndex, entry);
            }
            else
            {
                entry.Update(reading);
            }

            retainedProcessIds.Add(reading.ProcessId);
        }

        for (var index = ProcessEntries.Count - 1; index >= 0; index--)
        {
            if (!retainedProcessIds.Contains(ProcessEntries[index].ProcessId))
                ProcessEntries.RemoveAt(index);
        }

        OnPropertyChanged(nameof(ProcessSummary));
    }

    private static int CompareProcessEntries(ProcessEntry left, ProcessEntry right)
    {
        var nameOrder = StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
        return nameOrder != 0 ? nameOrder : left.ProcessId.CompareTo(right.ProcessId);
    }

    private bool CanEndProcess(ProcessEntry? entry) => entry is { CanTerminate: true } && !IsEndingProcess;

    [RelayCommand(CanExecute = nameof(CanEndProcess))]
    private async System.Threading.Tasks.Task EndProcessAsync(ProcessEntry? entry)
    {
        if (entry == null || !entry.CanTerminate || IsEndingProcess)
            return;

        var owner = System.Windows.Application.Current?.MainWindow;
        var confirmation = System.Windows.MessageBox.Show(
            owner,
            $"Yêu cầu đóng {entry.Name} (PID {entry.ProcessId})?\nCPU {entry.CpuDisplay} · RAM {entry.MemoryDisplay}\nỨng dụng sẽ được yêu cầu tự đóng trước.",
            "Kết thúc tác vụ",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
            return;

        IsEndingProcess = true;
        try
        {
            var result = await _processMonitor.RequestCloseAsync(entry);
            if (result == ProcessCloseResult.NeedsForce)
            {
                var forceConfirmation = System.Windows.MessageBox.Show(
                    owner,
                    $"{entry.Name} chưa đóng được bình thường. Buộc dừng có thể làm mất dữ liệu chưa lưu. Bạn có muốn buộc dừng riêng tiến trình này không?",
                    "Xác nhận buộc dừng",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);

                if (forceConfirmation == MessageBoxResult.Yes)
                    result = await _processMonitor.ForceTerminateAsync(entry);
                else
                    ProcessOperationStatus = $"Đã hủy buộc dừng {entry.Name}.";
            }

            if (result == ProcessCloseResult.Exited)
            {
                ProcessOperationStatus = $"Đã đóng {entry.Name}.";
                await RefreshTelemetryAsync();
            }
            else if (result == ProcessCloseResult.AccessDenied)
            {
                ProcessOperationStatus = $"Windows từ chối quyền đóng {entry.Name}.";
            }
            else if (result == ProcessCloseResult.ProcessChanged)
            {
                ProcessOperationStatus = "Tiến trình đã thoát hoặc PID đã được tái sử dụng; không gửi lệnh đóng.";
            }
            else if (result == ProcessCloseResult.ForceFailed)
            {
                ProcessOperationStatus = $"Đã gửi lệnh buộc dừng {entry.Name}, nhưng Windows chưa xác nhận tiến trình đã thoát.";
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                           or System.ComponentModel.Win32Exception
                                           or UnauthorizedAccessException)
        {
            ProcessOperationStatus = $"Không thể đóng {entry.Name}: {exception.Message}";
        }
        finally
        {
            IsEndingProcess = false;
        }
    }

    partial void OnSsdHealthChanged(float? value)
    {
        OnPropertyChanged(nameof(StorageWearSummary));
    }

    private List<AlertModel> CreateStorageAlerts()
    {
        var alerts = new List<AlertModel>();
        foreach (var drive in StorageDrives)
        {
            if (drive.NvmeCriticalWarning is byte criticalWarning && criticalWarning != 0)
            {
                alerts.Add(new AlertModel
                {
                    Title = $"NVMe critical warning · {drive.DisplayName}",
                    Description = drive.CriticalWarningDisplay,
                    Metric = $"0x{criticalWarning:X2}",
                    Recommendation = "Sao lưu dữ liệu và kiểm tra ổ bằng tiện ích của nhà sản xuất.",
                    Severity = AlertSeverity.Critical
                });
            }
            else if (drive.HealthStatusId == 2)
            {
                alerts.Add(new AlertModel
                {
                    Title = $"Ổ đĩa báo trạng thái không khỏe · {drive.DisplayName}",
                    Description = drive.DeviceHealthDisplay,
                    Metric = "Unhealthy",
                    Recommendation = "Sao lưu dữ liệu và kiểm tra ổ đĩa.",
                    Severity = AlertSeverity.Critical
                });
            }
            else if (drive.HealthStatusId == 1)
            {
                alerts.Add(new AlertModel
                {
                    Title = $"Ổ đĩa có cảnh báo · {drive.DisplayName}",
                    Description = drive.DeviceHealthDisplay,
                    Metric = "Warning",
                    Recommendation = "Kiểm tra chi tiết bằng tiện ích của nhà sản xuất.",
                    Severity = AlertSeverity.Warning
                });
            }

            if (drive.NvmeCriticalWarning is not byte warning || (warning & 0x01) == 0)
            {
                if (drive.AvailableSparePercent is int spare && drive.SpareThresholdPercent is int threshold && spare <= threshold)
                {
                    alerts.Add(new AlertModel
                    {
                        Title = $"Spare SSD chạm ngưỡng · {drive.DisplayName}",
                        Description = $"Spare khả dụng {spare}%, ngưỡng cảnh báo {threshold}%.",
                        Metric = $"{spare}%",
                        Recommendation = "Sao lưu dữ liệu và kiểm tra ổ đĩa.",
                        Severity = AlertSeverity.Warning
                    });
                }
            }

            if ((drive.MediaErrors ?? 0) > 0 || (drive.ReadErrorsUncorrected ?? 0) > 0 || (drive.WriteErrorsUncorrected ?? 0) > 0)
            {
                alerts.Add(new AlertModel
                {
                    Title = $"Ổ đĩa báo lỗi dữ liệu · {drive.DisplayName}",
                    Description = drive.NvmeErrorCountersDisplay.Length > 0
                        ? drive.NvmeErrorCountersDisplay
                        : drive.UncorrectedErrorsDisplay,
                    Metric = "Storage errors",
                    Recommendation = "Sao lưu dữ liệu quan trọng và chẩn đoán ổ bằng công cụ của nhà sản xuất.",
                    Severity = AlertSeverity.Warning
                });
            }
        }

        return alerts;
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
