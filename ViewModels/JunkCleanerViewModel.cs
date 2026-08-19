using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace PCHealthDashboard.ViewModels;

public partial class JunkItem : ObservableObject
{
    [ObservableProperty] private string _filePath = string.Empty;
    [ObservableProperty] private long _size;
    [ObservableProperty] private bool _isSelected = true;

    public string SizeStr => $"{Size / 1024.0 / 1024.0:F2} MB";
}

public partial class DriveSelectionItem : ObservableObject
{
    [ObservableProperty] private string _driveName = string.Empty;
    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private bool _isSelected = true;
}

public partial class JunkCleanerViewModel : ObservableObject
{
    [ObservableProperty] private ObservableCollection<DriveSelectionItem> _drives = new();
    [ObservableProperty] private ObservableCollection<JunkItem> _junkFiles = new();
    
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private bool _isScanning = false;
    [ObservableProperty] private bool _isCleaning = false;
    [ObservableProperty] private bool _canClean = false;
    [ObservableProperty] private bool _canScan = true;

    [ObservableProperty] private string _totalJunkSize = "0 MB";

    public JunkCleanerViewModel()
    {
        LoadDrives();
    }

    private void LoadDrives()
    {
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
        {
            Drives.Add(new DriveSelectionItem
            {
                DriveName = drive.Name,
                DisplayName = $"Ổ đĩa {drive.Name} ({drive.VolumeLabel}) - Trống {drive.AvailableFreeSpace / 1024 / 1024 / 1024} GB",
                IsSelected = true
            });
        }
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        IsScanning = true;
        CanScan = false;
        CanClean = false;
        JunkFiles.Clear();
        TotalJunkSize = "0 MB";
        
        string sysDrive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System)) ?? "C:\\";

        foreach (var driveItem in Drives.Where(d => d.IsSelected))
        {
            await ScanDirectoryAsync(Path.Combine(driveItem.DriveName, "$Recycle.Bin"));
            await ScanDirectoryAsync(Path.Combine(driveItem.DriveName, "Temp"));

            if (driveItem.DriveName.Equals(sysDrive, StringComparison.OrdinalIgnoreCase))
            {
                await ScanDirectoryAsync(Path.GetTempPath());
                await ScanDirectoryAsync(Path.Combine(sysDrive, "Windows", "Temp"));
                await ScanDirectoryAsync(Path.Combine(sysDrive, "Windows", "SoftwareDistribution", "Download"));
            }
        }

        long totalSize = JunkFiles.Sum(x => x.Size);
        TotalJunkSize = $"{totalSize / 1024.0 / 1024.0:F2} MB";
        StatusMessage = $"Scan completed. Found {JunkFiles.Count} files.";

        IsScanning = false;
        CanScan = true;
        if (JunkFiles.Any()) CanClean = true;
    }

    private async Task ScanDirectoryAsync(string path)
    {
        try
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => StatusMessage = $"Scanning: {path}");
            var dir = new DirectoryInfo(path);
            if (!dir.Exists) return;

            var resultItems = new List<JunkItem>();

            await Task.Run(() => {
                try
                {
                    foreach (var file in dir.GetFiles())
                    {
                        try { resultItems.Add(new JunkItem { FilePath = file.FullName, Size = file.Length }); } catch { }
                    }
                }
                catch { }
            });

            if (resultItems.Any())
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() => 
                {
                    foreach(var item in resultItems) JunkFiles.Add(item);
                });
            }

            var dirs = await Task.Run(() => {
                try { return dir.GetDirectories(); } catch { return new DirectoryInfo[0]; }
            });

            foreach (var subDir in dirs)
            {
                await ScanDirectoryAsync(subDir.FullName);
            }
        }
        catch { }
    }

    [RelayCommand]
    private async Task CleanAsync()
    {
        IsCleaning = true;
        CanClean = false;
        CanScan = false;
        
        long freed = 0;
        var toDelete = JunkFiles.Where(x => x.IsSelected).ToList();
        
        await Task.Run(() => 
        {
            int count = 0;
            foreach (var item in toDelete)
            {
                try
                {
                    count++;
                    if (count % 10 == 0)
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() => StatusMessage = $"Deleting: {item.FilePath}");
                    }
                    File.Delete(item.FilePath);
                    freed += item.Size;
                    
                    System.Windows.Application.Current.Dispatcher.Invoke(() => JunkFiles.Remove(item));
                }
                catch { }
            }
        });

        TotalJunkSize = $"{JunkFiles.Sum(x => x.Size) / 1024.0 / 1024.0:F2} MB";
        StatusMessage = $"Cleaned {freed / 1024.0 / 1024.0:F2} MB of junk files.";
        System.Windows.MessageBox.Show($"Đã dọn dẹp {(freed / 1024.0 / 1024.0):F2} MB rác hệ thống thành công!", "Hoàn tất", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);

        IsCleaning = false;
        CanScan = true;
        if (JunkFiles.Any()) CanClean = true;
    }
}
