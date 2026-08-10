using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameTracker.Services
{
    /// <summary>
    /// Hidden developer mode: "GameTracker.exe --capture-docs &lt;outDir&gt;" renders every
    /// window to PNGs for the built-in How-to guide, then exits. Windows are shown
    /// off-screen and sensitive fields are replaced with placeholders.
    /// </summary>
    public static class DocsCapture
    {
        public static string? OutDirFromArgs(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i].Equals("--capture-docs", StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }

        public static async Task RunAsync(string outDir)
        {
            Directory.CreateDirectory(outDir);

            await CaptureAsync(outDir, "settings-appearance.png", () => new Views.SettingsWindow());
            await CaptureAsync(outDir, "settings-chat.png", () =>
            {
                var w = new Views.SettingsWindow();
                w.ShowPanelForDocs("chat");
                return w;
            });
            await CaptureAsync(outDir, "settings-overlay.png", () =>
            {
                var w = new Views.SettingsWindow();
                w.ShowPanelForDocs("overlay");
                return w;
            });
            await CaptureAsync(outDir, "settings-backup.png", () =>
            {
                var w = new Views.SettingsWindow();
                w.ShowPanelForDocs("backup");
                return w;
            });
            await CaptureAsync(outDir, "features.png", () => new Views.ChatFeaturesWindow());
            await CaptureAsync(outDir, "soundalerts.png", () => new Views.SoundAlertsWindow());
            await CaptureAsync(outDir, "voicelab.png", () => new Views.VoiceLabWindow());
            await CaptureAsync(outDir, "voicemorph.png", () => new Views.VoiceMorphWindow());
            await CaptureAsync(outDir, "textpanels.png", () => new Views.TextPanelsWindow());
            await CaptureAsync(outDir, "goals.png", () => new Views.GoalsWindow());
            await CaptureAsync(outDir, "poll.png", () => new Views.PollWindow());
            await CaptureAsync(outDir, "chatters.png", () => new Views.ChattersWindow());
            await CaptureAsync(outDir, "stats.png", () => new Views.StreamStatsWindow());
            await CaptureAsync(outDir, "ssnguide.png", () => new Views.SsnGuideWindow());
            await CaptureAsync(outDir, "chat.png", () =>
            {
                var w = new Views.ChatWindow();
                w.SanitizeForDocs();   // placeholder channel/session values
                return w;
            });
        }

        private static async Task CaptureAsync(string outDir, string file, Func<Window> make)
        {
            Window? w = null;
            try
            {
                w = make();
                w.WindowStartupLocation = WindowStartupLocation.Manual;
                w.Left = -20000;                  // render fully, just not on screen
                w.Top = 0;
                w.ShowActivated = false;
                w.ShowInTaskbar = false;
                w.Show();
                w.UpdateLayout();
                await Task.Delay(350);            // let async content settle
                w.UpdateLayout();

                int width = (int)Math.Ceiling(w.ActualWidth);
                int height = (int)Math.Ceiling(w.ActualHeight);
                if (width < 10 || height < 10) return;

                var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(w);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                using var fs = File.Create(Path.Combine(outDir, file));
                enc.Save(fs);
            }
            catch { /* keep capturing the rest */ }
            finally
            {
                try { w?.Close(); } catch { }
            }
        }
    }
}
