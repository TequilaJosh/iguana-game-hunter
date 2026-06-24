using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        private Point _dragStartPoint;
        private bool _isDragging;
        private readonly DispatcherTimer _liveTimer;
        private readonly Dictionary<Guid, Views.SessionWindow> _sessionWindows = new();

        public MainWindow()
        {
            InitializeComponent();
            LoadGames();

            // While any game has a running session, keep the cards' live totals fresh.
            _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            _liveTimer.Tick += (_, _) =>
            {
                if (_games.Any(g => g.IsSessionActive))
                    RefreshView();
            };
            _liveTimer.Start();

            // Quietly check GitHub releases for a newer version on launch.
            Loaded += async (_, _) => await Services.UpdateService.CheckForUpdatesAsync(silent: true);
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
                OpenEditDialog(id);
            }
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

            if (game.IsSessionActive)
            {
                var session = game.ActiveSession!;
                session.End = DateTime.Now;
                Save();
                RefreshView();
                StatusText.Text =
                    $"Ended session for \"{game.Title}\"  -  played {PlaySession.FormatSpan(session.Duration)} " +
                    $"(total {game.TotalPlayTimeDisplay})";
            }
            else
            {
                game.Sessions.Add(new PlaySession { Start = DateTime.Now });
                var moved = game.Status != GameStatus.InProgress;
                game.Status = GameStatus.InProgress;
                Save();
                RefreshView();
                StatusText.Text = moved
                    ? $"Started session for \"{game.Title}\"  -  now HUNTING"
                    : $"Started session for \"{game.Title}\"  -  {DateTime.Now:h:mm tt}";
            }
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
