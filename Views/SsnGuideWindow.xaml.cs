using System.Diagnostics;
using System.Windows;

namespace GameTracker.Views
{
    /// <summary>Steps for enabling "Send chat messages to API server" in Social Stream Ninja.</summary>
    public partial class SsnGuideWindow : Window
    {
        public SsnGuideWindow() => InitializeComponent();

        private void OpenSsn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://socialstream.ninja/") { UseShellExecute = true });
            }
            catch { /* ignore */ }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
