using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace PCHealthDashboard
{
    public class DriveItem
    {
        public string DriveName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public bool IsSelected { get; set; } = true;
    }

    public partial class JunkCleanerWindow : Window
    {
        private List<DriveItem> _drives = new();

        public JunkCleanerWindow()
        {
            InitializeComponent();
            LoadDrives();
        }

        private void LoadDrives()
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            {
                _drives.Add(new DriveItem
                {
                    DriveName = drive.Name,
                    DisplayName = $"Ổ đĩa {drive.Name} ({drive.VolumeLabel}) - Trống {drive.AvailableFreeSpace / 1024 / 1024 / 1024} GB",
                    IsSelected = true
                });
            }
            DriveList.ItemsSource = _drives;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void CleanButton_Click(object sender, RoutedEventArgs e)
        {
            long freed = 0;
            string sysDrive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System)) ?? "C:\\";

            foreach (var driveItem in _drives.Where(d => d.IsSelected))
            {
                // Clean Recycle Bin on this drive
                freed += CleanDirectory(Path.Combine(driveItem.DriveName, "$Recycle.Bin"));

                // Clean root Temp on this drive
                freed += CleanDirectory(Path.Combine(driveItem.DriveName, "Temp"));

                // If System Drive, clean user temp and windows temp
                if (driveItem.DriveName.Equals(sysDrive, StringComparison.OrdinalIgnoreCase))
                {
                    freed += CleanDirectory(Path.GetTempPath());
                    freed += CleanDirectory(Path.Combine(sysDrive, "Windows", "Temp"));
                    freed += CleanDirectory(Path.Combine(sysDrive, "Windows", "SoftwareDistribution", "Download"));
                }
            }

            System.Windows.MessageBox.Show($"Đã dọn dẹp {(freed / 1024.0 / 1024.0):F1} MB rác hệ thống thành công!", "Hoàn tất", MessageBoxButton.OK, MessageBoxImage.Information);
            this.Close();
        }

        private long CleanDirectory(string path)
        {
            long freed = 0;
            try
            {
                var dir = new DirectoryInfo(path);
                if (!dir.Exists) return 0;

                foreach (var file in dir.GetFiles())
                {
                    try { freed += file.Length; file.Delete(); } catch { }
                }
                foreach (var subDir in dir.GetDirectories())
                {
                    try { freed += CleanDirectory(subDir.FullName); subDir.Delete(true); } catch { }
                }
            }
            catch { }
            return freed;
        }
    }
}
