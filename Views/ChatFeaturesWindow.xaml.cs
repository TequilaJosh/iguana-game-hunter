using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Microsoft.Win32;
using GameTracker.Models;
using GameTracker.Services;

namespace GameTracker.Views
{
    /// <summary>Streamer settings for chatter counts, the chatters list, points, chat style and point redeems.</summary>
    public partial class ChatFeaturesWindow : Window
    {
        private readonly ObservableCollection<ColorItem> _colors = new();
        private readonly ObservableCollection<RedeemItem> _redeems = new();
        private readonly SoundService _tester = new();

        // Shared, live list of morph voices ("" + every saved voice) that all redeem rows'
        // "Voice morph" dropdowns bind to. Refreshed on Activated so a voice saved in the morph
        // lab shows up here in real time without reopening the window.
        public ObservableCollection<string> MorphChoicesShared { get; } = new();

        private void RefreshMorphChoices()
        {
            var desired = new System.Collections.Generic.List<string> { "" };
            try
            {
                foreach (var p in SettingsService.LoadMorph().Presets)
                    if (!p.RedeemOwned && !string.IsNullOrWhiteSpace(p.Name)) desired.Add(p.Name);
            }
            catch { }
            for (int i = MorphChoicesShared.Count - 1; i >= 0; i--)
                if (!desired.Contains(MorphChoicesShared[i])) MorphChoicesShared.RemoveAt(i);
            foreach (var d in desired)
                if (!MorphChoicesShared.Contains(d)) MorphChoicesShared.Add(d);
        }

        private void CfTab_Changed(object sender, RoutedEventArgs e)
        {
            if (PanelGeneral == null || PanelPoints == null) return;   // during init
            bool points = TabPoints.IsChecked == true;
            PanelGeneral.Visibility = points ? Visibility.Collapsed : Visibility.Visible;
            PanelPoints.Visibility = points ? Visibility.Visible : Visibility.Collapsed;
        }

        // Open straight to the Points & Redeems tab when true.
        public ChatFeaturesWindow(bool pointsTab = false)
        {
            InitializeComponent();
            if (pointsTab) TabPoints.IsChecked = true; else TabGeneral.IsChecked = true;
            var f = SettingsService.LoadChatFeatures();

            ShowCountCb.IsChecked = f.ShowCount;
            PerSourceCb.IsChecked = f.CountPerSource;
            CountOverlayCb.IsChecked = f.CountOnOverlay;
            ChattersOverlayCb.IsChecked = f.ShowChattersOnOverlay;
            ReplyCb.IsChecked = f.ReplyInChat;
            PostClipsCb.IsChecked = f.PostClips;
            DiscordWebhookBox.Password = f.DiscordWebhook;
            BotIngestUrlBox.Text = f.BotIngestUrl;
            BotIngestTokenBox.Password = f.BotIngestToken;
            RpgEnabledCb.IsChecked = f.RpgEnabled;
            LurkBox.Text = f.LurkMinutes.ToString();
            RemoveBox.Text = f.RemoveMinutes.ToString();
            PointsCb.IsChecked = f.PointsEnabled;
            VideoAudioAppCb.IsChecked = f.VideoAudioThroughApp;
            PointsNameBox.Text = f.PointsName;
            PointsIntervalBox.Text = f.PointsIntervalMinutes.ToString();
            PointsAmountBox.Text = f.PointsPerInterval.ToString();
            FirstBonusBox.Text = f.FirstChatterBonus.ToString();
            StreakBonusBox.Text = f.StreakBonusPerDay.ToString();
            BalanceCmdBox.Text = string.IsNullOrWhiteSpace(f.BalanceCommand) ? "!points" : f.BalanceCommand;
            SelectStyle(f.ChatStyle);

            foreach (var c in f.BoxColors) _colors.Add(new ColorItem { Hex = c });
            ColorList.ItemsSource = _colors;

            foreach (var r in f.Redeems)
                _redeems.Add(new RedeemItem
                {
                    Command = r.Command, Effect = r.Effect, Cost = r.Cost,
                    SoundPath = r.SoundPath, ImagePath = r.ImagePath, VideoPath = r.VideoPath,
                    MorphPreset = r.MorphPreset, Volume = r.Volume, Hotkey = r.Hotkey,
                });
            RedeemList.ItemsSource = _redeems;

            RefreshMorphChoices();
            Activated += (_, _) => RefreshMorphChoices();   // pick up lab voices live
        }

