using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using GameTracker.Models;
using GameTracker.Services;

namespace GameTracker.Views
{
    /// <summary>
    /// The closed-app reminder mode (GameTracker.exe --reminders, launched by the scheduled task):
    /// shows only the due reminder pop-ups — no main window, overlay or chat — saves their new
    /// schedule, re-arms the wake-up task for the next reminder, and exits once every pop-up is
    /// dealt with. If Game Hunter itself is opened meanwhile it hands off and exits.
    /// </summary>
    public sealed class ReminderRunner
    {
        public const string HandoffEventName = "Local\\LazerGuanasGameHunter.ReminderHandoff";

        private readonly Action _exit;
        private readonly Dictionary<Guid, ReminderPopup> _popups = new();
        private DispatcherTimer? _timer;
        private EventWaitHandle? _handoff;
        private RegisteredWaitHandle? _handoffWait;
        private bool _finished;

        public ReminderRunner(Action exit) => _exit = exit;

        public void Start()
        {
            // Game Hunter opened while a reminder is up: let it take over.
            try
            {
                _handoff = new EventWaitHandle(false, EventResetMode.AutoReset, HandoffEventName);
                var dispatcher = Dispatcher.CurrentDispatcher;
                _handoffWait = ThreadPool.RegisterWaitForSingleObject(_handoff,
                    (_, _) => dispatcher.Invoke(Finish), null, Timeout.Infinite, executeOnlyOnce: true);
            }
            catch { /* handoff is a nicety */ }

            Check();
            if (_finished) return;
            // Keep checking while pop-ups are open (a repeating reminder can come due again).
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _timer.Tick += (_, _) => Check();
            _timer.Start();
        }

        private void Check()
        {
            if (_finished) return;
            var games = GameDataService.Load();
            // Never write after a failed load — that would replace the library with nothing.
            if (!GameDataService.LastLoadSucceeded || games.Count == 0) { Finish(); return; }

            var due = ReminderService.TakeDue(games, DateTime.Now);
            if (due.Count > 0) GameDataService.Save(games);
            foreach (var (game, dueAt) in due)
            {
                if (_popups.ContainsKey(game.Id)) continue;
                var popup = new ReminderPopup(game, game.Reminder!, dueAt);
                popup.Chosen += OnChosen;
                _popups[game.Id] = popup;
                popup.Show();
            }
            ReminderService.SyncWakeTaskNow(ReminderService.NextWake(games), Environment.ProcessPath);
            if (_popups.Count == 0) Finish();
        }

        private void OnChosen(ReminderPopup popup, ReminderService.Choice choice)
        {
            _popups.Remove(popup.GameId);
            // Re-read so a choice never overwrites anything newer on disk.
            var games = GameDataService.Load();
            if (GameDataService.LastLoadSucceeded && games.Count > 0)
            {
                var game = games.FirstOrDefault(g => g.Id == popup.GameId);
                if (game != null && ReminderService.Apply(game, choice, DateTime.Now))
                    GameDataService.Save(games);
                ReminderService.SyncWakeTaskNow(ReminderService.NextWake(games), Environment.ProcessPath);
            }
            if (_popups.Count == 0) Finish();
        }

        private void Finish()
        {
            if (_finished) return;
            _finished = true;
            _timer?.Stop();
            foreach (var p in _popups.Values.ToList())
            {
                p.Chosen -= OnChosen;   // closing here isn't a choice; the schedule's already saved
                p.Close();
            }
            _popups.Clear();
            try { _handoffWait?.Unregister(null); } catch { }
            try { _handoff?.Dispose(); } catch { }
            _exit();
        }
    }
}
