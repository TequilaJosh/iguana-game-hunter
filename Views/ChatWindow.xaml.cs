using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GameTracker.Models;
using GameTracker.Services;
using GameTracker.Services.Chat;

namespace GameTracker.Views
{
    /// <summary>Merged live-chat window (read-only): Twitch, Social Stream Ninja, Restream.</summary>
    public partial class ChatWindow : Window
    {
        private const int MaxMessages = 400;
        private int _overlayLines = 20;   // how many recent messages feed the overlay (user-set)

        private readonly TwitchChatConnector _twitch = new();
        private readonly SocialStreamConnector _ssn = new();
        private readonly RestreamConnector _restream = new();
        private readonly ObservableCollection<ChatRow> _rows = new();
        private readonly List<ChatMessage> _recent = new();
        private readonly DispatcherTimer _overlayTimer;
        private bool _pinned;
        private bool _collapsed;
        private bool _chatDirty;

        private readonly SoundService _sound = new();
        private readonly TtsService _tts = new();
        private readonly ChatterVoiceService _voices = new();
        private ChatTtsSettings _ttsSettings = new();

        /// <summary>The live chat window, if open — lets Settings push changes to it.</summary>
        public static ChatWindow? Current { get; private set; }

        private bool _docsMode;

        /// <summary>Docs capture: placeholder values instead of personal ones, and never
        /// persist anything on close (the placeholders must not overwrite real settings).</summary>
        public void SanitizeForDocs()
        {
            _docsMode = true;
            TwitchBox.Text = "yourchannel";
            SsnBox.Text = "your-session-id";
            RestreamBox.Text = string.Empty;
        }

        private const string AllChats = "All chats";
        private readonly ObservableCollection<string> _sendTargets = new() { AllChats };

        private ChatFeatureSettings _features = new();
        private readonly ChattersService _chattersSvc = new();
        private readonly StreakService _streaks = new();
        private DispatcherTimer? _chattersTimer;
        private ChattersWindow? _chattersWindow;
        private bool _chattersDirty;
        private int _boxColorIdx;

        /// <summary>Raised when a viewer runs "!request &lt;game&gt;" — (game title, requester, platform).</summary>
        public Action<string, string, string>? OnGameRequested;

        /// <summary>Raised when a clip happens in chat (!clip or a clip link) — (user, note/url).</summary>
        public Action<string, string>? OnClip;

