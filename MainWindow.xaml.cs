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
        
        var compactModeItem = new System.Windows.Forms.ToolStripMenuItem("Compact Mode");
        compactModeItem.CheckOnClick = true;
        compactModeItem.CheckedChanged += (s, e) => 
        {
            if (this.DataContext is MainViewModel vm)
            {
                vm.IsCompactMode = compactModeItem.Checked;
            }
        };
        contextMenu.Items.Add(compactModeItem);
        contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        var exitItem = new System.Windows.Forms.ToolStripMenuItem("Exit");
        exitItem.Click += (s, e) => System.Windows.Application.Current.Shutdown();
        contextMenu.Items.Add(exitItem);
        
        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (s, e) => 
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.ShowInTaskbar = true;
        };

        this.SourceInitialized += MainWindow_SourceInitialized;
        this.Closed += MainWindow_Closed;
    }

    protected override void OnStateChanged(EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            this.Hide(); // Completely hide to suspend WPF rendering loop and taskbar icon
            if (this.DataContext is MainViewModel vm)
            {
                vm.IsEfficiencyMode = true; // Auto Cryo Mode
            }
        }
        else if (WindowState == WindowState.Normal)
        {
            if (this.DataContext is MainViewModel vm)
            {
                vm.IsEfficiencyMode = false; // Wake up
            }
        }
        base.OnStateChanged(e);
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        
        _hotkeyHelper = new HotkeyHelper(helper.Handle, () =>
        {
            // Popup Mode (Ctrl+Shift+Space)
            if (this.DataContext is MainViewModel vm)
            {
                vm.IsCompactMode = false;
                if (_osdWindow != null && _osdWindow.IsVisible) _osdWindow.Hide();
                
                if (_kittyWindow == null) _kittyWindow = new KittyWindow();
                
                if (_kittyWindow.IsVisible)
                {
                    _kittyWindow.Hide();
                    vm.IsPopupVisible = false;
                }
                else
                {
                    _kittyWindow.SyncAllValues(vm);
                    _kittyWindow.Show();
                    vm.IsPopupVisible = true;
                }
            }
        }, () => 
        {
            // Compact Mode (Ctrl+Shift+Alt+C)
            if (this.DataContext is MainViewModel vm)
            {
                vm.IsCompactMode = true;
                if (_kittyWindow != null && _kittyWindow.IsVisible) _kittyWindow.Hide();

                if (_osdWindow == null) _osdWindow = new OsdWindow();
                
                if (_osdWindow.IsVisible)
                {
                    _osdWindow.Hide();
                    vm.IsPopupVisible = false;
                }
                else
                {
                    _osdWindow.SyncValues(vm);
                    _osdWindow.Show();
                    vm.IsPopupVisible = true;
                }
            }
        });

        // Hook into the ViewModel's poll timer to keep popup in sync
        if (this.DataContext is MainViewModel viewModel)
        {
            viewModel.DataPolled += (s, ev) =>
            {
                // Update Network Sparkline if window is visible
                if (this.Visibility == Visibility.Visible)
                {
                    UpdateSparkline(viewModel);
                }

                if (viewModel.IsPopupVisible)
                {
                    if (viewModel.IsCompactMode && _osdWindow != null)
                    {
                        _osdWindow.SyncValues(viewModel);
                    }
                    else if (!viewModel.IsCompactMode && _kittyWindow != null)
                    {
                        _kittyWindow.SyncAllValues(viewModel);
                    }
                }
            };
            
            // Watch for mode switch while visible
            viewModel.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.IsCompactMode) && viewModel.IsPopupVisible)
                {
                    // User toggled mode while popup is open via Context Menu
                    if (viewModel.IsCompactMode)
                    {
                        _kittyWindow?.Hide();
                        if (_osdWindow == null) _osdWindow = new OsdWindow();
                        _osdWindow.SyncValues(viewModel);
                        _osdWindow.Show();
                    }
                    else
                    {
                        _osdWindow?.Hide();
                        if (_kittyWindow == null) _kittyWindow = new KittyWindow();
                        _kittyWindow.TogglePopup(viewModel);
                        _kittyWindow.Show();
                    }
                }
            };
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _notifyIcon?.Dispose();
        _hotkeyHelper?.Dispose();
        _kittyWindow?.Close();
        _osdWindow?.Close();
    }

    private void UpdateSparkline(MainViewModel vm)
    {
        var history = vm.NetworkSpeedHistory;
        if (history.Count < 2) return;

        var points = new System.Windows.Media.PointCollection();
        double width = 700; // Approximate width of the sparkline area
        double height = 40;
        
        // Find max to scale
        double maxVal = 1; // avoid div by 0
        foreach (var val in history)
        {
            if (val > maxVal) maxVal = val;
        }

        double stepX = width / Math.Max(1, history.Count - 1);
        
        for (int i = 0; i < history.Count; i++)
        {
            double x = i * stepX;
            // Invert Y since 0 is top in WPF
            double y = height - ((history[i] / maxVal) * height);
            points.Add(new System.Windows.Point(x, y));
        }
        
        NetworkSparkline.Points = points;
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
        if (this.DataContext is MainViewModel vm)
        {
            vm.IsCompactMode = true; // Set mode
            
            // Close large widget if open
            if (_kittyWindow != null && _kittyWindow.IsVisible)
            {
                _kittyWindow.Hide();
            }

            // Toggle mini widget
            if (_osdWindow == null) _osdWindow = new OsdWindow();
            
            if (_osdWindow.IsVisible)
            {
                _osdWindow.Hide();
                vm.IsPopupVisible = false;
            }
            else
            {
                _osdWindow.DataContext = vm;
                _osdWindow.SyncValues(vm);
                _osdWindow.Show();
                vm.IsPopupVisible = true;
                this.WindowState = WindowState.Minimized;
            }
        }
    }

    private void CustomizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (this.DataContext is MainViewModel vm)
        {
            vm.IsCompactMode = false; // Set mode to large widget
            
            // Close mini widget if open
            if (_osdWindow != null && _osdWindow.IsVisible)
            {
                _osdWindow.Hide();
            }

            if (_kittyWindow == null) _kittyWindow = new KittyWindow();
            
            if (_kittyWindow.IsVisible)
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
                this.WindowState = WindowState.Minimized;
            }
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
