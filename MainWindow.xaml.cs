using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using PCHealthDashboard.Helpers;
using PCHealthDashboard.ViewModels;

namespace PCHealthDashboard;

public partial class MainWindow : Window
{
    private HotkeyHelper? _hotkeyHelper;
    private KittyWindow? _kittyWindow;
    private OsdWindow? _osdWindow;
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private EventHandler? _dataPolledHandler;

    public MainWindow()
    {
        InitializeComponent();
        
        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location),
            Visible = true,
            Text = "PC Health Dashboard"
        };
        
        var contextMenu = new System.Windows.Forms.ContextMenuStrip();
        
        var openDashboardItem = new System.Windows.Forms.ToolStripMenuItem("Dashboard");
        openDashboardItem.Click += (s, e) => ShowMainWindow();
        contextMenu.Items.Add(openDashboardItem);

        var widgetItem = new System.Windows.Forms.ToolStripMenuItem("Widget");
        widgetItem.Click += (s, e) => ToggleWidget();
        contextMenu.Items.Add(widgetItem);

        var compactModeItem = new System.Windows.Forms.ToolStripMenuItem("Compact Mode");
        compactModeItem.Click += (s, e) => ToggleCompactMode();
        contextMenu.Items.Add(compactModeItem);

        contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        var exitItem = new System.Windows.Forms.ToolStripMenuItem("Exit");
        exitItem.Click += (s, e) => System.Windows.Application.Current.Shutdown();
        contextMenu.Items.Add(exitItem);

        contextMenu.Opening += (s, e) =>
        {
            widgetItem.Checked = (_kittyWindow != null && _kittyWindow.IsVisible);
            compactModeItem.Checked = (_osdWindow != null && _osdWindow.IsVisible);
        };
        
        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (s, e) => ShowMainWindow();

        this.SourceInitialized += MainWindow_SourceInitialized;
        this.Closed += MainWindow_Closed;
    }

    public void ShowMainWindow()
    {
        this.Show();
        this.WindowState = WindowState.Normal;
        this.ShowInTaskbar = true;
        this.Activate();
        UpdateEfficiencyMode();
        if (this.DataContext is MainViewModel vm)
        {
            UpdateSparkline(vm);
        }
    }

    protected override void OnStateChanged(EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            this.Hide(); // Completely hide to suspend main window WPF rendering loop and taskbar icon
            UpdateEfficiencyMode();
        }
        else if (WindowState != WindowState.Minimized)
        {
            this.Show();
            this.ShowInTaskbar = true;
            UpdateEfficiencyMode();
            if (this.DataContext is MainViewModel vm)
            {
                UpdateSparkline(vm);
            }
        }
        base.OnStateChanged(e);
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        // Apply Windows 11 DWM Mica material & Dark Mode frame
        DwmBackdropHelper.ApplyBackdrop(this, BackdropType.Mica, enableDarkMode: true);

        var helper = new WindowInteropHelper(this);
        
        _hotkeyHelper = new HotkeyHelper(helper.Handle, () =>
        {
            // Popup Widget (Ctrl+Shift+Space)
            ToggleWidget();
        }, () => 
        {
            // Compact Mode (Ctrl+Shift+Alt+C)
            ToggleCompactMode();
        });

        // Initialize UI Subscription Pipeline
        if (this.DataContext is MainViewModel viewModel)
        {
            _dataPolledHandler = (s, ev) => OnDataPolled(viewModel);
            viewModel.DataPolled += _dataPolledHandler;
            UpdateEfficiencyMode();
        }
    }

    private void EnsureKittyWindow()
    {
        if (_kittyWindow == null)
        {
            _kittyWindow = new KittyWindow();
            _kittyWindow.ClosedOrHidden += (s, e) => UpdateEfficiencyMode();
        }
    }

    private void EnsureOsdWindow()
    {
        if (_osdWindow == null)
        {
            _osdWindow = new OsdWindow();
            _osdWindow.ClosedOrHidden += (s, e) => UpdateEfficiencyMode();
        }
    }

    public void ToggleWidget()
    {
        if (this.DataContext is MainViewModel vm)
        {
            EnsureKittyWindow();
            if (_kittyWindow!.IsVisible)
            {
                _kittyWindow.Hide();
                vm.IsPopupVisible = false;
            }
            else
            {
                _kittyWindow.DataContext = vm;
                _kittyWindow.SyncAllValues(vm);
                _kittyWindow.Show();
                vm.IsPopupVisible = true;
            }
            UpdateEfficiencyMode();
        }
    }

    public void ToggleCompactMode()
    {
        if (this.DataContext is MainViewModel vm)
        {
            EnsureOsdWindow();
            if (_osdWindow!.IsVisible)
            {
                _osdWindow.Hide();
                vm.IsCompactMode = false;
            }
            else
            {
                _osdWindow.DataContext = vm;
                _osdWindow.SyncValues(vm);
                _osdWindow.Show();
                vm.IsCompactMode = true;
            }
            UpdateEfficiencyMode();
        }
    }

    private void UpdateEfficiencyMode()
    {
        if (this.DataContext is MainViewModel vm)
        {
            bool isMainActive = this.Visibility == Visibility.Visible && this.WindowState != WindowState.Minimized;
            bool isKittyActive = _kittyWindow != null && _kittyWindow.IsVisible;
            bool isOsdActive = _osdWindow != null && _osdWindow.IsVisible;

            bool isAnyUiActive = isMainActive || isKittyActive || isOsdActive;
            vm.IsEfficiencyMode = !isAnyUiActive;

            NetworkSparkline.IsPaused = !isMainActive;
            if (_kittyWindow != null)
            {
                _kittyWindow.NetworkSparkline.IsPaused = !isKittyActive;
            }
        }
    }

    private void OnDataPolled(MainViewModel viewModel)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(() => OnDataPolled(viewModel));
            return;
        }

        // Update High-Performance SkiaSharp Sparkline if main window is visible
        if (this.Visibility == Visibility.Visible && this.WindowState != WindowState.Minimized)
        {
            UpdateSparkline(viewModel);
        }

        if (_kittyWindow != null && _kittyWindow.IsVisible)
        {
            _kittyWindow.SyncAllValues(viewModel);
        }

        if (_osdWindow != null && _osdWindow.IsVisible)
        {
            _osdWindow.SyncValues(viewModel);
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        if (this.DataContext is MainViewModel vm && _dataPolledHandler != null)
        {
            vm.DataPolled -= _dataPolledHandler;
        }

        _notifyIcon?.Dispose();
        _hotkeyHelper?.Dispose();
        _kittyWindow?.Close();
        _osdWindow?.Close();
    }

    /// <summary>
    /// Renders live telemetry into the zero-allocation SkiaSharp sparkline.
    /// Eliminates WPF PointCollection heap churn entirely.
    /// </summary>
    private void UpdateSparkline(MainViewModel vm)
    {
        if (vm.DownloadSpeedHistory.Count > 0 || vm.UploadSpeedHistory.Count > 0)
        {
            NetworkSparkline.UpdateData(vm.DownloadSpeedHistory, vm.UploadSpeedHistory);
        }
        else if (vm.NetworkSpeedHistory.Count > 0)
        {
            NetworkSparkline.UpdateData(vm.NetworkSpeedHistory);
        }
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            this.DragMove();
        }
    }

    private void CompactWidgetButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleCompactMode();
        if (_osdWindow != null && _osdWindow.IsVisible)
        {
            this.WindowState = WindowState.Minimized;
        }
    }

    private void CustomizeButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleWidget();
        if (_kittyWindow != null && _kittyWindow.IsVisible)
        {
            this.WindowState = WindowState.Minimized;
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        this.WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Application.Current.Shutdown();
    }
}