        // Twitch clip links dropped in chat (clips.twitch.tv/… or twitch.tv/<ch>/clip/<id>),
        // with or without the https:// prefix.
        private static readonly System.Text.RegularExpressions.Regex ClipLinkRx = new(
            @"(?:https?://)?(?:clips\.twitch\.tv/[\w-]+|(?:www\.)?twitch\.tv/[\w-]+/clip/[\w-]+)",
            System.Text.RegularExpressions.RegexOptions.Compiled |
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        private static readonly Brush DefaultUser = Frozen("#a8c488");

        public ChatWindow()
        {
            InitializeComponent();
            MessageList.ItemsSource = _rows;

            Wire(_twitch, s => TwitchStatus.Text = s);
            Wire(_ssn, s => SsnStatus.Text = s);
            Wire(_restream, s => RestreamStatus.Text = s);

            // Discover send targets from the platforms flowing through SSN.
            _ssn.MessageReceived += m => Dispatcher.Invoke(() => AddSendTarget(m.Platform));

            Current = this;

            // Restore saved connection details (the SSN session ID is persistent).
            var saved = SettingsService.LoadChat();
            TwitchBox.Text = saved.TwitchChannel;
            SsnBox.Text = saved.SsnSession;
            RestreamBox.Text = saved.RestreamToken;

            // Window transparency (set in Settings) applies to this window.
            Opacity = saved.Opacity is >= 0.25 and <= 1.0 ? saved.Opacity : 1.0;

            // Send-target picker: "All chats" + platforms seen via SSN. Always starts on
            // All chats so a leftover platform pick can't surprise the streamer later.
            SendTargetCombo.ItemsSource = _sendTargets;
            SendTargetCombo.SelectedItem = AllChats;

            _sound.SetAlerts(SettingsService.LoadSoundAlerts());
            UpdateMuteButton();

            _ttsSettings = SettingsService.LoadTts();
            _voices.SetProfiles(TtsService.AllProfiles(_ttsSettings.Custom));
            ApplyBadWordFilter();

            // Chat features: counts, chatters list, points, style, redeems.
            _features = SettingsService.LoadChatFeatures();
            ApplyChatTemplate();
            UpdateChattersUi();
            _chattersTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _chattersTimer.Tick += (_, _) =>
            {
                var (changed, awards) = _chattersSvc.Tick(3, _features);
                if (awards > 0) StreamStatsService.CountPoints(awards * _features.PointsPerInterval);
                if (changed || awards > 0 || _chattersDirty)
                {
                    _chattersDirty = false;
                    UpdateChattersUi();
                }
            };
            _chattersTimer.Start();

            _overlayLines = SettingsService.LoadOverlayChatLines();

            // Refresh the OBS chat.html a few times a second when there's new chat.
            _overlayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _overlayTimer.Tick += (_, _) =>
            {
                if (!_chatDirty) return;
                _chatDirty = false;
                OverlayService.WriteChatHtml(_recent.TakeLast(_overlayLines).ToList());
            };
            _overlayTimer.Start();
        }

        private void Wire(IChatConnector c, Action<string> status)
        {
            c.MessageReceived += OnMessage;
            c.StatusChanged += s => Dispatcher.Invoke(() => status(s));
        }

        private static SolidColorBrush Frozen(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        private void OnMessage(ChatMessage m)
        {
            Dispatcher.Invoke(() =>
            {
                bool atBottom = MsgScroll.VerticalOffset >= MsgScroll.ScrollableHeight - 4;

                _rows.Add(ToRow(m));
                while (_rows.Count > MaxMessages) _rows.RemoveAt(0);

                _recent.Add(m);
                while (_recent.Count > _overlayLines) _recent.RemoveAt(0);
                _chatDirty = true;

                _chattersSvc.OnMessage(m);
                _chattersDirty = true;

                StreamStatsService.CountMessage(m.Platform, m.User);
                AwardPerks(m);
                SpeakMessage(m);
                HandleCommands(m);

                if (atBottom) MsgScroll.ScrollToEnd();
            });
        }

        private void HandleCommands(ChatMessage m)
        {
            var text = m.Text ?? string.Empty;

            // Sound alerts: play if the first word matches a configured command.
            _sound.CheckAndPlay(text);

            // Auto-repost any Twitch clip link dropped in chat to the Discord channel.
            if (_features.PostClips)
            {
                var link = ClipLinkRx.Match(text);
                if (link.Success)
                {
                    var url = link.Value.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? link.Value : "https://" + link.Value;
                    PostClip(m.User ?? string.Empty, url, null, "clip");
                }
            }

            var parts = text.TrimStart().Split(new[] { ' ' }, 2);
            var cmd = parts.Length > 0 ? parts[0].Trim() : string.Empty;
            if (cmd.Length == 0) return;

            // Tavern Tales: forward RPG commands to the Discord bot and relay its reply.
            if (_features.RpgEnabled && IsGameCommand(cmd) &&
                !string.IsNullOrWhiteSpace(_features.BotIngestUrl) &&
                !string.IsNullOrWhiteSpace(_features.BotIngestToken))
            {
                _ = ForwardGameCommand(m, text.TrimStart());
                return;
            }

            // "!clip [note]" -> mark a highlight and post it to Discord.
            if (string.Equals(cmd, "!clip", StringComparison.OrdinalIgnoreCase))
            {
                var note = parts.Length == 2 ? parts[1].Trim() : string.Empty;
                OnClip?.Invoke(m.User ?? string.Empty, note);   // adds a session marker for the recap
                if (_features.PostClips)
                    PostClip(m.User ?? string.Empty, null, string.IsNullOrEmpty(note) ? null : note, "highlight");
                StreamStatsService.CountClip();
                OverlayServer.Toast($"🎬 {m.User} clipped it!", confetti: false);
                return;
            }

            // "!request <game>" -> hand off to the board (deduped there).
            if (parts.Length == 2 && string.Equals(cmd, "!request", StringComparison.OrdinalIgnoreCase))
            {
                var game = parts[1].Trim();
                if (game.Length > 0) OnGameRequested?.Invoke(game, m.User, m.Platform ?? string.Empty);
                return;
            }

            // "!ghhelp [topic]" -> command/redeem/points help, replied to the asker's chat.
            if (string.Equals(cmd, "!ghhelp", StringComparison.OrdinalIgnoreCase))
            {
                HandleHelp(m, parts.Length == 2 ? parts[1].Trim() : string.Empty);
                return;
            }

            // "!vote N" -> poll vote (one per person; revoting switches).
            if (string.Equals(cmd, "!vote", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length == 2 && int.TryParse(parts[1].Trim(), out int opt))
                    PollService.Vote(m.Platform, m.User, opt);
                return;
            }

            // "!points" (or "!<points name>") -> balance toast + optional chat reply.
            if (_features.PointsEnabled && IsBalanceCommand(cmd))
            {
                var bal = PointsService.Get(m.Platform, m.User);
                OverlayServer.Toast($"{m.User} has {bal} {_features.PointsName}");
                SendChatReply($"@{m.User} you have {bal} {_features.PointsName}", m.Platform);
                return;
            }

            // Point redeems -> spend points, fire the effect.
            var idx = _features.Redeems.FindIndex(r =>
                string.Equals(r.Command.Trim(), cmd, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                var r = _features.Redeems[idx];
                if (!_features.PointsEnabled || r.Cost <= 0 ||
                    PointsService.TrySpend(m.Platform, m.User, r.Cost))
                {
                    PointsService.Save();
                    if (!_sound.Muted && !string.IsNullOrWhiteSpace(r.SoundPath))
                        _sound.Play(r.SoundPath, r.Volume);
                    OverlayServer.TriggerEffect(r.Effect,
                        r.Effect == "custom" && !string.IsNullOrWhiteSpace(r.ImagePath) ? "/fx/" + idx : null);
                    if (!string.IsNullOrWhiteSpace(r.VideoPath))
                        OverlayServer.PlayVideo("/fxvideo/" + idx, (int)(Math.Clamp(r.Volume, 0, 1) * 100));
                    if (!string.IsNullOrWhiteSpace(r.MorphPreset))
                        VoiceMorphService.ActivateByName(r.MorphPreset);   // overlay shows the countdown
                    StreamStatsService.CountRedeem();
                    if (!string.IsNullOrWhiteSpace(r.VideoPath)) StreamStatsService.CountVideo();
                    if (!string.IsNullOrWhiteSpace(r.MorphPreset)) StreamStatsService.CountMorph();
                    OverlayServer.Toast($"{m.User} redeemed {r.Command.TrimStart('!')}!", confetti: false);
                    SendChatReply($"@{m.User} redeemed {r.Command.TrimStart('!')}!", m.Platform);
                }
                else
                {
                    var missing = r.Cost - PointsService.Get(m.Platform, m.User);
                    OverlayServer.Toast($"{m.User} needs {missing} more {_features.PointsName} for {r.Command}");
                    SendChatReply($"@{m.User} you need {missing} more {_features.PointsName} for {r.Command}", m.Platform);
                }
            }
        }

        // First-chatter and daily-streak bonuses (need points enabled to mean anything).
        private void AwardPerks(ChatMessage m)
        {
            if (!_features.PointsEnabled || string.IsNullOrWhiteSpace(m.User)) return;
            var (firstChatter, newDay, streak) = _streaks.OnMessage(m.Platform, m.User);

            if (firstChatter && _features.FirstChatterBonus > 0)
            {
                PointsService.Add(m.Platform, m.User, _features.FirstChatterBonus);
                PointsService.Save();
                StreamStatsService.CountPoints(_features.FirstChatterBonus);
                OverlayServer.Toast($"🥇 {m.User} is first in chat! +{_features.FirstChatterBonus} {_features.PointsName}", confetti: true);
            }

            if (newDay && _features.StreakBonusPerDay > 0)
            {
                // Bonus grows with the streak (capped so decade-long streaks stay sane).
                int bonus = _features.StreakBonusPerDay * Math.Min(streak, 10);
                PointsService.Add(m.Platform, m.User, bonus);
                PointsService.Save();
                StreamStatsService.CountPoints(bonus);
                if (streak >= 3)   // only celebrate real streaks to avoid toast spam
                    OverlayServer.Toast($"🔥 {m.User} is on a {streak}-day streak! +{bonus} {_features.PointsName}");
            }
        }

        // The viewer-facing help menu (!ghhelp). Replies in the asker's chat when SSN can
        // send; otherwise falls back to an overlay toast so the answer still shows somewhere.
        private void HandleHelp(ChatMessage m, string topic)
        {
            var balanceCmd = string.IsNullOrWhiteSpace(_features.BalanceCommand)
                ? "!points" : _features.BalanceCommand.Trim();
            var pointsName = _features.PointsName;
            string text;

            switch (topic.ToLowerInvariant())
            {
                case "commands":
                case "command":
                    text = $"Commands: {balanceCmd} = your {pointsName} · !request <game> = suggest a game · " +
                           "!vote <#> = vote in the poll · !ghhelp redeems = spendable rewards";
                    break;

                case "redeems":
                case "redeem":
                case "pointredeems":
                case "rewards":
                    var redeems = _features.Redeems
                        .Where(r => !string.IsNullOrWhiteSpace(r.Command) && r.Command.Trim() != "!")
                        .Take(8)
                        .Select(r => r.Cost > 0 ? $"{r.Command.Trim()} ({r.Cost})" : r.Command.Trim())
                        .ToList();
                    int more = Math.Max(0, _features.Redeems.Count(r => !string.IsNullOrWhiteSpace(r.Command)) - 8);
                    text = redeems.Count == 0
                        ? "No point redeems are set up yet."
                        : $"Spend {pointsName} on: " + string.Join(" · ", redeems) +
                          (more > 0 ? $" (+{more} more)" : "");
                    break;

                case "points":
                case "point":
                    text = $"Chat to earn {_features.PointsPerInterval} {pointsName} every " +
                           $"{_features.PointsIntervalMinutes} min while active. " +
                           (_features.FirstChatterBonus > 0 ? $"First chatter of the stream: +{_features.FirstChatterBonus}. " : "") +
                           (_features.StreakBonusPerDay > 0 ? "Daily streaks pay a growing bonus. " : "") +
                           $"Check yours with {balanceCmd}.";
                    break;

                default:
                    text = "Help topics: !ghhelp commands · !ghhelp redeems · !ghhelp points";
                    break;
            }

            if (_features.ReplyInChat && _ssn.IsConnected)
                SendChatReply($"@{m.User} {text}", m.Platform);
            else
                OverlayServer.Toast(text);
        }

        private bool IsBalanceCommand(string cmd)
        {
            var configured = string.IsNullOrWhiteSpace(_features.BalanceCommand)
                ? "!points" : _features.BalanceCommand.Trim();
            if (string.Equals(cmd, configured, StringComparison.OrdinalIgnoreCase)) return true;
            var custom = "!" + new string(_features.PointsName.Where(char.IsLetterOrDigit).ToArray());
            return custom.Length > 1 && string.Equals(cmd, custom, StringComparison.OrdinalIgnoreCase);
        }

        private void Mute_Click(object sender, RoutedEventArgs e)
        {
            _sound.Muted = !_sound.Muted;
            if (_sound.Muted) { _sound.StopAll(); _tts.StopAll(); }   // panic: kill alerts + TTS
            UpdateMuteButton();
        }

        private void UpdateMuteButton()
        {
            MuteButton.Content = _sound.Muted ? "\U0001F507" : "\U0001F50A";   // 🔇 / 🔊
            MuteButton.Foreground = _sound.Muted
                ? new SolidColorBrush(Color.FromRgb(0xd4, 0x5a, 0x37))         // red when muted
                : new SolidColorBrush(Color.FromRgb(0x7a, 0x90, 0x70));        // gray when active
            MuteButton.ToolTip = _sound.Muted
                ? "Sound alerts muted — click to unmute"
                : "Stop sound alerts (panic): kill the current alert and mute incoming";
        }

        // ---- live updates pushed from the Settings window ----

        /// <summary>Re-read chat features (counts/points/style/redeems) after they're edited elsewhere.</summary>
        public void ReloadFeatures()
        {
            _features = SettingsService.LoadChatFeatures();
            ApplyChatTemplate();
            OverlayServer.SetStyle(_features.ChatStyle, _features.BoxColors);
            UpdateChattersUi();
        }

        /// <summary>Re-read sound-alert bindings after they're edited elsewhere.</summary>
        public void ReloadSoundAlerts() => _sound.SetAlerts(SettingsService.LoadSoundAlerts());

        /// <summary>Re-read text-to-speech settings after they're changed in Settings.</summary>
        public void ReloadTts()
        {
            _ttsSettings = SettingsService.LoadTts();
            _voices.SetProfiles(TtsService.AllProfiles(_ttsSettings.Custom));
            ApplyBadWordFilter();
            if (!_ttsSettings.Enabled) _tts.StopAll();
        }

        // Push runtime TTS config (bad-word filter + output device) into the engine.
        private void ApplyBadWordFilter()
        {
            _tts.BleepBadWords = _ttsSettings.BleepBadWords;
            _tts.SetBadWords(_ttsSettings.BadWords);
            _tts.OutputDevice = _ttsSettings.OutputDevice ?? string.Empty;
        }

        // Known chat/service bots (streamscharts.com/tools/bots) — never read aloud.
        // Built-in and intentionally not surfaced in the UI; the streamer's own
        // "Ignore users" list layers on top of this.
        private static readonly HashSet<string> KnownBots = new(StringComparer.OrdinalIgnoreCase)
        {
            "streamelements", "nightbot", "silent_kev1n", "alex_north_play", "vibing_offline",
            "mmatcha_enjoyer", "kinda_lost_tbh", "casual_tryhard_tapp", "toasst_cruncher",
            "sleepy_slava", "f0x_in_the_city", "vova_chillzone", "cyberdmitry", "mistylunac_",
            "sery_bot", "wizebot", "tangiabot", "kofistreambot", "moobot", "botrixoficial",
            "own3d", "raiim", "creatisbot", "fossabot", "streamlabs", "swiftyspiffy",
            "frostytools",
        };

        // URLs in chat: http(s)/www links plus bare domains like "twitch.tv/name". Stripped
        // from spoken text so TTS never reads out "h t t p s colon slash slash…".
        private static readonly System.Text.RegularExpressions.Regex LinkPattern = new(
            @"(?:https?://|www\.)\S+|\b[\w-]+(?:\.[\w-]+)*\.(?:com|net|org|io|gg|tv|co|me|dev|app|xyz|stream|live|link)\b(?:/\S*)?",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.Compiled);

        // "<user> redeemed <reward> [cost]" — Twitch channel-point announcements relayed as
        // chat text. Only matches the announcement shape (first word = the sender's own name,
        // or an @mention bot reply) so normal sentences like "I redeemed my coupon" still read.
        private static bool IsRedeemAnnouncement(string text, string? user)
        {
            var parts = text.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3 ||
                !parts[1].Equals("redeemed", StringComparison.OrdinalIgnoreCase)) return false;
            if (parts[0].StartsWith('@')) return true;                      // "@viewer redeemed …" (bot echo)
            return parts[0].Equals(user?.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        // Read an incoming message aloud, honoring the TTS options.
        private void SpeakMessage(ChatMessage m)
        {
            if (!_ttsSettings.Enabled || _sound.Muted) return;   // panic mute also silences TTS
            var text = (m.Text ?? string.Empty).Trim();
            if (text.Length == 0) return;
            if (_ttsSettings.SkipCommands && text.StartsWith("!", StringComparison.Ordinal)) return;

            // Ignore rules: known bots, muted users, muted keywords, redeem announcements.
            if (KnownBots.Contains((m.User ?? string.Empty).Trim())) return;
            if (_ttsSettings.IgnoreUsers.Any(u =>
                    string.Equals(u?.Trim(), m.User?.Trim(), StringComparison.OrdinalIgnoreCase)))
                return;
            if (_ttsSettings.IgnoreKeywords.Any(k =>
                    !string.IsNullOrWhiteSpace(k) &&
                    text.Contains(k.Trim(), StringComparison.OrdinalIgnoreCase)))
                return;
            if (_ttsSettings.SkipRedeemMessages && IsRedeemAnnouncement(text, m.User)) return;

            // Links: read the message without them; if nothing but links, say nothing.
            if (_ttsSettings.SkipLinks && LinkPattern.IsMatch(text))
            {
                text = LinkPattern.Replace(text, " ").Trim();
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\s{2,}", " ");
                if (text.Length == 0) return;
            }
            if (_ttsSettings.MaxChars > 0 && text.Length > _ttsSettings.MaxChars)
                text = text[.._ttsSettings.MaxChars];
            var spoken = _ttsSettings.ReadName && !string.IsNullOrWhiteSpace(m.User)
                ? $"{m.User} says: {text}"
                : text;

            string voice, effect;
            if (_ttsSettings.PerChatterVoices)
                (voice, effect) = _voices.For(m.Platform, m.User ?? "");   // same person → same voice, saved
            else { voice = _ttsSettings.Voice; effect = _ttsSettings.Effect; }

            _tts.Speak(spoken, voice, effect, _ttsSettings.Rate, _ttsSettings.Volume);
        }

        /// <summary>Re-read the overlay chat-line count after it's changed in Settings.</summary>
        public void ReloadOverlayLines() => _overlayLines = SettingsService.LoadOverlayChatLines();

        /// <summary>Apply live window transparency from the Settings slider.</summary>
        public void ApplyChatOpacity(double value) => Opacity = Math.Clamp(value, 0.25, 1.0);

        private int _zebra;
        private static readonly Brush ZebraNone = Brushes.Transparent;
        private static readonly Brush ZebraDark = Frozen("#22000000");

        private ChatRow ToRow(ChatMessage m)
        {
            Brush userBrush = DefaultUser;
            if (!string.IsNullOrWhiteSpace(m.UserColor))
            {
                try { userBrush = Frozen(m.UserColor); } catch { /* keep default */ }
            }

            Brush symbolBrush = DefaultUser;
            try { symbolBrush = Frozen(OverlayService.ChatColorHex(m.Platform)); } catch { }

            // Avatar: photo if provided, otherwise a colored circle with the user's initial.
            Brush avatarBrush = symbolBrush;
            string initial = string.Empty;
            bool hasPhoto = false;
            if (IsHttp(m.AvatarUrl))
            {
                try { avatarBrush = AvatarImage(m.AvatarUrl); hasPhoto = true; } catch { }
            }
            if (!hasPhoto) initial = FirstLetter(m.User);

            Brush boxBrush = DefaultUser;
            if (_features.BoxColors.Count > 0)
            {
                try { boxBrush = Frozen(_features.BoxColors[_boxColorIdx++ % _features.BoxColors.Count]); }
                catch { }
            }

            return new ChatRow
            {
                Symbol = OverlayService.ChatSymbol(m.Platform),
                SymbolBrush = symbolBrush,
                User = m.User,
                Segments = m.Segments,
                Badges = m.Badges ?? new List<ChatBadge>(),
                UserBrush = userBrush,
                AvatarBrush = avatarBrush,
                Initial = initial,
                RowBrush = (_zebra++ % 2 == 0) ? ZebraNone : ZebraDark,
                BoxBrush = boxBrush,
            };
        }

        // ---- chat features (counts / chatters / points / style) ----

        private void ApplyChatTemplate()
        {
            var key = _features.ChatStyle == "boxes" ? "BoxRow" : "LogRow";
            if (MessageList.Resources[key] is DataTemplate t)
                MessageList.ItemTemplate = t;
        }

        private void UpdateChattersUi()
        {
            // In-app count bar.
            CountBar.Visibility = _features.ShowCount ? Visibility.Visible : Visibility.Collapsed;
            if (_features.ShowCount)
            {
                CountText.Inlines.Clear();
                CountText.Inlines.Add(new System.Windows.Documents.Run("\U0001F465 ")
                { Foreground = Frozen("#7a9070") });
                if (_features.CountPerSource)
                {
                    bool first = true;
                    foreach (var (platform, n) in _chattersSvc.PerSource().OrderBy(kv => kv.Key))
                    {
                        if (!first) CountText.Inlines.Add(new System.Windows.Documents.Run("   "));
                        first = false;
                        Brush b = DefaultUser;
                        try { b = Frozen(OverlayService.ChatColorHex(platform)); } catch { }
                        CountText.Inlines.Add(new System.Windows.Documents.Run(
                            OverlayService.ChatSymbol(platform) + " " + n)
                        { Foreground = b, FontWeight = FontWeights.Bold });
                    }
                    if (first) CountText.Inlines.Add(new System.Windows.Documents.Run("0 chatting")
                    { Foreground = Frozen("#7a9070") });
                }
                else
                {
                    CountText.Inlines.Add(new System.Windows.Documents.Run(
                        _chattersSvc.Count + " chatting")
                    { Foreground = Frozen("#a8c488"), FontWeight = FontWeights.Bold });
                }
            }

            // In-app chatters window.
            _chattersWindow?.Refresh(_chattersSvc.Snapshot(), _features);

            // Overlay.
            OverlayServer.SetChatters(BuildChattersPayload());
        }

        private object BuildChattersPayload() => new
        {
            showCount = _features.ShowCount,
            onOverlay = _features.CountOnOverlay,
            perSourceMode = _features.CountPerSource,
            total = _chattersSvc.Count,
            perSource = _chattersSvc.PerSource().OrderBy(kv => kv.Key).Select(kv => new
            {
                platform = kv.Key,
                symbol = OverlayService.ChatSymbol(kv.Key),
                symbolColor = OverlayService.ChatColorHex(kv.Key),
                count = kv.Value,
            }).ToArray(),
            showList = _features.ShowChattersOnOverlay,
            list = _chattersSvc.Snapshot().Select(c => new
            {
                u = c.User,
                s = c.State == ChatterState.Lurking ? "l" : "a",
                symbol = OverlayService.ChatSymbol(c.Platform),
                symbolColor = OverlayService.ChatColorHex(c.Platform),
            }).ToArray(),
        };

        private PollWindow? _pollWindow;

        private void Poll_Click(object sender, RoutedEventArgs e)
        {
            if (_pollWindow != null) { _pollWindow.Activate(); return; }
            _pollWindow = new PollWindow { Owner = this };
            _pollWindow.Closed += (_, _) => _pollWindow = null;
            _pollWindow.Show();
        }

        private StreamStatsWindow? _statsWindow;

        private void Stats_Click(object sender, RoutedEventArgs e)
        {
            if (_statsWindow != null) { _statsWindow.Activate(); return; }
            _statsWindow = new StreamStatsWindow { Owner = this };
            _statsWindow.Closed += (_, _) => _statsWindow = null;
            _statsWindow.Show();
        }

        private void Chatters_Click(object sender, RoutedEventArgs e)
        {
            if (_chattersWindow != null) { _chattersWindow.Activate(); return; }
            _chattersWindow = new ChattersWindow { Owner = this };
            _chattersWindow.Closed += (_, _) => _chattersWindow = null;
            _chattersWindow.Refresh(_chattersSvc.Snapshot(), _features);
            _chattersWindow.Show();
        }

        private static bool IsHttp(string? s) =>
            !string.IsNullOrWhiteSpace(s) &&
            (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
             s.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

        private static string FirstLetter(string? user) =>
            string.IsNullOrWhiteSpace(user) ? "?" : user.Trim()[0].ToString().ToUpperInvariant();

        private static ImageBrush AvatarImage(string url)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(url, UriKind.Absolute);
            bmp.DecodePixelWidth = 44;
            bmp.EndInit();
            return new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
        }

        // Persist connection fields, preserving auto-connect/opacity (now owned by Settings).
        private void SaveChat()
        {
            if (_docsMode) return;   // docs screenshots must never overwrite real settings
            var s = SettingsService.LoadChat();
            s.TwitchChannel = TwitchBox.Text.Trim();
            s.SsnSession = SsnBox.Text.Trim();
            s.RestreamToken = RestreamBox.Text.Trim();
            SettingsService.SaveChat(s);
        }

        private void SsnGuide_Click(object sender, RoutedEventArgs e) =>
            new SsnGuideWindow { Owner = this }.ShowDialog();

        // ---- two-way chat (outgoing via Social Stream Ninja) ----

        /// <summary>
        /// Send an @mention reply through SSN if "Reply in chat" is enabled and SSN is
        /// connected. When <paramref name="platform"/> is a real platform name, the reply
        /// goes only to that chat so the others aren't spammed. Fire-and-forget.
        /// </summary>
        public void SendChatReply(string text, string? platform = null)
        {
            if (!_features.ReplyInChat || !_ssn.IsConnected || string.IsNullOrWhiteSpace(text)) return;
            _ = _ssn.SendChatAsync(text, ReplyTarget(platform));
        }

        // Tavern Tales command words that get forwarded to the bot's /game bridge.
        private static readonly HashSet<string> GameCommands = new(StringComparer.OrdinalIgnoreCase)
        {
            "!play", "!link", "!confirm", "!create", "!char", "!sheet", "!me", "!skills",
            "!zones", "!classes", "!races", "!adventure", "!explore", "!hunt",
            "!attack", "!skill", "!cast", "!use", "!potion", "!flee", "!run",
            "!status", "!inv", "!inventory", "!bag", "!equip", "!rest",
            "!leaderboard", "!rpg", "!tavern", "!deletechar", "!help", "!commands",
        };

        private static bool IsGameCommand(string cmd) => GameCommands.Contains(cmd.Trim());

        // Send a chat game command to the bot and relay its one-line reply to the chatter.
        private async System.Threading.Tasks.Task ForwardGameCommand(ChatMessage m, string text)
        {
            var reply = await DiscordService.PlayGameAsync(
                _features.BotIngestUrl, _features.BotIngestToken,
                string.IsNullOrWhiteSpace(m.Platform) ? "chat" : m.Platform, m.User ?? string.Empty, text);
            if (!string.IsNullOrWhiteSpace(reply))
                SendChatReply($"@{m.User} {reply}", m.Platform);
        }

        // Post a clip to Discord (fire-and-forget). Prefers the companion bot's ingest
        // endpoint when configured; otherwise falls back to a plain channel webhook.
        private void PostClip(string user, string? url, string? note, string type)
        {
            if (!string.IsNullOrWhiteSpace(_features.BotIngestUrl) &&
                !string.IsNullOrWhiteSpace(_features.BotIngestToken))
            {
                _ = DiscordService.PostClipToBotAsync(_features.BotIngestUrl, _features.BotIngestToken,
                    new { user, url, note, type });
                return;
            }
            if (DiscordService.LooksLikeWebhook(_features.DiscordWebhook))
            {
                var tail = string.IsNullOrEmpty(note) ? "" : $" — {note}";
                var body = url != null
                    ? $"🎬 **{user}** shared a clip{tail}:\n{url}"
                    : $"🎬 **{user}** clipped this moment{(string.IsNullOrEmpty(note) ? "" : $": {note}")} — {DateTime.Now:h:mm tt}";
                _ = DiscordService.PostAsync(_features.DiscordWebhook, body);
            }
        }

        // Map a message's platform to an SSN send target. Unknown/aggregate sources
        // ("social", "restream", empty) have no reliable SSN label -> send to all.
        private static string? ReplyTarget(string? platform)
        {
            var p = (platform ?? string.Empty).Trim().ToLowerInvariant();
            return p.Length == 0 || p == "social" || p == "restream" ? null : p;
        }

        private void AddSendTarget(string? platform)
        {
            platform = (platform ?? string.Empty).Trim().ToLowerInvariant();
            if (platform.Length == 0 || platform == "social") return;
            if (!_sendTargets.Contains(platform)) _sendTargets.Add(platform);
        }

        private string? SelectedSendTarget()
        {
            var t = SendTargetCombo.SelectedItem as string;
            return string.IsNullOrWhiteSpace(t) || t == AllChats ? null : t;
        }

        private void SendTarget_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // Selection is intentionally session-only (always defaults back to All chats).
        }

        private void SendBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter) { _ = SendMessageAsync(); e.Handled = true; }
        }

        private void Send_Click(object sender, RoutedEventArgs e) => _ = SendMessageAsync();

        private async System.Threading.Tasks.Task SendMessageAsync()
        {
            var text = SendBox.Text.Trim();
            if (text.Length == 0) return;

            if (!_ssn.IsConnected)
            {
                SsnStatus.Text = "Connect Social Stream Ninja to send messages.";
                return;
            }

            SendBtn.IsEnabled = false;
            try
            {
                var target = SelectedSendTarget();
                bool ok = await _ssn.SendChatAsync(text, target);
                if (ok)
                {
                    SendBox.Clear();   // the message echoes back through SSN's chat feed
                    SsnStatus.Text = target == null ? "Sent to all chats." : $"Sent to {target}.";
                }
                else
                {
                    SsnStatus.Text = "Couldn't send — check SSN is running with remote API control on.";
                }
            }
            finally { SendBtn.IsEnabled = true; }
        }

        /// <summary>Connect every source that has a saved value and isn't already connected.</summary>
        public void ConnectSaved()
        {
            if (!string.IsNullOrWhiteSpace(TwitchBox.Text) && !_twitch.IsConnected)
                Twitch_Click(this, new RoutedEventArgs());
            if (!string.IsNullOrWhiteSpace(SsnBox.Text) && !_ssn.IsConnected)
                Ssn_Click(this, new RoutedEventArgs());
            if (!string.IsNullOrWhiteSpace(RestreamBox.Text) && !_restream.IsConnected)
                Restream_Click(this, new RoutedEventArgs());
        }

        private async void Twitch_Click(object sender, RoutedEventArgs e)
        {
            if (_twitch.IsConnected) { await _twitch.DisconnectAsync(); TwitchBtn.Content = "Connect"; }
            else { SaveChat(); TwitchBtn.Content = "Disconnect"; await _twitch.ConnectAsync(TwitchBox.Text); }
        }

        private async void Ssn_Click(object sender, RoutedEventArgs e)
        {
            if (_ssn.IsConnected) { await _ssn.DisconnectAsync(); SsnBtn.Content = "Connect"; }
            else { SaveChat(); SsnBtn.Content = "Disconnect"; await _ssn.ConnectAsync(SsnBox.Text); }
        }

        private async void Restream_Click(object sender, RoutedEventArgs e)
        {
            if (_restream.IsConnected) { await _restream.DisconnectAsync(); RestreamBtn.Content = "Connect"; }
            else { SaveChat(); RestreamBtn.Content = "Disconnect"; await _restream.ConnectAsync(RestreamBox.Text); }
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

        private void Collapse_Click(object sender, RoutedEventArgs e)
        {
            _collapsed = !_collapsed;
            ConnectPanel.Visibility = _collapsed ? Visibility.Collapsed : Visibility.Visible;
            CollapseButton.Content = _collapsed ? "▼" : "▲"; // ▼ / ▲
            CollapseButton.ToolTip = _collapsed ? "Show the connect bar" : "Hide the connect bar (clean chat box)";
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosed(EventArgs e)
        {
            if (ReferenceEquals(Current, this)) Current = null;
            SaveChat();
            _chattersTimer?.Stop();
            PointsService.Save();
            _overlayTimer.Stop();
            OverlayService.ClearChatHtml();
            _tts.Dispose();
            _twitch.Dispose();
            _ssn.Dispose();
            _restream.Dispose();
            base.OnClosed(e);
        }

        public class ChatRow
        {
            public string Symbol { get; set; } = string.Empty;
            public Brush SymbolBrush { get; set; } = Brushes.Gray;
            public string User { get; set; } = string.Empty;
            public List<ChatSegment> Segments { get; set; } = new();
            public List<ChatBadge> Badges { get; set; } = new();
            public Brush UserBrush { get; set; } = Brushes.White;
            public Brush AvatarBrush { get; set; } = Brushes.Gray;
            public string Initial { get; set; } = string.Empty;
            public Brush RowBrush { get; set; } = Brushes.Transparent;
            public Brush BoxBrush { get; set; } = Brushes.DarkOliveGreen;
        }
    }
}