        // Select the saved chat style in the dropdown (defaults to "log" if unknown).
        private void SelectStyle(string? mode)
        {
            mode = string.IsNullOrWhiteSpace(mode) ? "log" : mode.Trim().ToLowerInvariant();
            foreach (var obj in StyleCombo.Items)
                if (obj is System.Windows.Controls.ComboBoxItem it &&
                    string.Equals(it.Tag as string, mode, StringComparison.OrdinalIgnoreCase))
                { StyleCombo.SelectedItem = it; return; }
            StyleCombo.SelectedIndex = 0;   // fall back to Chat log
        }

        private void Style_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (StylePreview == null) return;   // fires once during init before the panel exists
            var mode = (StyleCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "log";
            RenderStylePreview(mode);
        }

        // Two sample lines rendered in the selected style so the streamer sees it before saving.
        private static readonly (string User, string Msg)[] _sample =
            { ("Rytlock", "For the Legion!"), ("Aerith", "Got your back — healing up!") };

        private void RenderStylePreview(string mode)
        {
            StylePreview.Children.Clear();
            var cols = _colors.Select(c => c.Hex).Where(IsHex).ToList();
            if (cols.Count == 0) cols = new System.Collections.Generic.List<string> { "#9fb4ff", "#f8d878", "#4aa3c4" };
            for (int i = 0; i < _sample.Length; i++)
                StylePreview.Children.Add(BuildPreviewRow(mode, _sample[i].User, _sample[i].Msg, cols[i % cols.Count]));
        }

        private static SolidColorBrush PB(string hex)
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { return new SolidColorBrush(Colors.Gray); }
        }
        private static readonly SolidColorBrush DarkPanel = new(Color.FromArgb(0xD6, 0, 0, 0));
        private static readonly SolidColorBrush Ink = new(Color.FromRgb(0x08, 0x0e, 0x34));

