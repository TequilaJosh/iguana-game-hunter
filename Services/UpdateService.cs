using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json.Linq;

namespace GameTracker.Services
{
    /// <summary>
    /// Checks the GitHub Releases API for a newer version and, if the user agrees,
    /// downloads the installer asset and launches it.
    /// </summary>
    public static class UpdateService
    {
        private const string Owner = "TequilaJosh";
        private const string Repo = "iguana-game-hunter";

        private static readonly string ReleasesListApi =
            $"https://api.github.com/repos/{Owner}/{Repo}/releases?per_page=30";
        private static readonly string ReleasesPage =
            $"https://github.com/{Owner}/{Repo}/releases/latest";

        /// <summary>Invoked just before the app restarts to apply an update — lets the app
        /// record which windows are open so they can reopen afterward.</summary>
        public static Action? CaptureStateBeforeRestart;

        /// <param name="silent">
        /// When true (startup check), stays quiet if already up to date or on error.
        /// When false (manual check), reports those outcomes to the user.
        /// </param>
        public static async Task CheckForUpdatesAsync(bool silent = true)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("GameTracker-Updater");
                http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

                var json = await http.GetStringAsync(ReleasesListApi);
                var current = CurrentVersion();

                // Parse the release list (skip drafts/pre-releases), newest first.
                var releases = new List<(Version ver, string tag, string body, JObject obj)>();
                foreach (var r in JArray.Parse(json).OfType<JObject>())
                {
                    if ((bool?)r["draft"] == true || (bool?)r["prerelease"] == true) continue;
                    var t = (string?)r["tag_name"] ?? string.Empty;
                    var v = ParseVersion(t);
                    if (v != null) releases.Add((v, t, ((string?)r["body"] ?? string.Empty).Trim(), r));
                }
                releases.Sort((a, b) => b.ver.CompareTo(a.ver));

                if (releases.Count == 0 || releases[0].ver <= current)
                {
                    if (!silent)
                        MessageBox.Show($"You're on the latest version (v{current.ToString(3)}).",
                            "LazerGuanas Game Hunter", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var latestRel = releases[0];
                var release = latestRel.obj;

                // Everything the user hasn't seen yet: notes for each version above theirs.
                var entries = releases
                    .Where(x => x.ver > current)
                    .Select(x => (x.tag, x.body))
                    .ToList();

                bool doUpdate = false;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var dlg = new Views.UpdateAvailableWindow(current.ToString(3), latestRel.tag, entries);
                    var owner = Application.Current.MainWindow;
                    if (owner != null && owner.IsLoaded && owner.IsVisible) dlg.Owner = owner;
                    doUpdate = dlg.ShowDialog() == true;
                });
                if (!doUpdate) return;

                var asset = ((JArray?)release["assets"] ?? new JArray())
                    .FirstOrDefault(a => ((string?)a["name"] ?? "")
                        .EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

                var downloadUrl = (string?)asset?["browser_download_url"];
                var assetName = (string?)asset?["name"];

                if (downloadUrl == null || assetName == null)
                {
                    // No installer attached — just open the releases page in the browser.
                    OpenInBrowser(ReleasesPage);
                    return;
                }

                var destination = Path.Combine(Path.GetTempPath(), assetName);

                var splash = new Views.UpdatingWindow();
                splash.Show();
                try
                {
                    using (var response = await http.GetAsync(downloadUrl,
                               HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();
                        var total = response.Content.Headers.ContentLength ?? -1;

                        await using var input = await response.Content.ReadAsStreamAsync();
                        await using var fs = File.Create(destination);

                        var buffer = new byte[81920];
                        long readSoFar = 0;
                        int read;
                        if (total <= 0) splash.SetIndeterminate("Downloading the update…");
                        while ((read = await input.ReadAsync(buffer)) > 0)
                        {
                            await fs.WriteAsync(buffer.AsMemory(0, read));
                            readSoFar += read;
                            if (total > 0)
                            {
                                splash.SetProgress((double)readSoFar / total * 100);
                                splash.SetStatus(
                                    $"Downloading the update…  {readSoFar / 1048576} / {total / 1048576} MB");
                            }
                        }
                    }

                    splash.SetIndeterminate("Installing… the app will reopen automatically.");
                    await Task.Delay(500); // let the message render

                    // Remember which windows are open so they reopen after the restart.
                    try { CaptureStateBeforeRestart?.Invoke(); } catch { /* best-effort */ }

                    // Run the installer silently (no wizard), then exit so it can replace the
                    // running files. The installer relaunches the app when it finishes.
                    Process.Start(new ProcessStartInfo(destination)
                    {
                        UseShellExecute = true,
                        Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART",
                    });
                    Application.Current.Shutdown();
                }
                catch
                {
                    splash.Close();
                    throw;
                }
            }
            catch (Exception ex)
            {
                if (!silent)
                    MessageBox.Show($"Couldn't check for updates:\n{ex.Message}",
                        "Update", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static Version CurrentVersion()
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }

        private static Version? ParseVersion(string tag)
        {
            // Accept tags like "v1.2.3", "1.2.3", or "v1.2.3-beta" (pre-release suffix ignored).
            var cleaned = tag.TrimStart('v', 'V').Trim().Split('-', '+')[0];
            if (!Version.TryParse(cleaned, out var v)) return null;
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }

        private static void OpenInBrowser(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { /* ignore */ }
        }
    }
}
