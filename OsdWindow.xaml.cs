using System;
using System.Windows;
using System.Windows.Input;
using PCHealthDashboard.ViewModels;

namespace PCHealthDashboard
{
    public partial class OsdWindow : Window
    {
        private MainViewModel? _vm;
        private bool _isDragging = false;

        public OsdWindow()
        {
            InitializeComponent();
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
            OsdCpu.Text = $"{vm.CpuUsage:F0}% ({vm.CpuTemp:F0}°C)";
            OsdGpu.Text = $"{vm.GpuUsage:F0}% ({vm.GpuTemp:F0}°C)";
            OsdRam.Text = $"{ramPct:F0}%";
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _isDragging = true;
                this.DragMove();
                _isDragging = false;
            }
        }

        private void CloseCompactMode_Click(object sender, RoutedEventArgs e)
        {
            if (_vm != null)
            {
                _vm.IsPopupVisible = false;
            }
            this.Hide();
        }
    }
}
