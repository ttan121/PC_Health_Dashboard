using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PCHealthDashboard.ViewModels;

namespace PCHealthDashboard;

public partial class KittyWindow : Window
{
    private bool _isPinned = true;

    private MainViewModel? _vm;
    private bool _isDragging = false;
    // Cached status brushes to avoid allocations on every UI refresh
    private static readonly System.Windows.Media.SolidColorBrush GreenBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#4ade80")!);
    private static readonly System.Windows.Media.SolidColorBrush AmberBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#f59e0b")!);
    private static readonly System.Windows.Media.SolidColorBrush RedBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#ef4444")!);

    public KittyWindow()
    {
        InitializeComponent();

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
            Hide();
            vm.IsPopupVisible = false;
        }
        else
        {
            SyncAllValues(vm);
            Show();
            vm.IsPopupVisible = true;
        }
    }

    // Toggle compact mode via context menu or hotkey

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
        CpuText.Text = $"{vm.CpuUsage:F0}%";
        CpuTempText.Text = $"{vm.CpuTemp:F0}°C";
        CpuFill.Width = new GridLength(vm.CpuUsage, GridUnitType.Star);
        CpuEmpty.Width = new GridLength(Math.Max(0, 100 - vm.CpuUsage), GridUnitType.Star);

        // ── GPU ──
        GpuText.Text = $"{vm.GpuUsage:F0}%";
        GpuTempText.Text = $"{vm.GpuTemp:F0}°C";
        GpuVramText.Text = $"VRAM {vm.GpuVram:F1} / 8 GB";
        GpuFill.Width = new GridLength(vm.GpuUsage, GridUnitType.Star);
        GpuEmpty.Width = new GridLength(Math.Max(0, 100 - vm.GpuUsage), GridUnitType.Star);

        // ── RAM ──
        float ramPct = vm.RamTotal > 0 ? (vm.RamUsed / vm.RamTotal) * 100f : 0;
        RamText.Text = $"{ramPct:F0}%";
        RamFill.Width = new GridLength(ramPct, GridUnitType.Star);
        RamEmpty.Width = new GridLength(Math.Max(0, 100 - ramPct), GridUnitType.Star);

        // ── Storage ──
        float storagePct = vm.SsdTotalSpace > 0 ? (vm.SsdUsedSpace / vm.SsdTotalSpace) * 100f : 0;
        SsdText.Text = $"{storagePct:F0}%";
        SsdFill.Width = new GridLength(storagePct, GridUnitType.Star);
        SsdEmpty.Width = new GridLength(Math.Max(0, 100 - storagePct), GridUnitType.Star);

        // ── Network ──
        NetDownText.Text = $"{vm.DownloadMbps:F1} Mbps";
        NetUpText.Text = $"{vm.UploadMbps:F1} Mbps";
        
        DrawSparkline(vm.NetworkSpeedHistory);
    }

    private void DrawSparkline(ObservableCollection<double> source)
    {
        if (source.Count == 0) return;
        
        double width = 300; // Fixed fallback width for simplicity
        double height = 32;

        var points = new PointCollection();
        double max = 1;
        foreach (var v in source) if (v > max) max = v;
        
        double stepX = width / Math.Max(1, source.Count - 1);
        
        for (int i = 0; i < source.Count; i++)
        {
            double val = source[i];
            double x = i * stepX;
            double y = height - ((val / max) * height);
            points.Add(new System.Windows.Point(x, y));
        }
        
        NetworkSparkline.Points = points;
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        _isPinned = !_isPinned;
        Topmost = _isPinned;
        PinButton.Content = _isPinned ? "📌" : "📍";
    }

    private void ClosePopup_Click(object sender, RoutedEventArgs e)
    {
        Hide();
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
}
