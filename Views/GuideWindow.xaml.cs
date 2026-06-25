using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace GameTracker.Views
{
    /// <summary>
    /// Themed, pinnable window that embeds GameFAQs so the user can find and read
    /// walkthroughs for a game. Remembers the last page and scroll position per game
    /// so reopening returns to where they left off.
    /// </summary>
    public partial class GuideWindow : Window
    {
        private const string GameFaqsBase = "https://gamefaqs.gamespot.com";

        private readonly string _title;
        private readonly string _savedUrl;
        private readonly double _savedScroll;
        private readonly Action<string, double> _onState;

        private readonly DispatcherTimer _scrollTimer;
        private string _currentUrl = string.Empty;
        private double _currentScroll;
        private bool _restorePending;
        private bool _pinned;

        public GuideWindow(string gameTitle, string? savedUrl, double savedScroll,
                           Action<string, double> onState)
        {
            InitializeComponent();

            _title = gameTitle ?? string.Empty;
            _savedUrl = savedUrl ?? string.Empty;
            _savedScroll = savedScroll;
            _onState = onState;
            _restorePending = !string.IsNullOrWhiteSpace(_savedUrl);

            TitleBarText.Text = string.IsNullOrWhiteSpace(_title)
                ? "GAME GUIDES"
                : $"GUIDES — {_title.ToUpperInvariant()}";
            SearchBox.Text = _title;

            // Poll the scroll position while reading so we can save where they are.
            _scrollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _scrollTimer.Tick += async (_, _) => await CaptureScrollAsync();

            Loaded += async (_, _) => await InitAsync();
        }

        private async Task InitAsync()
        {
            try
            {
                // Keep the browser's data in a writable per-user folder (the install
                // dir may be read-only).
                var dataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "LazerGuanas Game Hunter", "WebView2");
                Directory.CreateDirectory(dataFolder);

                var env = await CoreWebView2Environment.CreateAsync(null, dataFolder);
                await Web.EnsureCoreWebView2Async(env);
                Web.CoreWebView2.NavigationCompleted += OnNavigationCompleted;

                // Resume the saved guide page if there is one, else search the title.
                Navigate(_restorePending ? _savedUrl : BuildSearchUrl(_title));
                _scrollTimer.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Couldn't start the in-app browser (WebView2).\n\n" + ex.Message +
                    "\n\nIf this keeps happening, install the Microsoft Edge WebView2 Runtime.",
                    "Game Guides", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            _currentUrl = Web.Source?.ToString() ?? _currentUrl;
            _currentScroll = 0;

            // Restore scroll once, on the initial resume navigation.
            if (_restorePending)
            {
                _restorePending = false;
                if (e.IsSuccess && _savedScroll > 1)
                {
                    await Task.Delay(350); // let the page lay out first
                    try
                    {
                        await Web.CoreWebView2.ExecuteScriptAsync(
                            "window.scrollTo(0, " +
                            _savedScroll.ToString(CultureInfo.InvariantCulture) + ")");
                        _currentScroll = _savedScroll;
                    }
                    catch { /* ignore */ }
                }
            }

            SaveState();
        }

        private async Task CaptureScrollAsync()
        {
            if (Web.CoreWebView2 == null) return;
            try
            {
                var s = await Web.CoreWebView2.ExecuteScriptAsync("window.scrollY");
                if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var y))
                    _currentScroll = y;
            }
            catch { /* page not ready */ }
        }

        private void SaveState()
        {
            if (!string.IsNullOrEmpty(_currentUrl))
                _onState?.Invoke(_currentUrl, _currentScroll);
        }

        private static string BuildSearchUrl(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return GameFaqsBase + "/";
            return GameFaqsBase + "/search?game=" + Uri.EscapeDataString(query.Trim());
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

        protected override void OnClosed(EventArgs e)
        {
            _scrollTimer.Stop();
            // _currentScroll is kept fresh by the 2s poll; persist the last known spot.
            SaveState();
            base.OnClosed(e);
        }
    }
}