        private static UIElement BuildPreviewRow(string mode, string user, string msg, string hex)
        {
            switch (mode)
            {
                case "boxes":
                {
                    var head = new Border { Background = PB(hex), CornerRadius = new CornerRadius(8, 8, 0, 0), Padding = new Thickness(6, 2, 6, 2),
                        Child = new TextBlock { Text = "● " + user, Foreground = Ink, FontWeight = FontWeights.Bold, FontSize = 12 } };
                    var body = new Border { Background = DarkPanel, CornerRadius = new CornerRadius(0, 0, 8, 8), Padding = new Thickness(6, 3, 6, 3),
                        Child = new TextBlock { Text = msg, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap } };
                    return new StackPanel { Margin = new Thickness(0, 3, 0, 3), Children = { head, body } };
                }
                case "rpg":
                {
                    var grid = new Grid { Margin = new Thickness(0, 9, 0, 4), HorizontalAlignment = HorizontalAlignment.Left };
                    var box = new Border { BorderBrush = PB(hex), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(8),
                        Background = PB("#0a0a12"), Padding = new Thickness(9, 9, 9, 9), MaxWidth = 300,
                        Child = new TextBlock { Text = msg, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap } };
                    var name = new Border { Background = PB(hex), CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 0, 6, 0),
                        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(7, -8, 0, 0),
                        Child = new TextBlock { Text = user, Foreground = Ink, FontWeight = FontWeights.Bold, FontSize = 11 } };
                    var arrow = new TextBlock { Text = "▼", Foreground = PB(hex), FontSize = 10,
                        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 7, 3) };
                    grid.Children.Add(box); grid.Children.Add(name); grid.Children.Add(arrow);
                    return grid;
                }
                case "speech":
                {
                    var av = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = PB(hex), Margin = new Thickness(0, 2, 6, 0),
                        Child = new TextBlock { Text = Initial(user), Foreground = Ink, FontWeight = FontWeights.Bold, FontSize = 11,
                            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                    var st = new StackPanel();
                    st.Children.Add(new TextBlock { Text = user, Foreground = PB(hex), FontWeight = FontWeights.Bold, FontSize = 12 });
                    st.Children.Add(new TextBlock { Text = msg, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap });
                    var bub = new Border { Background = DarkPanel, BorderBrush = PB(hex), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(12),
                        Padding = new Thickness(8, 4, 8, 4), MaxWidth = 270, Child = st };
                    return new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3), Children = { av, bub } };
                }
                case "comic":
                {
                    // Batman-style action burst: a spiky starburst that pops in.
                    const double W = 240, H = 120;
                    var poly = new System.Windows.Shapes.Polygon { Points = StarburstPoints(W, H), Fill = PB(hex), Stroke = Brushes.Black, StrokeThickness = 2.5 };
                    var st = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = W * 0.6 };
                    st.Children.Add(new TextBlock { Text = user.ToUpperInvariant(), Foreground = Brushes.White, FontWeight = FontWeights.Black, FontSize = 11,
                        HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, Effect = Outline() });
                    st.Children.Add(new TextBlock { Text = msg.ToUpperInvariant(), Foreground = Brushes.White, FontWeight = FontWeights.Black, FontSize = 14,
                        HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Effect = Outline() });
                    var grid = new Grid { Width = W, Height = H, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 4),
                        RenderTransformOrigin = new Point(0.5, 0.5) };
                    grid.Children.Add(poly); grid.Children.Add(st);
                    var sc = new ScaleTransform(0.2, 0.2); grid.RenderTransform = sc;
                    grid.Loaded += (_, _) =>
                    {
                        sc.BeginAnimation(ScaleTransform.ScaleXProperty, PopAnim());
                        sc.BeginAnimation(ScaleTransform.ScaleYProperty, PopAnim());
                    };
                    return grid;
                }
                case "terminal":
                {
                    // Types the message out like a terminal.
                    var tb = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 12, Margin = new Thickness(0, 1, 0, 1), TextWrapping = TextWrapping.Wrap };
                    tb.Inlines.Add(new Run("❯ ") { Foreground = PB(hex), FontWeight = FontWeights.Bold });
                    tb.Inlines.Add(new Run(user + ": ") { Foreground = PB(hex), FontWeight = FontWeights.Bold });
                    var body = new Run("") { Foreground = Brushes.White };
                    var cur = new Run("▋") { Foreground = PB(hex) };
                    tb.Inlines.Add(body); tb.Inlines.Add(cur);
                    int i = 0;
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(35) };
                    timer.Tick += (_, _) =>
                    {
                        if (i >= msg.Length) { timer.Stop(); tb.Inlines.Remove(cur); return; }
                        body.Text += msg[i++];
                    };
                    tb.Loaded += (_, _) => timer.Start();
                    tb.Unloaded += (_, _) => timer.Stop();
                    return tb;
                }
                case "neon":
                {
                    var t = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White };
                    t.Inlines.Add(new Run(user + " ") { Foreground = PB(hex), FontWeight = FontWeights.Bold });
                    t.Inlines.Add(new Run(msg));
                    var b = new Border { BorderBrush = PB(hex), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(10),
                        Background = new SolidColorBrush(Color.FromArgb(0xD8, 6, 10, 26)), Padding = new Thickness(8, 5, 8, 5),
                        HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 4), MaxWidth = 300, Child = t,
                        Effect = new DropShadowEffect { Color = ColOf(hex), ShadowDepth = 0, BlurRadius = 14, Opacity = 0.9 } };
                    b.Loaded += (_, _) => b.BeginAnimation(OpacityProperty, FlickerAnim());
                    return b;
                }
                case "scroll":
                {
                    var st = new StackPanel();
                    st.Children.Add(new TextBlock { Text = user, Foreground = PB("#7a2e12"), FontWeight = FontWeights.Bold, FontFamily = new FontFamily("Georgia") });
                    st.Children.Add(new TextBlock { Text = msg, Foreground = PB("#4a2f14"), FontFamily = new FontFamily("Georgia"), TextWrapping = TextWrapping.Wrap });
                    var b = new Border { Background = new LinearGradientBrush(ColOf("#f3e2b8"), ColOf("#e6cf92"), 90),
                        BorderBrush = PB("#b9985a"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(9, 6, 9, 6), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 4),
                        MaxWidth = 300, Child = st, RenderTransformOrigin = new Point(0.5, 0) };
                    var sc = new ScaleTransform(1, 0.05); b.RenderTransform = sc;
                    b.Loaded += (_, _) => sc.BeginAnimation(ScaleTransform.ScaleYProperty,
                        new DoubleAnimation(0.05, 1, new Duration(TimeSpan.FromMilliseconds(500))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                    return b;
                }
                case "coin":
                {
                    var t = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = PB("#3a2500"), FontWeight = FontWeights.Bold };
                    t.Inlines.Add(new Run(user.ToUpperInvariant() + " ") { Foreground = PB("#7a4d00"), FontWeight = FontWeights.Black });
                    t.Inlines.Add(new Run(msg));
                    var b = new Border { Background = new LinearGradientBrush(ColOf("#ffcf3f"), ColOf("#e8a91e"), 90),
                        BorderBrush = PB("#7a4d00"), BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(8, 5, 8, 5), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 6),
                        MaxWidth = 290, Child = t };
                    var tr = new TranslateTransform(0, 11); b.RenderTransform = tr;
                    b.Loaded += (_, _) => tr.BeginAnimation(TranslateTransform.YProperty, BounceAnim());
                    return b;
                }
                case "holo":
                {
                    var t = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = PB("#dff1ff") };
                    t.Inlines.Add(new Run(user + " ") { Foreground = PB(hex), FontWeight = FontWeights.Bold });
                    t.Inlines.Add(new Run(msg));
                    var b = new Border { BorderBrush = PB(hex), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                        Background = new SolidColorBrush(Color.FromArgb(0x26, 60, 160, 255)), Padding = new Thickness(8, 5, 8, 5),
                        HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 4), MaxWidth = 300, Child = t,
                        Effect = new DropShadowEffect { Color = ColOf(hex), ShadowDepth = 0, BlurRadius = 12, Opacity = 0.8 } };
                    b.Loaded += (_, _) => b.BeginAnimation(OpacityProperty, FlickerAnim());
                    return b;
                }
                default:   // log
                {
                    var tb = new TextBlock { Margin = new Thickness(0, 1, 0, 1), TextWrapping = TextWrapping.Wrap };
                    tb.Inlines.Add(new Run("● ") { Foreground = PB(hex) });
                    tb.Inlines.Add(new Run(user + " ") { Foreground = PB(hex), FontWeight = FontWeights.Bold });
                    tb.Inlines.Add(new Run(msg) { Foreground = Brushes.White });
                    return tb;
                }
            }
        }

        private static string Initial(string s) => string.IsNullOrEmpty(s) ? "?" : s.Substring(0, 1).ToUpperInvariant();

        // 20-point comic starburst, matching the overlay's clip-path, scaled to w×h.
        private static readonly double[,] _burst = {
            {50,0},{61,20},{83,8},{79,32},{100,35},{82,50},{100,65},{79,68},{83,92},{61,80},
            {50,100},{39,80},{17,92},{21,68},{0,65},{18,50},{0,35},{21,32},{17,8},{39,20} };
        private static PointCollection StarburstPoints(double w, double h)
        {
            var pts = new PointCollection();
            for (int i = 0; i < _burst.GetLength(0); i++)
                pts.Add(new Point(_burst[i, 0] / 100.0 * w, _burst[i, 1] / 100.0 * h));
            return pts;
        }

        // Fake a black text outline (WPF has no native text stroke).
        private static DropShadowEffect Outline() =>
            new() { Color = Colors.Black, ShadowDepth = 0, BlurRadius = 3, Opacity = 1 };

        // Pop-in with a little overshoot, for the comic burst.
        private static DoubleAnimationUsingKeyFrames PopAnim()
        {
            var a = new DoubleAnimationUsingKeyFrames();
            a.KeyFrames.Add(new EasingDoubleKeyFrame(0.2, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(1.14, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(230))));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(0.95, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(310))));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(380))));
            return a;
        }

        private static Color ColOf(string hex)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return Colors.Gray; }
        }

        // Neon/hologram flicker-on.
        private static DoubleAnimationUsingKeyFrames FlickerAnim()
        {
            var a = new DoubleAnimationUsingKeyFrames();
            void K(double v, int ms) => a.KeyFrames.Add(new DiscreteDoubleKeyFrame(v, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
            K(0, 0); K(1, 70); K(0.25, 95); K(1, 140); K(0.5, 190); K(1, 240);
            return a;
        }

        // Mario ? block bounce.
        private static DoubleAnimationUsingKeyFrames BounceAnim()
        {
            var a = new DoubleAnimationUsingKeyFrames();
            a.KeyFrames.Add(new EasingDoubleKeyFrame(11, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(-6, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260))));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(2, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(400))));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(520))));
            return a;
        }

        // ---- colors ----

        private void AddColor_Click(object sender, RoutedEventArgs e)
        {
            if (_colors.Count >= 10) { StatusText.Text = "Max 10 colors."; return; }
            _colors.Add(new ColorItem { Hex = "#7cc44a" });
        }

        private void RemoveColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is ColorItem c) _colors.Remove(c);
        }

        private void PickBoxColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is ColorItem c)
            {
                var hex = ColorPickerWindow.Pick(this, c.Hex);
                if (hex != null) c.Hex = hex;
            }
        }

        // ---- redeems ----

        private void AddRedeem_Click(object sender, RoutedEventArgs e) =>
            _redeems.Add(new RedeemItem { Command = "!", Effect = "confetti", Cost = 100 });

        private void RemoveRedeem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is RedeemItem r) _redeems.Remove(r);
        }

        private void PickSound_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not RedeemItem r) return;
            var dlg = new OpenFileDialog
            {
                Title = "Choose a sound file",
                Filter = "Audio files|*.mp3;*.wav;*.wma;*.m4a;*.aac;*.flac;*.ogg;*.aif;*.aiff|All files (*.*)|*.*",
            };
            if (dlg.ShowDialog(this) == true) r.SoundPath = dlg.FileName;
        }

        private void PickImage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not RedeemItem r) return;
            var dlg = new OpenFileDialog
            {
                Title = "Choose an image",
                Filter = "Images|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp|All files (*.*)|*.*",
            };
            if (dlg.ShowDialog(this) == true) r.ImagePath = dlg.FileName;
        }

        private void PickVideo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not RedeemItem r) return;
            var dlg = new OpenFileDialog
            {
                Title = "Choose a video",
                Filter = "Video files|*.mp4;*.m4v;*.webm;*.ogv;*.mov;*.mkv;*.avi|All files (*.*)|*.*",
            };
            if (dlg.ShowDialog(this) == true)
            {
                r.VideoPath = dlg.FileName;
                if (string.IsNullOrWhiteSpace(r.Effect) || r.Effect == "confetti") r.Effect = "video";
            }
        }

        private void ClearVideo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is RedeemItem r) r.VideoPath = string.Empty;
        }

        private void TestRedeem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not RedeemItem r) return;

            // Persist the current redeem list quietly so /fx and /fxvideo can serve the files.
            var saved = SettingsService.LoadChatFeatures();
            saved.Redeems = BuildRedeems();
            saved.VideoAudioThroughApp = VideoAudioAppCb.IsChecked == true;   // test with what's ticked now
            SettingsService.SaveChatFeatures(saved);

            if (!string.IsNullOrWhiteSpace(r.SoundPath))
                _tester.Play(r.SoundPath, r.Volume, msg => Dispatcher.Invoke(() => StatusText.Text = msg));

            if (OverlayServer.IsRunning)
            {
                int idx = saved.Redeems.FindIndex(x =>
                    x.Command == r.Command.Trim() && x.VideoPath == r.VideoPath && x.ImagePath == r.ImagePath);
                OverlayServer.TriggerEffect(r.Effect,
                    r.Effect == "custom" && idx >= 0 && !string.IsNullOrWhiteSpace(r.ImagePath)
                        ? "/fx/" + idx : null);
                if (idx >= 0 && !string.IsNullOrWhiteSpace(r.VideoPath))
                    OverlayServer.PlayVideo("/fxvideo/" + idx, (int)(Math.Clamp(r.Volume, 0, 1) * 100));
                if (!string.IsNullOrWhiteSpace(r.MorphPreset))
                    VoiceMorphService.ActivateByName(r.MorphPreset);
                StatusText.Text = "Fired " + r.Effect + " on the overlay.";
            }
            else StatusText.Text = "Overlay server isn't running — sound only.";
        }

        // ---- save ----

        private System.Collections.Generic.List<EffectRedeem> BuildRedeems() =>
            _redeems.Where(r => !string.IsNullOrWhiteSpace(r.Command))
                    .Select(r => new EffectRedeem
                    {
                        Command = r.Command.Trim(),
                        Effect = string.IsNullOrWhiteSpace(r.Effect) ? "confetti" : r.Effect,
                        Cost = Math.Max(0, r.Cost),
                        SoundPath = r.SoundPath, ImagePath = r.ImagePath, VideoPath = r.VideoPath,
                        MorphPreset = r.MorphPreset,
                        Volume = Math.Clamp(r.Volume, 0, 1),
                        Hotkey = r.Hotkey,
                    }).ToList();

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // Start from what's saved and change only the fields this window edits. Building a
            // fresh object here used to reset settings owned by other windows — notably the
            // Gift Alerts tiers (and their sounds) — back to defaults on every save.
            var f = SettingsService.LoadChatFeatures();
            f.ShowCount = ShowCountCb.IsChecked == true;
            f.CountPerSource = PerSourceCb.IsChecked == true;
            f.CountOnOverlay = CountOverlayCb.IsChecked == true;
            f.ShowChattersOnOverlay = ChattersOverlayCb.IsChecked == true;
            f.ReplyInChat = ReplyCb.IsChecked == true;
            f.PostClips = PostClipsCb.IsChecked == true;
            f.DiscordWebhook = (DiscordWebhookBox.Password ?? string.Empty).Trim();
            f.BotIngestUrl = (BotIngestUrlBox.Text ?? string.Empty).Trim();
            f.BotIngestToken = (BotIngestTokenBox.Password ?? string.Empty).Trim();
            f.RpgEnabled = RpgEnabledCb.IsChecked == true;
            f.LurkMinutes = ParseInt(LurkBox.Text, 5, 1, 720);
            f.RemoveMinutes = ParseInt(RemoveBox.Text, 15, 1, 1440);
            f.PointsEnabled = PointsCb.IsChecked == true;
            f.PointsName = string.IsNullOrWhiteSpace(PointsNameBox.Text) ? "Points" : PointsNameBox.Text.Trim();
            f.PointsIntervalMinutes = ParseInt(PointsIntervalBox.Text, 5, 1, 720);
            f.PointsPerInterval = ParseInt(PointsAmountBox.Text, 10, 1, 1000000);
            f.FirstChatterBonus = ParseInt(FirstBonusBox.Text, 50, 0, 1000000);
            f.StreakBonusPerDay = ParseInt(StreakBonusBox.Text, 10, 0, 1000000);
            f.BalanceCommand = NormalizeCommand(BalanceCmdBox.Text);
            f.ChatStyle = (StyleCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string ?? "log";
            f.BoxColors = _colors.Select(c => c.Hex.Trim()).Where(IsHex).Take(10).ToList();
            f.Redeems = BuildRedeems();
            f.VideoAudioThroughApp = VideoAudioAppCb.IsChecked == true;
            if (f.BoxColors.Count == 0) f.BoxColors = new ChatFeatureSettings().BoxColors;

            SettingsService.SaveChatFeatures(f);
            DialogResult = true;
        }

        private async void TestBot_Click(object sender, RoutedEventArgs e)
        {
            var url = (BotIngestUrlBox.Text ?? string.Empty).Trim();
            var token = (BotIngestTokenBox.Password ?? string.Empty).Trim();
            if (url.Length == 0 || token.Length == 0)
            {
                StatusText.Text = "Enter the bot ingest URL and token first (from /setup ingest).";
                return;
            }

            TestBotBtn.IsEnabled = false;
            StatusText.Text = "Testing bot connection…";
            try
            {
                var result = await DiscordService.VerifyBotAsync(url, token);
                StatusText.Text = result switch
                {
                    DiscordService.BotTest.Ok =>
                        "✅ Bot is working — a test message was posted to your clips channel. Check Discord!",
                    DiscordService.BotTest.NoClipChannel =>
                        "⚠️ Bot & token OK, but no clips channel set. Run /setup clips in Discord.",
                    DiscordService.BotTest.BadToken =>
                        "❌ Reached the bot, but the token is wrong. Re-copy it from /setup ingest.",
                    DiscordService.BotTest.Unreachable =>
                        "❌ Can't reach the bot. Is it running, and is the URL correct?",
                    _ => "Enter the bot ingest URL and token first (from /setup ingest).",
                };
            }
            finally { TestBotBtn.IsEnabled = true; }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private static string NormalizeCommand(string s)
        {
            s = (s ?? string.Empty).Trim();
            if (s.Length == 0) return "!points";
            if (!s.StartsWith('!')) s = "!" + s;
            return s.Split(' ')[0];   // single word
        }

        private static int ParseInt(string s, int fallback, int min, int max) =>
            int.TryParse((s ?? "").Trim(), out int v) ? Math.Clamp(v, min, max) : fallback;

        private static bool IsHex(string s) =>
            !string.IsNullOrEmpty(s) && s.Length == 7 && s[0] == '#' &&
            s.Skip(1).All(Uri.IsHexDigit);

        public class ColorItem : INotifyPropertyChanged
        {
            private string _hex = "#7cc44a";
            public string Hex
            {
                get => _hex;
                set { _hex = value; Raise(nameof(Hex)); Raise(nameof(Brush)); }
            }
            public Brush Brush
            {
                get
                {
                    try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(_hex)); }
                    catch { return Brushes.Transparent; }
                }
            }
            public event PropertyChangedEventHandler? PropertyChanged;
            private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }

        public class RedeemItem : INotifyPropertyChanged
        {
            private string _command = string.Empty, _effect = "confetti", _sound = string.Empty, _image = string.Empty, _video = string.Empty;
            private string _morph = "";
            private int _cost = 100;
            private double _volume = 1.0;

            public string MorphPreset
            {
                get => _morph;
                set { _morph = value ?? ""; Raise(nameof(MorphPreset)); }
            }

            /// <summary>Carried through the editor so Settings → Hotkeys assignments survive a save.</summary>
            public HotkeyBinding? Hotkey { get; set; }

            public string Command { get => _command; set { _command = value; Raise(nameof(Command)); } }
            public string Effect { get => _effect; set { _effect = value; Raise(nameof(Effect)); } }
            public int Cost { get => _cost; set { _cost = value; Raise(nameof(Cost)); } }
            public double Volume { get => _volume; set { _volume = value; Raise(nameof(Volume)); } }
            public string SoundPath
            {
                get => _sound;
                set { _sound = value; Raise(nameof(SoundPath)); Raise(nameof(SoundName)); }
            }
            public string ImagePath
            {
                get => _image;
                set { _image = value; Raise(nameof(ImagePath)); Raise(nameof(ImageName)); }
            }
            public string VideoPath
            {
                get => _video;
                set { _video = value; Raise(nameof(VideoPath)); Raise(nameof(VideoName)); }
            }
            public string SoundName => string.IsNullOrWhiteSpace(_sound) ? "(none)" : Path.GetFileName(_sound);
            public string ImageName => string.IsNullOrWhiteSpace(_image) ? "(none)" : Path.GetFileName(_image);
            public string VideoName => string.IsNullOrWhiteSpace(_video) ? "(none)" : Path.GetFileName(_video);

            public event PropertyChangedEventHandler? PropertyChanged;
            private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }
    }
}
