using System;
using System.Threading;
using System.Windows;

namespace GameTracker
{
    public partial class App : Application
    {
        // Per-user single-instance guard. Without this, launching the app again
        // stacks up extra processes — and each tries (and fails) to bind the overlay
        // port. Instead, a second launch tells the running instance to come forward
        // and then exits, so PIDs/ports never accumulate.
        private const string MutexName = "Local\\LazerGuanasGameHunter.SingleInstance";
        private const string ShowEventName = "Local\\LazerGuanasGameHunter.Show";

        private Mutex? _instanceMutex;
        private EventWaitHandle? _showEvent;
        private RegisteredWaitHandle? _showRegistration;
        private bool _isPrimary;

        private const string ReminderMutexName = "Local\\LazerGuanasGameHunter.Reminders";
        private Mutex? _reminderMutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Woken by the scheduled task to show game reminders while the app is closed.
            if (Array.Exists(e.Args, a => string.Equals(a, Services.ReminderService.WakeArg, StringComparison.OrdinalIgnoreCase)))
            {
                RunReminderMode(e);
                return;
            }

            _instanceMutex = new Mutex(initiallyOwned: true, MutexName, out _isPrimary);

            if (!_isPrimary)
            {
                // Already running: nudge the live instance to surface, then bow out.
                try
                {
                    if (EventWaitHandle.TryOpenExisting(ShowEventName, out var existing))
                    {
                        existing.Set();
                        existing.Dispose();
                    }
                }
                catch { /* best-effort */ }
                Shutdown();
                return;
            }

            // A closed-app reminder pop-up may be on screen: hand off to us (we show reminders on
            // our own timer) and give it a moment to exit before the library is loaded.
            try
            {
                if (EventWaitHandle.TryOpenExisting(Views.ReminderRunner.HandoffEventName, out var handoff))
                {
                    handoff.Set();
                    handoff.Dispose();
                    for (int i = 0; i < 20 && Mutex.TryOpenExisting(ReminderMutexName, out var m); i++)
                    {
                        m.Dispose();
                        Thread.Sleep(100);
                    }
                }
            }
            catch { /* best-effort */ }

            // Primary instance: listen for "show" pings from any later launches.
            try
            {
                _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
                _showRegistration = ThreadPool.RegisterWaitForSingleObject(
                    _showEvent, (_, _) => BringToFront(), null, Timeout.Infinite, executeOnlyOnce: false);
            }
            catch { /* focusing the existing window is best-effort */ }

            base.OnStartup(e);

            // One-time settings migrations, then the saved colour theme.
            try { Services.SettingsService.RunMigrations(); } catch { /* keep whatever's saved */ }
            try { Services.ThemeService.Initialize(); } catch { /* fall back to XAML defaults */ }

            // Hidden docs mode: render window screenshots for the How-to guide, then exit.
            var docsDir = Services.DocsCapture.OutDirFromArgs(e.Args);
            if (docsDir != null)
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                Dispatcher.InvokeAsync(async () =>
                {
                    try { await Services.DocsCapture.RunAsync(docsDir); } catch { }
                    Shutdown();
                });
                return;
            }

            // Start the live mic morph chain if the streamer has it enabled.
            try { Services.VoiceMorphService.Start(); } catch { /* engine is optional */ }

            var win = new MainWindow();
            MainWindow = win;
            win.Show();
        }

        private void BringToFront()
        {
            Dispatcher.Invoke(() =>
            {
                var w = MainWindow;
                if (w == null) return;
                if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
                w.Show();
                w.Activate();
                // Brief topmost flip to pull it above other windows, then release.
                w.Topmost = true;
                w.Topmost = false;
                w.Focus();
            });
        }

        /// <summary>
        /// "--reminders": show only the due reminder pop-ups, then exit. If Game Hunter itself is
        /// running it does nothing (the app shows reminders on its own timer), and it never takes
        /// the app's single-instance guard — so opening Game Hunter meanwhile still works.
        /// </summary>
        private void RunReminderMode(StartupEventArgs e)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            if (Mutex.TryOpenExisting(MutexName, out var running)) { running.Dispose(); Shutdown(); return; }

            _reminderMutex = new Mutex(initiallyOwned: true, ReminderMutexName, out bool first);
            if (!first) { _reminderMutex.Dispose(); _reminderMutex = null; Shutdown(); return; }

            base.OnStartup(e);
            try { Services.ThemeService.Initialize(); } catch { /* fall back to XAML defaults */ }
            new Views.ReminderRunner(() => Shutdown()).Start();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_reminderMutex != null)
            {
                try { _reminderMutex.ReleaseMutex(); } catch { }
                try { _reminderMutex.Dispose(); } catch { }
            }
            // Free the overlay port and release the single-instance guard so the next
            // launch starts clean.
            try { Services.VoiceMorphService.Stop(); } catch { }
            try { Services.OverlayServer.Stop(); } catch { }
            try { _showRegistration?.Unregister(null); } catch { }
            try { _showEvent?.Dispose(); } catch { }
            if (_isPrimary)
            {
                try { _instanceMutex?.ReleaseMutex(); } catch { }
            }
            try { _instanceMutex?.Dispose(); } catch { }
            base.OnExit(e);
        }
    }
}
