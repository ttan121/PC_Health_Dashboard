using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PCHealthDashboard.Helpers;
using PCHealthDashboard.ViewModels;

namespace PCHealthDashboard;

public partial class KittyWindow : Window
{
    private bool _isPinned = true;
    private MainViewModel? _vm;
    private bool _isDragging = false;
    public event EventHandler? ClosedOrHidden;

    // Cached status brushes to avoid allocations on every UI refresh
    private static readonly System.Windows.Media.SolidColorBrush GreenBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#4ade80")!);
    private static readonly System.Windows.Media.SolidColorBrush AmberBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#f59e0b")!);
    private static readonly System.Windows.Media.SolidColorBrush RedBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#ef4444")!);

    public KittyWindow()
    {
        InitializeComponent();

        // Apply Windows 11 DWM Acrylic Backdrop
        this.SourceInitialized += (s, e) => DwmBackdropHelper.ApplyBackdrop(this, BackdropType.Acrylic, enableDarkMode: true);

        // Position at bottom right
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 20;
        Top = workArea.Bottom - Height - 20;
    }

    public void TogglePopup(MainViewModel vm)
    {
        _vm = vm;
        if (IsVisible)
        {
            NetworkSparkline.IsPaused = true;
            Hide();
            vm.IsPopupVisible = false;
            ClosedOrHidden?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            this.DataContext = vm;
            NetworkSparkline.IsPaused = false;
            SyncAllValues(vm);
            Show();
            vm.IsPopupVisible = true;
        }
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
    }

    /// <summary>
    /// Copies every value from the ViewModel into the popup controls.
    /// Called from MainWindow on every poll tick to stay perfectly in sync.
    /// </summary>
    public void SyncAllValues(MainViewModel vm)
    {
        // Skip updates while the user is dragging the popup – this drops UI work during move
        if (_isDragging) return;

        HealthScoreText.Text = $"{vm.HealthScore} Healthy";

        // ── CPU ──
        float cpu = Math.Clamp(float.IsNaN(vm.CpuUsage) ? 0f : vm.CpuUsage, 0f, 100f);
        CpuText.Text = $"{cpu:F0}%";
        CpuTempText.Text = $" {(float.IsNaN(vm.CpuTemp) ? 0f : vm.CpuTemp):F0}°C";
        CpuFill.Width = new GridLength(cpu, GridUnitType.Star);
        CpuEmpty.Width = new GridLength(100f - cpu, GridUnitType.Star);

        // ── RAM ──
        float rawRamPct = vm.RamTotal > 0 ? (vm.RamUsed / vm.RamTotal) * 100f : 0f;
        float ramPct = Math.Clamp(float.IsNaN(rawRamPct) ? 0f : rawRamPct, 0f, 100f);
        RamText.Text = $"{ramPct:F0}%";
        RamFill.Width = new GridLength(ramPct, GridUnitType.Star);
        RamEmpty.Width = new GridLength(100f - ramPct, GridUnitType.Star);

        // ── Storage ──
        float rawStoragePct = vm.SsdTotalSpace > 0 ? (vm.SsdUsedSpace / vm.SsdTotalSpace) * 100f : 0f;
        float storagePct = Math.Clamp(float.IsNaN(rawStoragePct) ? 0f : rawStoragePct, 0f, 100f);
        SsdText.Text = $"{storagePct:F0}%";
        SsdFill.Width = new GridLength(storagePct, GridUnitType.Star);
        SsdEmpty.Width = new GridLength(100f - storagePct, GridUnitType.Star);

        // ── Network (Zero-Allocation SkiaSharp Sparkline) ──
        NetDownText.Text = $"{vm.DownloadMbps:F1} Mbps";
        NetUpText.Text = $"{vm.UploadMbps:F1} Mbps";
        
        if (vm.DownloadSpeedHistory.Count > 0 || vm.UploadSpeedHistory.Count > 0)
        {
            NetworkSparkline.UpdateData(vm.DownloadSpeedHistory, vm.UploadSpeedHistory);
        }
        else if (vm.NetworkSpeedHistory.Count > 0)
        {
            NetworkSparkline.UpdateData(vm.NetworkSpeedHistory);
        }

        // Force GPU binding update just in case
        if (GpuItemsControl != null)
        {
            if (GpuItemsControl.ItemsSource != vm.Gpus)
            {
                GpuItemsControl.ItemsSource = vm.Gpus;
            }
        }
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        _isPinned = !_isPinned;
        Topmost = _isPinned;
        PinButton.Content = _isPinned ? "📌" : "📍";
    }

    private void ClosePopup_Click(object sender, RoutedEventArgs e)
    {
        NetworkSparkline.IsPaused = true;
        Hide();
        if (_vm != null) _vm.IsPopupVisible = false;
        ClosedOrHidden?.Invoke(this, EventArgs.Empty);
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            _isDragging = true;
            try
            {
                DragMove();
            }
            finally
            {
                _isDragging = false;
            }
        }
    }

    private void Window_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (System.Windows.Application.Current != null && !System.Windows.Application.Current.Dispatcher.HasShutdownStarted)
        {
            e.Cancel = true;
            NetworkSparkline.IsPaused = true;
            Hide();
            if (_vm != null) _vm.IsPopupVisible = false;
            ClosedOrHidden?.Invoke(this, EventArgs.Empty);
            return;
        }
        base.OnClosing(e);
    }
}


