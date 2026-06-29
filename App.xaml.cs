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

        protected override void OnStartup(StartupEventArgs e)
        {
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

            // Primary instance: listen for "show" pings from any later launches.
            try
            {
                _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
                _showRegistration = ThreadPool.RegisterWaitForSingleObject(
                    _showEvent, (_, _) => BringToFront(), null, Timeout.Infinite, executeOnlyOnce: false);
            }
            catch { /* focusing the existing window is best-effort */ }

            base.OnStartup(e);

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

        protected override void OnExit(ExitEventArgs e)
        {
            // Free the overlay port and release the single-instance guard so the next
            // launch starts clean.
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
