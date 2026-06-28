using System.Windows;

namespace GameTracker.Views
{
    /// <summary>Small splash shown while a silent auto-update downloads and installs.</summary>
    public partial class UpdatingWindow : Window
    {
        public UpdatingWindow()
        {
            InitializeComponent();
        }

        public void SetStatus(string text) => StatusText.Text = text;

        public void SetProgress(double percent)
        {
            Bar.IsIndeterminate = false;
            Bar.Value = percent;
        }

        public void SetIndeterminate(string text)
        {
            Bar.IsIndeterminate = true;
            StatusText.Text = text;
        }
    }
}
