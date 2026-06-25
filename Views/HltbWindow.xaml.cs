using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;

namespace GameTracker.Views
{
    /// <summary>
    /// Themed, pinnable window that embeds HowLongToBeat to show completion-time
    /// estimates (Main Story / Main+Extras / Completionist) for a game.
    /// </summary>
    public partial class HltbWindow : Window
    {
        private const string HltbBase = "https://howlongtobeat.com";

        private readonly string _title;
        private bool _pinned;

        public HltbWindow(string gameTitle)
        {
            InitializeComponent();

            _title = gameTitle ?? string.Empty;
            TitleBarText.Text = string.IsNullOrWhiteSpace(_title)
                ? "HOW LONG TO BEAT"
                : $"HOW LONG TO BEAT — {_title.ToUpperInvariant()}";
            SearchBox.Text = _title;

            Loaded += async (_, _) => await InitAsync();
        }

        private async Task InitAsync()
        {
            try
            {
                var dataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "LazerGuanas Game Hunter", "WebView2");
                Directory.CreateDirectory(dataFolder);

                var env = await CoreWebView2Environment.CreateAsync(null, dataFolder);
                await Web.EnsureCoreWebView2Async(env);

                Navigate(BuildSearchUrl(_title));
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Couldn't start the in-app browser (WebView2).\n\n" + ex.Message +
                    "\n\nIf this keeps happening, install the Microsoft Edge WebView2 Runtime.",
                    "How Long To Beat", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static string BuildSearchUrl(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return HltbBase + "/";
            return HltbBase + "/?q=" + Uri.EscapeDataString(query.Trim());
        }

        private void Navigate(string url)
        {
            if (Web.CoreWebView2 != null)
                Web.CoreWebView2.Navigate(url);
            else
                Web.Source = new Uri(url);
        }

        private void Search_Click(object sender, RoutedEventArgs e) =>
            Navigate(BuildSearchUrl(SearchBox.Text));

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                Navigate(BuildSearchUrl(SearchBox.Text));
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (Web.CoreWebView2 != null && Web.CoreWebView2.CanGoBack)
                Web.CoreWebView2.GoBack();
        }

        private void OpenBrowser_Click(object sender, RoutedEventArgs e)
        {
            var url = Web.Source?.ToString();
            if (string.IsNullOrEmpty(url)) return;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { /* ignore */ }
        }

        private void Pin_Click(object sender, RoutedEventArgs e)
        {
            _pinned = !_pinned;
            Topmost = _pinned;
            PinButton.Foreground = _pinned
                ? new SolidColorBrush(Color.FromRgb(0xd4, 0xa4, 0x37))
                : new SolidColorBrush(Color.FromRgb(0x7a, 0x90, 0x70));
            PinButton.ToolTip = _pinned ? "Unpin" : "Pin on top";
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
