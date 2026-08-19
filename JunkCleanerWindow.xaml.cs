using System.Windows;

namespace PCHealthDashboard
{
    public partial class JunkCleanerWindow : Window
    {
        public JunkCleanerWindow()
        {
            InitializeComponent();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
