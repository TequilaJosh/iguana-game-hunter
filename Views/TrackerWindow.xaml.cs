using System;
using System.Windows;
using GameTracker.Services;

namespace GameTracker.Views
{
    /// <summary>
    /// The randomizer tracker living on its own — same dashboard as the one that docks
    /// under the chat, just in a window you can park on a second monitor and pin on top.
    /// </summary>
    public partial class TrackerWindow : Window
    {
        /// <summary>The open tracker window, if there is one.</summary>
        public static TrackerWindow? Current { get; private set; }

        private bool _pinned;

        public TrackerWindow()
        {
            InitializeComponent();
            Current = this;
            Panel.Mode = TrackerPanelMode.Window;
            Panel.PopOutRequested += DockBack;
            Closed += (_, _) =>
            {
                if (Current == this) Current = null;
                TrackerService.Flush();
            };
        }

        /// <summary>Open it, or bring the one that's already open to the front.</summary>
        public static TrackerWindow Open(Window? owner)
        {
            if (Current is { } w)
            {
                if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
                w.Activate();
                return w;
            }
            var win = new TrackerWindow();
            // Owning it to a dialog-ish window would drag it down when that closes, so the
            // tracker is only owned by the main window.
            if (owner is MainWindow) win.Owner = owner;
            win.Show();
            return win;
        }

        // Put it back under the chat (when the chat window is open to take it).
        private void DockBack()
        {
            if (ChatWindow.Current is { } chat)
            {
                chat.ShowTrackerDock(true);
                chat.Activate();
            }
            Close();
        }

        private void Pin_Click(object sender, RoutedEventArgs e)
        {
            _pinned = !_pinned;
            Topmost = _pinned;
            PinButton.Opacity = _pinned ? 1.0 : 0.55;
            PinButton.ToolTip = _pinned ? "Unpin" : "Pin on top";
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
