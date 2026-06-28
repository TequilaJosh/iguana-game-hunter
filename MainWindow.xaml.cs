using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using GameTracker.Models;
using GameTracker.Services;
using GameTracker.Views;

namespace GameTracker
{
    public partial class MainWindow : Window
    {
        private List<Game> _games = new();
        private string _searchQuery = string.Empty;
        private Guid? _currentGameId;
        private Point _dragStartPoint;
        private bool _isDragging;
        private readonly DispatcherTimer _liveTimer;
        private readonly DispatcherTimer _overlayTimer;
        private readonly Dictionary<Guid, Views.SessionWindow> _sessionWindows = new();
        private readonly Dictionary<Guid, Views.GuideWindow> _guideWindows = new();
        private readonly Dictionary<Guid, Views.HltbWindow> _hltbWindows = new();

        public MainWindow()
        {
            InitializeComponent();
            LoadGames();
            Services.OverlayService.Initialize();

            // While any game has a running session, keep the cards' live totals fresh.
            _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            _liveTimer.Tick += (_, _) =>
            {
                if (_games.Any(g => g.IsSessionActive))
                    RefreshView();
            };
            _liveTimer.Start();

            // Push the live "Now Playing" state to the OBS overlay files every second.
            _overlayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _overlayTimer.Tick += (_, _) => Services.OverlayService.Update(CurrentlyStreaming());
            _overlayTimer.Start();

            // Quietly check GitHub releases for a newer version on launch.
            Loaded += async (_, _) => await Services.UpdateService.CheckForUpdatesAsync(silent: true);
        }

        // The active session most recently started — what the overlay shows.
        private Game? CurrentlyStreaming() =>
            _games.Where(g => g.IsSessionActive)
                  .OrderByDescending(g => g.ActiveSession!.Start)
                  .FirstOrDefault();

        #region Global hotkeys

        private const int WM_HOTKEY = 0x0312;
        private const int HK_TOGGLE = 1, HK_CLIP = 2, HK_NOTE = 3;
        private IntPtr _hwnd;
        private HotkeyConfig _hotkeys = new();

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(_hwnd)?.AddHook(WndHook);

            _hotkeys = Services.SettingsService.LoadHotkeys();
            RegisterHotkeys();
        }

        private void RegisterHotkeys()
        {
            if (_hwnd == IntPtr.Zero) return;
            UnregisterHotkeys();
            RegisterHotKey(_hwnd, HK_TOGGLE, _hotkeys.Toggle.Win32Modifiers, _hotkeys.Toggle.VirtualKey);
            RegisterHotKey(_hwnd, HK_CLIP, _hotkeys.Clip.Win32Modifiers, _hotkeys.Clip.VirtualKey);
            RegisterHotKey(_hwnd, HK_NOTE, _hotkeys.Note.Win32Modifiers, _hotkeys.Note.VirtualKey);
        }

        private void UnregisterHotkeys()
        {
            if (_hwnd == IntPtr.Zero) return;
            UnregisterHotKey(_hwnd, HK_TOGGLE);
            UnregisterHotKey(_hwnd, HK_CLIP);
            UnregisterHotKey(_hwnd, HK_NOTE);
        }

