using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCHealthDashboard.Helpers;
using PCHealthDashboard.Models;
using PCHealthDashboard.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace PCHealthDashboard.ViewModels;

public partial class JunkItem : ObservableObject
{
    [ObservableProperty] private string _filePath = string.Empty;
    [ObservableProperty] private long _size;
    [ObservableProperty] private string _category = "Temp";
    [ObservableProperty] private bool _isSelected = true;

    public string SizeStr => ByteSizeFormatter.FormatBytes(Size);
}

public partial class DriveSelectionItem : ObservableObject
{
    [ObservableProperty] private string _driveName = string.Empty;
    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private bool _isSelected = true;
}

public partial class JunkCleanerViewModel : ObservableObject
{
    private readonly IDiskCleanerService _diskCleanerService;
    private CancellationTokenSource? _cts;

    [ObservableProperty] private ObservableCollection<DriveSelectionItem> _drives = new();
    [ObservableProperty] private ObservableCollection<JunkItem> _junkFiles = new();
    
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private bool _isScanning = false;
    [ObservableProperty] private bool _isCleaning = false;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    private bool _canClean = false;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    private bool _canScan = true;

    [ObservableProperty] private string _totalJunkSize = "0 MB";

    public JunkCleanerViewModel() : this(new DiskCleanerService())
    {
    }

    public JunkCleanerViewModel(IDiskCleanerService diskCleanerService)
    {
        _diskCleanerService = diskCleanerService ?? throw new ArgumentNullException(nameof(diskCleanerService));
        LoadDrives();
    }

    public void UpdateTotalJunkSize()
    {
        var selected = JunkFiles.Where(x => x.IsSelected).ToList();
        long totalSelectedBytes = selected.Sum(x => x.Size);
        TotalJunkSize = ByteSizeFormatter.FormatBytes(totalSelectedBytes);
        CanClean = !IsScanning && !IsCleaning && totalSelectedBytes > 0 && selected.Count > 0;
    }

    private void LoadDrives()
    {
        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            {
                long freeGb = drive.AvailableFreeSpace / 1024 / 1024 / 1024;
                Drives.Add(new DriveSelectionItem
                {
                    DriveName = drive.Name,
                    DisplayName = $"Ổ đĩa {drive.Name} ({drive.VolumeLabel}) - Trống {freeGb} GB",
                    IsSelected = true
                });
            }
        }
        catch
        {
            // Fallback default
            Drives.Add(new DriveSelectionItem
            {
                DriveName = "C:\\",
                DisplayName = "Ổ đĩa C:\\ (Hệ thống)",
                IsSelected = true
            });
        }
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        if (IsScanning || IsCleaning) return;

        IsScanning = true;
        CanScan = false;
        CanClean = false;
        JunkFiles.Clear();
        TotalJunkSize = "0 MB";
        StatusMessage = "Scanning system cache, temp folders, and browser data...";

        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        try
        {
            var selectedDrives = Drives.Where(d => d.IsSelected).Select(d => d.DriveName).ToList();
            var scanResult = await _diskCleanerService.ScanJunkAsync(selectedDrives, _cts.Token);

            // Dispatch items to UI in batches of 100 to avoid freezing
            const int batchSize = 100;
            for (int i = 0; i < scanResult.Files.Count; i += batchSize)
            {
                if (_cts.Token.IsCancellationRequested) break;
                var batch = scanResult.Files.Skip(i).Take(batchSize).Select(f =>
                {
                    var item = new JunkItem
                    {
                        FilePath = f.FilePath,
                        Size = f.SizeBytes,
                        Category = f.Category,
                        IsSelected = true
                    };
                    item.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(JunkItem.IsSelected))
                        {
                            UpdateTotalJunkSize();
                        }
                    };
                    return item;
                }).ToList();

                foreach (var item in batch)
                {
                    JunkFiles.Add(item);
                }
            }

            UpdateTotalJunkSize();
            long totalSize = scanResult.TotalSizeBytes;
            StatusMessage = $"Scan complete. Found {scanResult.TotalFilesCount:N0} junk files ({ByteSizeFormatter.FormatBytes(totalSize)}).";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Scan cancelled.";
            UpdateTotalJunkSize();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Scan error: {ex.Message}";
            UpdateTotalJunkSize();
        }
        finally
        {
            IsScanning = false;
            CanScan = true;
            UpdateTotalJunkSize();
        }
    }

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        if (IsCleaning || IsScanning) return;

        var toDelete = JunkFiles.Where(x => x.IsSelected).ToList();
        if (!toDelete.Any())
        {
            StatusMessage = "No items selected for cleaning.";
            UpdateTotalJunkSize();
            return;
        }

        IsCleaning = true;
        CanClean = false;
        CanScan = false;
        StatusMessage = "Starting safe disk cleanup...";

        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        var progress = new Progress<DiskCleaningProgress>(p =>
        {
            StatusMessage = $"Cleaning: {p.FilesDeleted:N0} deleted ({p.CleanedMB:F1} MB), {p.FilesSkippedLocked:N0} locked skipped";
        });

        try
        {
            var selectedDrives = Drives.Where(d => d.IsSelected).Select(d => d.DriveName).ToList();
            var filePaths = toDelete.Select(x => x.FilePath).ToList();

            var report = await _diskCleanerService.CleanSpecificFilesAsync(
                filePaths,
                emptyRecycleBin: true,
                selectedDrives,
                progress,
                _cts.Token
            );

            // Update remaining junk items
            var remaining = JunkFiles.Where(x => !x.IsSelected).ToList();
            JunkFiles.Clear();
            foreach (var r in remaining)
            {
                r.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(JunkItem.IsSelected))
                    {
                        UpdateTotalJunkSize();
                    }
                };
                JunkFiles.Add(r);
            }

            UpdateTotalJunkSize();
            StatusMessage = $"Cleaned {report.TotalCleanedMB:F2} MB. {report.FilesDeleted:N0} deleted, {report.FilesSkippedLocked:N0} locked files skipped safely.";

            System.Windows.MessageBox.Show(
                $"Dọn dẹp hoàn tất!\n\n" +
                $"• Dung lượng giải phóng: {report.TotalCleanedMB:F2} MB\n" +
                $"• Tệp đã xóa: {report.FilesDeleted:N0}\n" +
                $"• Tệp đang khóa (bỏ qua an toàn): {report.FilesSkippedLocked:N0}\n" +
                $"• Thùng rác (Recycle Bin): {(report.RecycleBinEmptied ? "Đã dọn sạch" : "Bỏ qua")}\n" +
                $"• Thư mục rỗng đã xóa: {report.DirectoriesRemoved:N0}\n" +
                $"• Thời gian thực thi: {report.Duration.TotalSeconds:F2}s",
                "Dọn dẹp rác hệ thống",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Cleanup cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Cleanup error: {ex.Message}";
        }
        finally
        {
            IsCleaning = false;
            CanScan = true;
            UpdateTotalJunkSize();
        }
    }
}
