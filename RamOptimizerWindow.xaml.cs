using System.Windows;
using PCHealthDashboard.ViewModels;

namespace PCHealthDashboard;

public partial class RamOptimizerWindow : Window
{
    public RamOptimizerWindow()
    {
        InitializeComponent();
        DataContext = new RamOptimizerViewModel();
    }
}