        private IntPtr WndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                switch (wParam.ToInt32())
                {
                    case HK_TOGGLE: HotkeyToggle(); handled = true; break;
                    case HK_CLIP: HotkeyClip(); handled = true; break;
                    case HK_NOTE: HotkeyNote(); handled = true; break;
                }
            }
            return IntPtr.Zero;
        }

        protected override void OnClosed(EventArgs e)
        {
            UnregisterHotkeys();
            base.OnClosed(e);
        }

        private Game? ResolveCurrentGame()
        {
            var active = CurrentlyStreaming();
            if (active != null) return active;
            if (_currentGameId is Guid id)
            {
                var g = _games.FirstOrDefault(x => x.Id == id);
                if (g != null) return g;
            }
            return _games.Where(g => g.LastPlayed != null)
                         .OrderByDescending(g => g.LastPlayed)
                         .FirstOrDefault()
                   ?? _games.FirstOrDefault(g => g.Status == GameStatus.InProgress);
        }

        private void HotkeyToggle()
        {
            var game = ResolveCurrentGame();
            if (game == null)
            {
                StatusText.Text = "Hotkey: no game to start — pick one first.";
                return;
            }
            if (game.IsSessionActive) StopSession(game);
            else StartSession(game);
        }

        private void HotkeyClip()
        {
            var game = CurrentlyStreaming();
            if (game?.ActiveSession == null)
            {
                StatusText.Text = "Hotkey: no active session to clip.";
                return;
            }
            game.ActiveSession.Markers.Add(new SessionMarker { At = DateTime.Now, Text = string.Empty });
            Save();
            RefreshView();
            StatusText.Text = $"Clip marked for \"{game.Title}\" at {DateTime.Now:h:mm tt}";
        }

        private void HotkeyNote()
        {
            var game = CurrentlyStreaming();
            if (game?.ActiveSession == null)
            {
                StatusText.Text = "Hotkey: no active session for a note.";
                return;
            }
            var win = new Views.QuickNoteWindow { Owner = this };
            if (win.ShowDialog() == true && !string.IsNullOrWhiteSpace(win.NoteText))
            {
                game.ActiveSession.Markers.Add(new SessionMarker
                {
                    At = DateTime.Now,
                    Text = win.NoteText.Trim()
                });
                Save();
                RefreshView();
                StatusText.Text = $"Note clipped for \"{game.Title}\".";
            }
        }

        #endregion

        private Views.HelpWindow? _helpWindow;

        private void Help_Click(object sender, RoutedEventArgs e)
        {
            if (_helpWindow != null) { _helpWindow.Activate(); return; }

            _helpWindow = new Views.HelpWindow(_hotkeys,
                onBeginCapture: UnregisterHotkeys,           // free the keys so the new combo can be read
                onApply: () =>
                {
                    Services.SettingsService.SaveHotkeys(_hotkeys);
                    RegisterHotkeys();
                })
            { Owner = this };
            _helpWindow.Closed += (_, _) => _helpWindow = null;
            _helpWindow.Show();
        }

        private void Overlay_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Services.OverlayService.FolderPath);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    Services.OverlayService.FolderPath) { UseShellExecute = true });
            }
            catch { /* ignore */ }

            MessageBox.Show(
                "Stream overlay files are in the folder that just opened.\n\n" +
                "OBS Text source: add a Text (GDI+) source, check \"Read from file\", and pick " +
                "game.txt, elapsed.txt, requester.txt, or now_playing.txt.\n\n" +
                "OBS Browser source: add a Browser source, check \"Local file\", and select " +
                "overlay.html (transparent styled card; ~600x120).\n\n" +
                "They update live while a session is running and clear when you stop. " +
                "See README.txt in the folder.",
                "Stream Overlay (OBS)", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "Checking for updates…";
            await Services.UpdateService.CheckForUpdatesAsync(silent: false);
        }

        private void PopOutSession_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is not Button btn || btn.Tag is not Guid id) return;

            var game = _games.FirstOrDefault(g => g.Id == id);
            if (game == null || !game.IsSessionActive) return;
            _currentGameId = id;

            if (_sessionWindows.TryGetValue(id, out var existing))
            {
                existing.Activate();
                return;
            }

            var win = new Views.SessionWindow(game, () => { Save(); RefreshView(); }) { Owner = this };
            _sessionWindows[id] = win;
            win.Closed += (_, _) => _sessionWindows.Remove(id);
            win.Show();
        }

        private void OpenGuide_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is not Button btn || btn.Tag is not Guid id) return;

            var game = _games.FirstOrDefault(g => g.Id == id);
            if (game == null) return;

            if (_guideWindows.TryGetValue(id, out var existing))
            {
                existing.Activate();
                return;
            }

            var win = new Views.GuideWindow(game.Title, game.GuideUrl, game.GuideScroll,
                (url, scroll) =>
                {
                    game.GuideUrl = url;
                    game.GuideScroll = scroll;
                    Save();
                    RefreshView();
                })
            { Owner = this };
            _guideWindows[id] = win;
            win.Closed += (_, _) => _guideWindows.Remove(id);
            win.Show();
        }

        private void OpenHltb_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is not Button btn || btn.Tag is not Guid id) return;

            var game = _games.FirstOrDefault(g => g.Id == id);
            if (game == null) return;

            if (_hltbWindows.TryGetValue(id, out var existing))
            {
                existing.Activate();
                return;
            }

            var win = new Views.HltbWindow(game.Title) { Owner = this };
            _hltbWindows[id] = win;
            win.Closed += (_, _) => _hltbWindows.Remove(id);
            win.Show();
        }

        private void LoadGames()
        {
            _games = GameDataService.Load();
            RefreshView();
        }

        private void Save()
        {
            GameDataService.Save(_games);
        }

        private void RefreshView()
        {
            var filtered = string.IsNullOrWhiteSpace(_searchQuery)
                ? _games
                : _games.Where(g =>
                    g.Title.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase) ||
                    g.Platform.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase) ||
                    g.Genre.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase) ||
                    g.Requester.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase)).ToList();

            var notStarted = filtered.Where(g => g.Status == GameStatus.NotStarted)
                                     .OrderBy(g => g.Title).ToList();
            var inProgress = filtered.Where(g => g.Status == GameStatus.InProgress)
                                     .OrderByDescending(g => g.LastPlayed).ToList();
            var beaten = filtered.Where(g => g.Status == GameStatus.Beaten)
                                  .OrderByDescending(g => g.LastPlayed).ToList();

            NotStartedList.ItemsSource = notStarted;
            InProgressList.ItemsSource = inProgress;
            BeatenList.ItemsSource = beaten;

            NotStartedCount.Text = notStarted.Count.ToString();
            InProgressCount.Text = inProgress.Count.ToString();
            BeatenCount.Text = beaten.Count.ToString();

            var total = _games.Count;
            var played = _games.Count(g => g.PlaySessions.Count > 0);
            StatusText.Text = $"{total} games total  -  {played} played  -  {beaten.Count} beaten  -  {inProgress.Count} in progress";
        }

        private void AddGame_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AddEditGameWindow { Owner = this };
            if (dialog.ShowDialog() == true && dialog.ResultGame != null)
            {
                _games.Add(dialog.ResultGame);
                Save();
                RefreshView();
            }
        }

        private void GameCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                return;
            }
            if (sender is Border border && border.Tag is Guid id)
            {
                OpenInfoDialog(id);
            }
        }

        private void OpenInfoDialog(Guid id)
        {
            var game = _games.FirstOrDefault(g => g.Id == id);
            if (game == null) return;

            var info = new Views.GameInfoWindow(game) { Owner = this };
            info.ShowDialog();

            // "Edit" in the info window hands off to the full edit dialog.
            if (info.EditRequested)
                OpenEditDialog(id);
        }

        private void GameCard_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        private void GameCard_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            var pos = e.GetPosition(null);
            if (Math.Abs(pos.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            if (sender is Border border && border.Tag is Guid id)
            {
                _isDragging = true;
                DragDrop.DoDragDrop(border, id.ToString(), DragDropEffects.Move);
            }
        }

        private void Column_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.StringFormat)
                ? DragDropEffects.Move
                : DragDropEffects.None;
            if (sender is Border b && e.Effects == DragDropEffects.Move)
                b.Opacity = 0.75;
            e.Handled = true;
        }

        private void Column_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is Border b) b.Opacity = 1.0;
        }

        private void Column_Drop(object sender, DragEventArgs e)
        {
            if (sender is Border b) b.Opacity = 1.0;
            if (sender is Border target &&
                target.Tag is string statusName &&
                Enum.TryParse<GameStatus>(statusName, out var status) &&
                e.Data.GetData(DataFormats.StringFormat) is string idStr &&
                Guid.TryParse(idStr, out var id))
            {
                MoveGameTo(id, status);
            }
        }

        private void MoveGameTo(Guid id, GameStatus status)
        {
            var game = _games.FirstOrDefault(g => g.Id == id);
            if (game == null || game.Status == status) return;
            game.Status = status;
            Save();
            RefreshView();
            StatusText.Text = $"Moved \"{game.Title}\" to {ColumnLabel(status)}";
        }

        private static string ColumnLabel(GameStatus s) => s switch
        {
            GameStatus.NotStarted => "DORMANT",
            GameStatus.InProgress => "HUNTING",
            GameStatus.Beaten     => "DEVOURED",
            _ => s.ToString()
        };

        private Guid? GetMenuItemGameId(object sender)
        {
            if (sender is MenuItem mi &&
                mi.Parent is ContextMenu cm &&
                cm.PlacementTarget is Border b &&
                b.Tag is Guid id)
                return id;
            return null;
        }

        private void MoveToDormant_Click(object sender, RoutedEventArgs e)
        {
            if (GetMenuItemGameId(sender) is Guid id) MoveGameTo(id, GameStatus.NotStarted);
        }

        private void MoveToHunting_Click(object sender, RoutedEventArgs e)
        {
            if (GetMenuItemGameId(sender) is Guid id) MoveGameTo(id, GameStatus.InProgress);
        }

        private void MoveToDevoured_Click(object sender, RoutedEventArgs e)
        {
            if (GetMenuItemGameId(sender) is Guid id) MoveGameTo(id, GameStatus.Beaten);
        }

        private void RemoveGame_Click(object sender, RoutedEventArgs e)
        {
            if (GetMenuItemGameId(sender) is not Guid id) return;
            var game = _games.FirstOrDefault(g => g.Id == id);
            if (game == null) return;

            var result = MessageBox.Show(
                $"Shed \"{game.Title}\" from your tracker?\nThis cannot be undone.",
                "Shed Game", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            _games.Remove(game);
            Save();
            RefreshView();
            StatusText.Text = $"Shed \"{game.Title}\"";
        }

        private void ToggleSession_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is not Button btn || btn.Tag is not Guid id) return;
            var game = _games.FirstOrDefault(g => g.Id == id);
            if (game == null) return;

            if (game.IsSessionActive) StopSession(game);
            else StartSession(game);
        }

        private void StartSession(Game game)
        {
            _currentGameId = game.Id;
            game.Sessions.Add(new PlaySession { Start = DateTime.Now });
            var moved = game.Status != GameStatus.InProgress;
            game.Status = GameStatus.InProgress;
            Save();
            RefreshView();
            Services.OverlayService.Update(CurrentlyStreaming());
            StatusText.Text = moved
                ? $"Started session for \"{game.Title}\"  -  now HUNTING"
                : $"Started session for \"{game.Title}\"  -  {DateTime.Now:h:mm tt}";
        }

        private void StopSession(Game game)
        {
            var session = game.ActiveSession;
            if (session == null) return;
            session.End = DateTime.Now;
            Save();
            RefreshView();
            Services.OverlayService.Update(CurrentlyStreaming());
            StatusText.Text =
                $"Ended session for \"{game.Title}\"  -  played {PlaySession.FormatSpan(session.Duration)} " +
                $"(total {game.TotalPlayTimeDisplay})";
        }

        private void Spin_Click(object sender, RoutedEventArgs e)
        {
            // All games are available to the wheel; Dormant ones are on it by default.
            var wheelGames = _games.Select(g => new Views.WheelGame
            {
                Id = g.Id,
                Title = g.Title,
                SuggestionType = string.IsNullOrWhiteSpace(g.SuggestionType)
                    ? SuggestionTypes.Default : g.SuggestionType,
                StatusLabel = g.Status switch
                {
                    GameStatus.NotStarted => "Dormant",
                    GameStatus.InProgress => "Hunting",
                    GameStatus.Beaten => "Devoured",
                    _ => "",
                },
                IsDefault = g.Status == GameStatus.NotStarted,
            }).ToList();

            var win = new Views.WheelWindow("Spin the Backlog",
                Array.Empty<string>(), editable: false,
                onItemsChanged: null, onChosen: null, chooseButtonText: "▶ Start playing",
                games: wheelGames,
                onStartGame: id =>
                {
                    var game = _games.FirstOrDefault(g => g.Id == id);
                    if (game != null) StartSession(game);
                })
            { Owner = this };
            win.ShowDialog();
        }

        private void OpenWheel_Click(object sender, RoutedEventArgs e)
        {
            if (GetMenuItemGameId(sender) is not Guid id) return;
            var game = _games.FirstOrDefault(g => g.Id == id);
            if (game == null) return;

            var win = new Views.WheelWindow($"{game.Title} Wheel", game.WheelItems, editable: true,
                onItemsChanged: items => { game.WheelItems = new List<string>(items); Save(); },
                onChosen: null, chooseButtonText: null,
                initialResults: game.WheelResults,
                onResultsChanged: results =>
                {
                    game.WheelResults = new List<string>(results);
                    Save();
                    Services.OverlayService.Update(CurrentlyStreaming());
                },
                onRolled: challenge => LogChallengeToSession(game, challenge))
            { Owner = this };
            win.Show();
        }

        // Record a rolled wheel challenge into the game's session notes.
        private void LogChallengeToSession(Game game, string challenge)
        {
            var line = $"🎡 {challenge}";

            // If the live pop-out is open, append through it so its notes box stays in sync.
            if (_sessionWindows.TryGetValue(game.Id, out var sw))
            {
                sw.AppendNote(line);
                return;
            }

            var session = game.ActiveSession
                          ?? game.Sessions.OrderByDescending(s => s.Start).FirstOrDefault();
            if (session == null) return;

            session.Note = string.IsNullOrWhiteSpace(session.Note)
                ? line
                : session.Note.TrimEnd() + Environment.NewLine + line;
            Save();
        }

        private void Recap_Click(object sender, RoutedEventArgs e)
        {
            var win = new Views.RecapWindow(_games) { Owner = this };
            win.Show();
        }

        private Views.ChatWindow? _chatWindow;

        private void Chat_Click(object sender, RoutedEventArgs e)
        {
            if (_chatWindow != null) { _chatWindow.Activate(); return; }
            _chatWindow = new Views.ChatWindow { Owner = this };
            _chatWindow.Closed += (_, _) => _chatWindow = null;
            _chatWindow.Show();
        }

        private void OpenEditDialog(Guid id)
        {
            var game = _games.FirstOrDefault(g => g.Id == id);
            if (game == null) return;

            var dialog = new AddEditGameWindow(game) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                if (dialog.Deleted)
                {
                    _games.Remove(game);
                }
                else if (dialog.ResultGame != null)
                {
                    var idx = _games.IndexOf(game);
                    _games[idx] = dialog.ResultGame;
                }
                Save();
                RefreshView();
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchQuery = SearchBox.Text;
            RefreshView();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState.Minimized;

        private void MaxRestore_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            MaxRestoreButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
