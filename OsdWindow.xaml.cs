using PCHealthDashboard.ViewModels;
using System.Windows;
using System.Windows.Input;

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

        public void ToggleOsd(MainViewModel vm)
        {
            if (IsVisible) Hide();
            else 
            {
                this.DataContext = vm;
                SyncValues(vm);
                Show();
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
                var parts = new System.Collections.Generic.List<string>();
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
                this.DragMove();
                _isDragging = false;
            }
        }

        private void CloseCompactMode_Click(object sender, RoutedEventArgs e)
        {
            if (_vm != null)
            {
                _vm.IsCompactMode = false;
                _vm.IsPopupVisible = true;
            }
        }
    }
}
