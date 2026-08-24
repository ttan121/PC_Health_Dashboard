using PCHealthDashboard.ViewModels;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

namespace PCHealthDashboard
{
    public partial class OsdWindow : Window
    {
        private MainViewModel? _vm;
        private bool _isDragging = false;
        public event EventHandler? ClosedOrHidden;

        public OsdWindow()
        {
            InitializeComponent();

            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - 320;
            Top = workArea.Bottom - 60;
        }

        public void ToggleOsd(MainViewModel vm)
        {
            _vm = vm;
            if (IsVisible)
            {
                Hide();
                vm.IsCompactMode = false;
                ClosedOrHidden?.Invoke(this, EventArgs.Empty);
            }
            else 
            {
                this.DataContext = vm;
                SyncValues(vm);
                Show();
                vm.IsCompactMode = true;
            }
        }

        public void SyncValues(MainViewModel vm)
        {
            _vm = vm;
            if (this.DataContext != vm)
            {
                this.DataContext = vm;
            }
            if (_isDragging) return;

            float ramPct = vm.RamTotal > 0 ? (vm.RamUsed / vm.RamTotal) * 100f : 0;
            OsdCpu.Text = $"{System.Math.Round(vm.CpuUsage)}% ({System.Math.Round(vm.CpuTemp)}°C)";
            OsdRam.Text = $"{System.Math.Round(ramPct)}%";
            
            if (vm.Gpus.Count == 0)
            {
                OsdGpu.Text = "N/A";
            }
            else if (vm.Gpus.Count == 1)
            {
                OsdGpu.Text = $"{System.Math.Round(vm.Gpus[0].Usage)}%";
            }
            else
            {
                var parts = new List<string>();
                foreach(var g in vm.Gpus)
                {
                    string prefix = g.IsSharedMemory ? "iGPU" : "dGPU";
                    parts.Add($"{prefix} {System.Math.Round(g.Usage)}%");
                }
                OsdGpu.Text = string.Join(" ", parts);
            }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _isDragging = true;
                try
                {
                    this.DragMove();
                }
                finally
                {
                    _isDragging = false;
                }
            }
        }

        private void CloseCompactMode_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            if (_vm != null)
            {
                _vm.IsCompactMode = false;
            }
            ClosedOrHidden?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (System.Windows.Application.Current != null && !System.Windows.Application.Current.Dispatcher.HasShutdownStarted)
            {
                e.Cancel = true;
                Hide();
                if (_vm != null)
                {
                    _vm.IsCompactMode = false;
                }
                ClosedOrHidden?.Invoke(this, EventArgs.Empty);
                return;
            }
            base.OnClosing(e);
        }
    }
}
