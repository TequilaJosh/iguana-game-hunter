using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GameTracker.Models;
using GameTracker.Services;

namespace GameTracker.Views
{
    /// <summary>Consolidated settings hub: Appearance (theme), Chat, and Overlay.</summary>
    public partial class SettingsWindow : Window
    {
        private ThemeSettings _theme;
        private bool _ready;
        private TextPanelsWindow? _textPanelsWindow;
        private readonly TtsService _ttsTest = new();
        private const string DefaultOutputLabel = "Default (system output)";
        private List<VoiceProfile> _profiles = new();

        // The chosen TTS output device ("" = system default).
        private string SelectedTtsOutput()
        {
            var s = TtsOutput.SelectedItem as string;
            return string.IsNullOrEmpty(s) || s == DefaultOutputLabel ? string.Empty : s;
        }
        private readonly List<(string label, Func<ThemeSettings, string> get, Action<ThemeSettings, string> set)> _slots;

        public SettingsWindow()
        {
            InitializeComponent();
            _theme = Clone(ThemeService.Current);

            _slots = new()
            {
                ("Accent",           t => t.Accent,     (t, v) => t.Accent = v),
                ("Accent (deep)",    t => t.AccentDeep, (t, v) => t.AccentDeep = v),
                ("Accent 2 (amber)", t => t.Accent2,    (t, v) => t.Accent2 = v),
                ("Background",       t => t.BgBase,     (t, v) => t.BgBase = v),
                ("Background lines", t => t.BgTile,     (t, v) => t.BgTile = v),
            };

            PresetList.ItemsSource = BuildPresetVms();
            BuildCustomRows();

            // Chat
            var chat = SettingsService.LoadChat();
            AutoConnectCheck.IsChecked = chat.AutoConnect;
            OpacitySlider.Value = chat.Opacity is >= 0.25 and <= 1.0 ? chat.Opacity : 1.0;

            // Text to speech
            var tts = SettingsService.LoadTts();
            _profiles = TtsService.AllProfiles(tts.Custom);
            TtsVoice.ItemsSource = _profiles;
            var match = _profiles.FirstOrDefault(p => p.Voice == tts.Voice && p.Effect == tts.Effect)
                        ?? _profiles.FirstOrDefault();
            TtsVoice.SelectedItem = match;
            TtsOutput.Items.Clear();
            TtsOutput.Items.Add(DefaultOutputLabel);
            foreach (var d in TtsService.OutputDevices()) TtsOutput.Items.Add(d);
            TtsOutput.SelectedItem = !string.IsNullOrEmpty(tts.OutputDevice) && TtsOutput.Items.Contains(tts.OutputDevice)
                ? tts.OutputDevice : DefaultOutputLabel;
            TtsEnabled.IsChecked = tts.Enabled;
            TtsPerChatter.IsChecked = tts.PerChatterVoices;
            TtsRate.Value = tts.Rate;
            TtsVolume.Value = tts.Volume;
            TtsReadName.IsChecked = tts.ReadName;
            TtsSkipCommands.IsChecked = tts.SkipCommands;
            TtsSkipRedeems.IsChecked = tts.SkipRedeemMessages;
            TtsSkipLinks.IsChecked = tts.SkipLinks;
            TtsBleepBadWords.IsChecked = tts.BleepBadWords;
            TtsIgnoreUsers.Text = string.Join(", ", tts.IgnoreUsers);
            TtsIgnoreKeywords.Text = string.Join(", ", tts.IgnoreKeywords);
            TtsBadWords.Text = string.Join(", ", tts.BadWords);
            UpdateVoicePickerState();

            // Overlay
            PortBox.Text = OverlayServer.Port.ToString();
            ChatLinesBox.Text = SettingsService.LoadOverlayChatLines().ToString();
            LoadTickerControls();
            UpdateOverlayStatus();

            _ready = true;
        }

        // ---- left nav ----

        private void Nav_Click(object sender, RoutedEventArgs e)
        {
            NavAppearance.Tag = NavChat.Tag = NavOverlay.Tag = NavHelp.Tag = NavBackup.Tag = NavHotkeys.Tag = null;
            PanelAppearance.Visibility = PanelChat.Visibility = PanelOverlay.Visibility =
                PanelHelp.Visibility = PanelBackup.Visibility = PanelHotkeys.Visibility = Visibility.Collapsed;

            if (sender == NavChat) { NavChat.Tag = "active"; PanelChat.Visibility = Visibility.Visible; }
            else if (sender == NavOverlay) { NavOverlay.Tag = "active"; PanelOverlay.Visibility = Visibility.Visible; }
            else if (sender == NavBackup) { NavBackup.Tag = "active"; PanelBackup.Visibility = Visibility.Visible; }
            else if (sender == NavHotkeys)
            {
                NavHotkeys.Tag = "active"; PanelHotkeys.Visibility = Visibility.Visible;
                BuildHotkeys();
            }
            else if (sender == NavHelp)
            {
                NavHelp.Tag = "active"; PanelHelp.Visibility = Visibility.Visible;
                BuildHelp();
            }
            else { NavAppearance.Tag = "active"; PanelAppearance.Visibility = Visibility.Visible; }
        }

        // ---- how-to guides ----

        private bool _helpBuilt;

        private void BuildHelp()
        {
            if (_helpBuilt) return;
            _helpBuilt = true;

            // Collapsible sections: a clickable "▸ title" header revealing its steps.
            var sections = new List<(TextBlock header, StackPanel panel, string title)>();
            StackPanel? current = null;

            void SetOpen((TextBlock header, StackPanel panel, string title) s, bool open)
            {
                s.panel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                s.header.Text = (open ? "▾  " : "▸  ") + s.title;
            }

            void Section(string title)
            {
                var header = new TextBlock
                {
                    Text = "▸  " + title,
                    Foreground = (System.Windows.Media.Brush)FindResource("ThemeAccent"),
                    FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 3),
                    TextWrapping = TextWrapping.Wrap,
                    Cursor = System.Windows.Input.Cursors.Hand,
                };
                var panel = new StackPanel
                {
                    Visibility = Visibility.Collapsed,
                    Margin = new Thickness(6, 0, 0, 6),
                };
                var entry = (header, panel, title);
                sections.Add(entry);
                header.MouseLeftButtonUp += (_, _) =>
                    SetOpen(entry, panel.Visibility != Visibility.Visible);
                HelpContent.Children.Add(header);
                HelpContent.Children.Add(panel);
                current = panel;
            }

            void Add(UIElement el)
            {
                if (current != null) current.Children.Add(el);
                else HelpContent.Children.Add(el);
            }
            void Body(string text)
            {
                Add(new TextBlock
                {
                    Text = text,
                    Foreground = Brush("#c4d4a8"), FontSize = 12, TextWrapping = TextWrapping.Wrap,
                    LineHeight = 17, Margin = new Thickness(0, 0, 0, 4),
                });
            }
            void Step(int n, string text)
            {
                var tb = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap, FontSize = 12, LineHeight = 17,
                    Margin = new Thickness(8, 1, 0, 1),
                };
                tb.Inlines.Add(new System.Windows.Documents.Run($"{n}.  ")
                {
                    Foreground = (System.Windows.Media.Brush)FindResource("ThemeAccent"),
                    FontWeight = FontWeights.Bold,
                });
                tb.Inlines.Add(new System.Windows.Documents.Run(text) { Foreground = Brush("#e8e0c4") });
                Add(tb);
            }
            void Img(string file)
            {
                try
                {
                    var img = new System.Windows.Controls.Image
                    {
                        Source = new System.Windows.Media.Imaging.BitmapImage(
                            new Uri($"pack://application:,,,/Docs/Screens/{file}")),
                        MaxWidth = 420,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Stretch = System.Windows.Media.Stretch.Uniform,
                    };
                    System.Windows.Media.RenderOptions.SetBitmapScalingMode(
                        img, System.Windows.Media.BitmapScalingMode.HighQuality);
                    Add(new Border
                    {
                        Child = img,
                        BorderBrush = (System.Windows.Media.Brush)FindResource("ThemeBorder"),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Margin = new Thickness(8, 6, 0, 6),
                        HorizontalAlignment = HorizontalAlignment.Left,
                    });
                }
                catch { /* screenshot missing — text still stands alone */ }
            }

            HelpContent.Children.Add(new TextBlock
            {
                Text = "How to use LazerGuanas Game Hunter",
                Foreground = Brush("#e8e0c4"), FontSize = 16, FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 2),
            });
            HelpContent.Children.Add(new TextBlock
            {
                Text = "Click a section to expand it. Hover most controls for a tooltip too.",
                Foreground = Brush("#7a9070"), FontSize = 11, TextWrapping = TextWrapping.Wrap,
            });

            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 4),
            };
            Button MiniBtn(string label)
            {
                var b = new Button { Content = label, Style = (Style)FindResource("Btn"),
                                     Margin = new Thickness(0, 0, 7, 0), Padding = new Thickness(9, 3, 9, 3) };
                btnRow.Children.Add(b);
                return b;
            }
            MiniBtn("Expand all").Click += (_, _) => { foreach (var s in sections) SetOpen(s, true); };
            MiniBtn("Collapse all").Click += (_, _) => { foreach (var s in sections) SetOpen(s, false); };
            HelpContent.Children.Add(btnRow);

            Section("The board (Dormant / Hunting / Devoured)");
            Body("Your games live in three columns, and a game's whole life happens by moving across them:");
            Step(1, "DORMANT is the backlog — games you own or plan to play but haven't started.");
            Step(2, "HUNTING is what you're actively playing. Press ▶ Start on a card to begin a timed session; the card moves here automatically.");
            Step(3, "DEVOURED is finished games. Drag a card there (or right-click → Move to DEVOURED) and give it a ★ rating from the card.");
            Step(4, "Drag cards between columns any time, right-click a card for the full menu, and click a card to open its details, notes, and rating.");

            Section("Adding games");
            Step(1, "Click + Add Game (top-right) and fill in the title — platform, who suggested it, and a suggestion type are optional but searchable later.");
            Step(2, "Viewers can add games for you: anyone typing !request <game> in chat adds it straight to Dormant (duplicates are ignored), with a confirmation toast on the overlay.");

            Section("Sessions & the stream timer");
            Step(1, "Press ▶ Start on a Hunting game. The session timer starts and shows in the Now Playing block on your OBS overlay.");
            Step(2, "Press ■ Stop when you're done — playtime is added to the game's total. Your total hours show on each card.");
            Step(3, "Starting a session also auto-connects your saved chats (toggle in Settings → Chat) and counts the game in your Stream Stats.");

            Section("The challenge wheel");
            Step(1, "Right-click a game → 🎡 Wheel (or use the Spin button in the top bar).");
            Step(2, "Add your own challenge entries, set how many the wheel holds at once (\"max on wheel\"), and hit Randomize to shuffle from your full pool.");
            Step(3, "Spin! Landed challenges are removed from rotation, replaced by unused ones, and appear on the overlay's Challenges block in real time — even before a session starts.");

            Section("Chat — connecting");
            Img("chat.png");
            Step(1, "Open Chat from the top bar. Three sources can run at once — use any or all.");
            Step(2, "TW (Twitch): type just your channel name and Connect. No login needed — it reads chat anonymously.");
            Step(3, "SSN (Social Stream Ninja): one session ID brings in every platform SSN supports (YouTube, TikTok, Kick and more). See the next section for the one-time setup.");
            Step(4, "RS (Restream): paste an access token from a Restream app if you use their service.");
            Step(5, "\"Auto-connect when a game session starts\" (Settings → Chat) reconnects your saved sources every time you press Start — set it and forget it.");

            Section("Chat — Social Stream Ninja setup (one-time)");
            Body("Social Stream Ninja (SSN) merges chat from every platform — YouTube, TikTok, Kick and more — into one feed. It runs as a browser extension OR the SSN desktop app; either one works, you just need to open its settings screen once.");
            Step(1, "In SSN, open \"Global settings and tools\" (the 🛠 options screen).");
            Img("ssn-settings.png");
            Step(2, "Open \"Experimental Features\" near the top. The two switches you need live HERE — not under Mechanics.");
            Step(3, "Turn ON \"Enable remote API control of extension\" — this lets Game Tracker send commands, including your send-to-all chat messages.");
            Step(4, "Turn ON \"Send chat messages to API server (for external listeners)\" — the key switch that pipes chat into the app; without it, no chat arrives.");
            Img("ssn-api.png");
            Step(5, "Copy the session ID from your SSN dock URL (the part after session=) into the SSN box in Game Tracker and Connect.");
            Body("The other SSN screens (Mechanics, etc.) don't affect the connection — you can leave them at their defaults.");
            Img("ssn-mechanics.png");
            Step(6, "Stuck? The \"SSN not showing chat? Setup guide\" link under the SSN box walks through this with more detail.");

            Section("Chat — sending & auto-replies");
            Step(1, "Type in the bar at the bottom of the chat window and press Enter — your message goes out through SSN to your chats (needs SSN connected).");
            Step(2, "The dropdown next to Send picks the destination: All chats, or one platform. It always resets to All chats on open.");
            Step(3, "The app auto-replies with @mentions for !request confirmations, balance checks, and redeems — only in the chat the viewer used, so other platforms aren't spammed. Toggle in Settings → Chat → Features.");
            Step(4, "Viewers can type !ghhelp any time for a self-serve menu: !ghhelp commands, !ghhelp redeems, !ghhelp points.");

            Section("Chatters, points & perks");
            Img("chatters.png");
            Step(1, "The 👥 Chatters button shows who's in the room: green dot = chatted recently, yellow = lurking. Idle timers are set in Features.");
            Step(2, "Points are ON by default: everyone on the chatters list earns 25 points every 5 minutes (rename them, change amounts, or turn off in Features → Points).");
            Step(3, "The first person to chat each stream gets a bonus with a confetti toast, and chatting on back-to-back stream days builds a growing streak bonus.");
            Step(4, "Viewers check their balance with your balance command (default !points — change it in Features if another bot like StreamElements also answers that, e.g. to !gh).");

            Section("Features — counts, chat style & redeems");
            Img("features.png");
            Step(1, "Settings → Chat → ⚙ Features is the control room for chat: the chatter-count header (total or per platform, on the overlay or not), points, perks, replies, and redeems.");
            Step(2, "Chat style: classic log, or colored boxes — pick up to 10 rotating colors (click a swatch for the color picker).");
            Step(3, "Point redeems: map a !command to Confetti, Fireworks, Screen shake, a Custom image, a Video, or one of your saved voice morphs. Set the point cost (0 = free), and use ▶ to test any of them instantly.");

            Section("Sound alerts");
            Img("soundalerts.png");
            Step(1, "Settings → Chat → 🔊 Sound Alerts maps chat commands to your own sound files (mp3, wav, ogg, flac and more).");
            Step(2, "Every alert has its own volume slider and ▶ Test button.");
            Step(3, "If chat spams sounds, hit the 🔇 button in the chat window's title bar — it kills the current sound and mutes everything (including TTS) until you click it again.");

            Section("Text to speech (TTS)");
            Img("settings-chat.png");
            Step(1, "Settings → Chat → Text to Speech. Turn on \"Read incoming chat messages aloud\".");
            Step(2, "\"Give each chatter their own random voice\" assigns every viewer a voice from the palette — saved per person, so they sound the same every stream. Or untick it and pick one voice for everybody.");
            Step(3, "Set speed and volume, whether to say names, and whether to skip ! commands.");
            Step(4, "Ignore rules: users listed in \"Ignore users\" (StreamElements by default) are never read; \"Ignore keywords\" mutes any message containing them; and point-redeem announcements (\"user redeemed …\") are skipped by default.");
            Step(5, "Add more Windows voices any time (Windows Settings → Time & language → Speech) — they appear here automatically.");

            Section("Voice Lab — make chatter voices");
            Img("voicelab.png");
            Step(1, "Settings → Chat → 🎤 Voice Lab. Pick a base voice and a funny effect (chipmunk, robot, ghost, demon, whisper, gremlin, underwater and more).");
            Step(2, "Type any phrase and ▶ Test to hear it.");
            Step(3, "Name it and Save — it joins the voice list and the random per-chatter pool, marked with a ★.");

            Section("Voice Morph — morph YOUR voice");
            Img("voicemorph.png");
            Step(1, "Settings → Chat → 🎙 Voice Morph. Pick your mic as Input and choose an Output (headphones to preview; 🔇 None to not hear yourself).");
            Step(2, "Build a morph: pitch slider (±12 semitones) + an effect, then \"Try it live\" and talk. Name it, set how long it lasts, and Save.");
            Step(3, "To get the morphed voice on stream: in OBS add an Application Audio Capture source pointed at Game Tracker and mute your raw mic — or set Output to a virtual cable (VB-CABLE) and use the cable as your OBS mic.");
            Step(4, "Attach saved morphs to point redeems in Features — viewers spend points to change YOUR voice. The overlay shows the morph name with a countdown, and your voice reverts automatically at zero.");

            Section("Appearance — themes");
            Img("settings-appearance.png");
            Step(1, "Settings → Appearance. Click a preset (Reptile, Amber, Ocean, Royal, Crimson, Mono) — the entire app re-colors instantly.");
            Step(2, "Or build your own: click any custom color swatch to open the picker (accent, deep accent, second accent, background, background lines).");
            Step(3, "\"Reset to default\" brings back the classic Reptile look.");

            Section("OBS overlay — setup");
            Img("settings-overlay.png");
            Step(1, "Settings → Overlay → Copy URL. In OBS add a Browser source, paste the URL, and set its size to 1280×720.");
            Step(2, "Click \"Edit overlay layout\" to open the editor in your browser: drag blocks, resize with the corner handle, set per-block fonts (every font on your PC, plus MR. Saturn), text color, and background opacity.");
            Step(3, "Use the Elements checkboxes to choose which blocks exist: Now Playing, Challenges, Live Chat, Chatters, Goals, Poll.");
            Step(4, "Save up to 10 layout presets, or Reset to default. Everything saves automatically and updates OBS live.");

            Section("Effects overlay — where redeems play");
            Step(1, "Settings → Overlay → \"Copy effects overlay URL\" (the /effects page). Add it to OBS as its own Browser source.");
            Step(2, "It's fully transparent — size and place it wherever you want redeem videos and images to appear (e.g. the top half of the screen).");
            Step(3, "While it's connected, video/image redeems play there instead of on the main overlay, so they never cover your chat or timer. Without it, they fall back to the main overlay.");

            Section("Text overlays — custom panels");
            Img("textpanels.png");
            Step(1, "Settings → Overlay → 📝 Text overlays. Add up to 5 panels — each gets its own URL (Copy URL) to add as an OBS Browser source.");
            Step(2, "Each panel has a styled header, a divider, and up to 10 lines — every line with its own font, size, color, optional image, and optional marquee scrolling with a speed slider.");
            Step(3, "Add bordered Left/Right side blocks with text and images — vertical (credits-style scroll) or horizontal (ticker scroll).");
            Step(4, "Set each panel's background opacity from solid to glass. Everything updates in OBS live as you type.");

            Section("Polls (!vote)");
            Img("poll.png");
            Step(1, "Chat window → 🗳 Poll. Type a question and 2–6 options, then ▶ Start poll.");
            Step(2, "Viewers vote with !vote 1, !vote 2 … one vote each; revoting switches their vote.");
            Step(3, "Results show as live bars in the overlay's Poll block (enable it in the layout editor).");
            Step(4, "■ End voting closes the poll, highlights the winner in gold, and announces it with a toast. ✕ Clear removes it from the overlay.");

            Section("Goals (progress bars)");
            Img("goals.png");
            Step(1, "Settings → Overlay → 🎯 Goals. Add up to 8 goals: name, current, target, and a bar color.");
            Step(2, "Enable the Goals block in the layout editor to show them on stream.");
            Step(3, "Keep the window open during the stream and tap +1 (or type a number) as things happen — OBS updates instantly.");

            Section("Stream stats & recap");
            Img("stats.png");
            Step(1, "Chat window → 📊 Stats: uptime, messages, unique chatters, top 5 chatters, games played, redeems, requests, and points handed out — live.");
            Step(2, "\"Copy recap\" puts a ready-to-paste summary on your clipboard for Discord or socials at the end of stream.");

            Section("Hotkeys");
            Step(1, "Settings → Hotkeys: click any box, press a key combo, done. All hotkeys work globally, even mid-game.");
            Step(2, "Give every redeem its own hotkey — pressing it fires the redeem instantly and free (sound, effect, video, and voice morph included). Or click \"Apply defaults\" to map Ctrl+Shift+1–9 to your first nine.");
            Step(3, "The panic key (default Ctrl+Shift+0) stops effects and ends the active voice morph.");
            Step(4, "Session hotkeys live here too: start/stop the session, clip a moment, and quick note.");

            Section("Backup & restore");
            Img("settings-backup.png");
            Step(1, "Settings → Backup → \"Export backup…\" writes one zip containing everything: games, settings, theme, overlays, points, custom voices, morphs, and streaks.");
            Step(2, "\"Import backup…\" restores a zip — your current data is saved to a safety backup first, then the app restarts with the imported data.");
            Step(3, "Great for reinstalls or moving to a new PC.");

            Section("Gift alerts & the activity feed");
            Body("Gifts from your streams (TikTok Roses, etc.) can trigger tiered sound alerts, and a live Activity Feed can show gifts, follows, subs and redeems right on your overlay.");
            Step(1, "Settings → Chat → 🎁 Gift Alerts: add tiers by coin value. When a gift arrives, the highest tier its value reaches plays its sound (and optional confetti/fireworks/shake). Example: a small gift plays a chime, a 1000-coin gift sets off fireworks.");
            Step(2, "Two ways to show it. (a) Activity Feed — a live scrolling list: open the overlay with ?edit, tick \"Activity Feed\", drag it where you like; new gifts/follows/subs/raids/redeems slide in. (b) Activity Ticker — a separate banner (Settings → Overlay → \"Copy activity ticker URL\", /ticker) that stays static and shows the latest of each (Newest Sub, Latest Gift, Newest Follower), swapping the name in place when a new one happens. Add the ticker as its own thin OBS Browser source; pick slots with ?kinds=sub,gift,follow.");
            Step(3, "Gifts, follows and subs are also tallied in Stream Stats and included in the recap.");
            Step(4, "Share the recap to Discord: open Stream Stats → \"📤 Share to Discord\". It posts through the companion bot (into your recap/tavern/clips channel) or a webhook if that's all you've set. Admins can pick the recap channel in Discord with !gh setup recapchannel.");
            Step(5, "Gift/follow/sub detection reads Social Stream Ninja's event data; if a gift ever doesn't trigger, a diagnostic log at %AppData%\\LazerGuanas Game Hunter\\events-debug.log captures the raw event so mappings can be tuned.");

            Section("Bot update announcements (Discord)");
            Body("The companion bot can announce new Game Hunter releases in your Discord.");
            Step(1, "In the Discord channel you want announcements in, an admin types: !gh setup updatechannel. The bot posts there whenever a new Game Hunter version is released. Turn it off with !gh setup updateoff.");

            Section("Tavern Tales — the chat RPG (needs the companion bot)");
            Body("A text RPG your community plays by typing \"tt <command>\" — in Discord and in your stream chat, sharing one character. It runs on the LazerGuanas Discord bot; Game Hunter forwards chat commands to it.");
            Step(1, "Set up the bot (see its README) and, in Settings → Chat → ⚙ Features → Discord, paste the Bot ingest URL + token, then tick \"Let chatters play Tavern Tales from chat\".");
            Step(2, "Every command starts with tt. Discord players just start: tt create <class> <race> [name], then tt adventure. Everyone in the channel sees the fights — great for drawing a crowd. Want a fresh start? tt new <class> <race> [name].");
            Step(3, "Stream-chat viewers link first: tt play <their Discord @username> → the bot DMs them a code → they type tt confirm <code> in chat. Now their chat and Discord share the same hero.");
            Step(4, "Play commands: tt help (menu) · tt char · tt skills · tt zones · tt adventure · in combat tt attack / tt skill <#> / tt use / tt flee · tt inv · tt equip <#> · tt shop / tt buy / tt sell · tt rest · tt leaderboard.");
            Step(5, "Gather & craft: tt chop / mine / fish / forage / dig / scavenge for materials, then tt recipes · tt craft <#> · tt brew <#> · tt enchant <#> to make and upgrade gear. Sell surplus with tt sell junk.");
            Step(6, "Progression & raids: beat a zone's boss with tt boss to unlock the next zone. Raids (a shared boss) are announced every 6–12 hours — viewers tt raid join, then it auto-battles and everyone who joins shares the loot (fall in a raid and you revive after, with reduced rewards).");
            Step(7, "Tavern Tales chat is auto-ignored by TTS (both commands and the bot's replies), so it won't spam your text-to-speech.");

            Section("Updates");
            Step(1, "The app checks for updates at launch and installs them silently — windows you had open reopen afterward, and your data is untouched.");
            Step(2, "The ↻ button in the top bar checks manually any time.");
        }

        // ---- theme ----

        private static ThemeSettings Clone(ThemeSettings t) => new()
        {
            PresetName = t.PresetName, Accent = t.Accent, AccentDeep = t.AccentDeep,
            Accent2 = t.Accent2, BgBase = t.BgBase, BgTile = t.BgTile,
        };

        private static List<PresetVm> BuildPresetVms()
        {
            var list = new List<PresetVm>();
            foreach (var p in ThemeSettings.Presets) list.Add(new PresetVm(p));
            return list;
        }

        private void BuildCustomRows()
        {
            CustomColors.Children.Clear();
            foreach (var slot in _slots)
                CustomColors.Children.Add(BuildColorRow(slot.label, slot.get(_theme), hex =>
                {
                    slot.set(_theme, hex);
                    _theme.PresetName = "Custom";
                    ApplyLive();
                    BuildCustomRows();
                }));
        }

        private FrameworkElement BuildColorRow(string label, string hex, Action<string> onPick)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };
            row.Children.Add(new TextBlock
            {
                Text = label, Foreground = Brush("#a8c488"), FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center, Width = 128,
            });
            var swatch = new Border
            {
                Width = 26, Height = 22, CornerRadius = new CornerRadius(3),
                BorderBrush = Brush("#4a7c3a"), BorderThickness = new Thickness(1),
                Background = Brush(hex), Margin = new Thickness(0, 0, 8, 0),
                Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "Pick a color",
            };
            swatch.MouseLeftButtonUp += (_, _) =>
            {
                var picked = ColorPickerWindow.Pick(this, hex);
                if (picked != null) onPick(picked);
            };
            row.Children.Add(swatch);
            row.Children.Add(new TextBlock
            {
                Text = hex, Foreground = Brush("#e8e0c4"), FontFamily = new FontFamily("Consolas"),
                FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
            });
            return row;
        }

        private void Preset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is PresetVm vm)
            {
                _theme = Clone(vm.Source);
                ApplyLive();
                BuildCustomRows();
            }
        }

        private void ResetTheme_Click(object sender, RoutedEventArgs e)
        {
            _theme = Clone(ThemeSettings.Presets[0]);
            ApplyLive();
            BuildCustomRows();
        }

        private void ApplyLive() => ThemeService.Apply(_theme);

        private static SolidColorBrush Brush(string hex)
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { return new SolidColorBrush(Colors.Gray); }
        }

        // ---- chat ----

        private void AutoConnect_Changed(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            var s = SettingsService.LoadChat();
            s.AutoConnect = AutoConnectCheck.IsChecked == true;
            SettingsService.SaveChat(s);
        }

        private void Opacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_ready) return;
            var s = SettingsService.LoadChat();
            s.Opacity = OpacitySlider.Value;
            SettingsService.SaveChat(s);
            ChatWindow.Current?.ApplyChatOpacity(OpacitySlider.Value);
        }

        // ---- text to speech ----

        private ChatTtsSettings ReadTtsUi()
        {
            // Mutate the stored settings so fields this page doesn't own (e.g. the Voice
            // Lab's custom voices) survive every save.
            var t = SettingsService.LoadTts();
            var profile = TtsVoice.SelectedItem as VoiceProfile;
            t.Enabled = TtsEnabled.IsChecked == true;
            t.OutputDevice = SelectedTtsOutput();
            t.PerChatterVoices = TtsPerChatter.IsChecked == true;
            t.Voice = profile?.Voice ?? string.Empty;
            t.Effect = profile?.Effect ?? "normal";
            t.Rate = (int)TtsRate.Value;
            t.Volume = (int)TtsVolume.Value;
            t.ReadName = TtsReadName.IsChecked == true;
            t.SkipCommands = TtsSkipCommands.IsChecked == true;
            t.SkipRedeemMessages = TtsSkipRedeems.IsChecked == true;
            t.SkipLinks = TtsSkipLinks.IsChecked == true;
            t.BleepBadWords = TtsBleepBadWords.IsChecked == true;
            t.IgnoreUsers = SplitList(TtsIgnoreUsers.Text);
            t.IgnoreKeywords = SplitList(TtsIgnoreKeywords.Text);
            t.BadWords = SplitList(TtsBadWords.Text);
            return t;
        }

        private static List<string> SplitList(string text) =>
            (text ?? string.Empty)
                .Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        private void ResetBadWords_Click(object sender, RoutedEventArgs e)
        {
            TtsBadWords.Text = string.Join(", ", GameTracker.Models.BadWordDefaults.Words);
            SaveTts();   // persist immediately so a reset sticks even without leaving the field
        }

        private void BadWordTest_Click(object sender, RoutedEventArgs e)
        {
            // Play a sample containing a censored word through the current voice so the
            // streamer hears the chicken bawk. Uses the words currently in the box.
            var words = SplitList(TtsBadWords.Text);
            _ttsTest.BleepBadWords = TtsBleepBadWords.IsChecked == true;
            _ttsTest.SetBadWords(words);
            _ttsTest.OutputDevice = SelectedTtsOutput();

            var sample = words.FirstOrDefault(w => w.Length >= 3) ?? "shit";
            var profile = TtsVoice.SelectedItem as VoiceProfile;
            _ttsTest.StopAll();
            _ttsTest.Speak($"Bad word filter test. That was some real {sample}, right there.",
                profile?.Voice, profile?.Effect ?? "normal", (int)TtsRate.Value, (int)TtsVolume.Value);
        }

        private void UpdateVoicePickerState()
        {
            bool perChatter = TtsPerChatter.IsChecked == true;
            TtsVoice.IsEnabled = !perChatter;
            TtsVoiceLbl.Foreground = new SolidColorBrush(
                perChatter ? Color.FromRgb(0x50, 0x60, 0x50) : Color.FromRgb(0xa8, 0xc4, 0x88));
        }

        private void SaveTts()
        {
            if (!_ready) return;
            SettingsService.SaveTts(ReadTtsUi());
            ChatWindow.Current?.ReloadTts();
        }

        private void Tts_Changed(object sender, RoutedEventArgs e) => SaveTts();
        private void Tts_Slider(object sender, RoutedPropertyChangedEventArgs<double> e) => SaveTts();

        private void TtsPerChatter_Changed(object sender, RoutedEventArgs e)
        {
            UpdateVoicePickerState();
            SaveTts();
        }

        private void VoiceLab_Click(object sender, RoutedEventArgs e)
        {
            new VoiceLabWindow { Owner = this }.ShowDialog();
            // Refresh the voice list — custom voices may have been added/removed.
            var tts = SettingsService.LoadTts();
            var selected = TtsVoice.SelectedItem as VoiceProfile;
            _profiles = TtsService.AllProfiles(tts.Custom);
            TtsVoice.ItemsSource = _profiles;
            TtsVoice.SelectedItem = _profiles.FirstOrDefault(p =>
                selected != null && p.Voice == selected.Voice && p.Effect == selected.Effect)
                ?? _profiles.FirstOrDefault();
        }

        private void VoiceMorph_Click(object sender, RoutedEventArgs e) =>
            new VoiceMorphWindow { Owner = this }.ShowDialog();

        private void TtsTest_Click(object sender, RoutedEventArgs e)
        {
            // Test the currently-selected single voice (random-per-chatter picks live in chat).
            var profile = TtsVoice.SelectedItem as VoiceProfile;
            _ttsTest.OutputDevice = SelectedTtsOutput();
            _ttsTest.StopAll();
            _ttsTest.Speak("This is a text to speech test. Your chat will sound like this.",
                profile?.Voice, profile?.Effect ?? "normal", (int)TtsRate.Value, (int)TtsVolume.Value);
        }

        private void Features_Click(object sender, RoutedEventArgs e)
        {
            var win = new ChatFeaturesWindow { Owner = this };
            if (win.ShowDialog() == true)
            {
                ChatWindow.Current?.ReloadFeatures();
                Main?.RefreshHotkeys();   // redeem list (and its hotkeys) may have changed
            }
        }

        private void SoundAlerts_Click(object sender, RoutedEventArgs e)
        {
            var win = new SoundAlertsWindow { Owner = this };
            if (win.ShowDialog() == true) ChatWindow.Current?.ReloadSoundAlerts();
        }

        private void GiftAlerts_Click(object sender, RoutedEventArgs e)
        {
            // The window persists and calls ChatWindow.ReloadFeatures() itself on Save.
            new GiftAlertsWindow { Owner = this }.ShowDialog();
        }

        // ---- overlay ----

        private string OverlayUrl => $"http://localhost:{OverlayServer.Port}/";

        private void ApplyPort_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(PortBox.Text.Trim(), out int port) || port is < 1 or > 65535)
            {
                OverlayStatus.Text = "Enter a port between 1 and 65535 (default 3620).";
                return;
            }
            SettingsService.SaveOverlayPort(port);
            OverlayServer.Restart();
            PortBox.Text = OverlayServer.Port.ToString();
            UpdateOverlayStatus();
        }

        private void CopyUrl_Click(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(OverlayUrl); OverlayStatus.Text = "Copied: " + OverlayUrl; }
            catch { /* clipboard can be momentarily locked */ }
        }

        private void CopyEffectsUrl_Click(object sender, RoutedEventArgs e)
        {
            var url = OverlayUrl + "effects";
            try { Clipboard.SetText(url); OverlayStatus.Text = "Copied: " + url + " — add as a transparent OBS Browser source."; }
            catch { /* clipboard can be momentarily locked */ }
        }

        // Ticker "kind" checkboxes in display order.
        private (System.Windows.Controls.CheckBox box, string kind)[] TickerKindBoxes() => new[]
        {
            (TkSub, "sub"), (TkFollow, "follow"), (TkGift, "gift"),
            (TkRaid, "raid"), (TkRedeem, "redeem"), (TkClip, "clip"),
        };

        private void LoadTickerControls()
        {
            var t = SettingsService.LoadTicker();
            foreach (var (box, kind) in TickerKindBoxes())
                box.IsChecked = t.Kinds != null && t.Kinds.Contains(kind);
            TkSize.Value = System.Math.Clamp(t.Size, (int)TkSize.Minimum, (int)TkSize.Maximum);
            TkSpeed.Value = System.Math.Clamp(t.Speed, (int)TkSpeed.Minimum, (int)TkSpeed.Maximum);
            TkBg.Value = System.Math.Clamp(t.BgOpacity, 0, 100);
            TkScroll.SelectedValue = string.IsNullOrEmpty(t.Scroll) ? "off" : t.Scroll;
            if (TkScroll.SelectedValue == null) TkScroll.SelectedIndex = 0;
            TkPos.SelectedValue = t.Position == "top" ? "top" : "bottom";
            if (TkPos.SelectedValue == null) TkPos.SelectedIndex = 0;
            TkAccent.SelectedValue = (t.Accent ?? "#7cc44a").TrimStart('#').ToLowerInvariant();
            if (TkAccent.SelectedValue == null) TkAccent.SelectedIndex = 0;
            TkLabels.IsChecked = t.ShowLabels;
        }

        private TickerSettings BuildTickerSettings()
        {
            var kinds = new System.Collections.Generic.List<string>();
            foreach (var (box, kind) in TickerKindBoxes())
                if (box.IsChecked == true) kinds.Add(kind);
            if (kinds.Count == 0) kinds.Add("sub");   // never empty
            return new TickerSettings
            {
                Kinds = kinds,
                Size = (int)TkSize.Value,
                Speed = (int)TkSpeed.Value,
                BgOpacity = (int)TkBg.Value,
                Scroll = (TkScroll.SelectedValue as string) ?? "off",
                Position = (TkPos.SelectedValue as string) ?? "bottom",
                Accent = "#" + ((TkAccent.SelectedValue as string) ?? "7cc44a"),
                ShowLabels = TkLabels.IsChecked == true,
            };
        }

        private void CopyTickerUrl_Click(object sender, RoutedEventArgs e)
        {
            var t = BuildTickerSettings();
            SettingsService.SaveTicker(t);   // remember the choices

            var q = "kinds=" + string.Join(",", t.Kinds) +
                    "&size=" + t.Size +
                    "&pos=" + t.Position +
                    "&bg=" + t.BgOpacity +
                    "&accent=" + t.Accent.TrimStart('#') +
                    "&labels=" + (t.ShowLabels ? "1" : "0");
            if (t.Scroll == "rtl" || t.Scroll == "ltr")
                q += "&scroll=" + t.Scroll + "&speed=" + t.Speed;

            var url = OverlayUrl + "ticker?" + q;
            try { Clipboard.SetText(url); OverlayStatus.Text = "Copied ticker URL — paste it into an OBS Browser source. Re-copy after any change."; }
            catch { /* clipboard can be momentarily locked */ }
        }

        private void ChatLines_Changed(object sender, RoutedEventArgs e)
        {
            if (!_ready || !int.TryParse(ChatLinesBox.Text.Trim(), out int n)) return;
            n = Math.Clamp(n, 5, 100);
            ChatLinesBox.Text = n.ToString();
            SettingsService.SaveOverlayChatLines(n);
            ChatWindow.Current?.ReloadOverlayLines();
            OverlayStatus.Text = $"Chat set to {n} lines — refresh the OBS source to apply.";
        }

        private void EditLayout_Click(object sender, RoutedEventArgs e)
        {
            if (!OverlayServer.IsRunning)
            {
                OverlayStatus.Text = "Overlay server isn't running — can't open the editor.";
                return;
            }
            var url = OverlayUrl + "?edit=1";
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                OverlayStatus.Text = "Opened the layout editor in your browser — it saves automatically.";
            }
            catch { OverlayStatus.Text = "Couldn't open a browser. Go to " + url + " manually."; }
        }

        private void TextPanels_Click(object sender, RoutedEventArgs e)
        {
            if (_textPanelsWindow != null) { _textPanelsWindow.Activate(); return; }
            _textPanelsWindow = new TextPanelsWindow { Owner = this };
            _textPanelsWindow.Closed += (_, _) => _textPanelsWindow = null;
            _textPanelsWindow.Show();
        }

        private GoalsWindow? _goalsWindow;

        private void Goals_Click(object sender, RoutedEventArgs e)
        {
            if (_goalsWindow != null) { _goalsWindow.Activate(); return; }
            _goalsWindow = new GoalsWindow { Owner = this };
            _goalsWindow.Closed += (_, _) => _goalsWindow = null;
            _goalsWindow.Show();
        }

        private void UpdateOverlayStatus() =>
            OverlayStatus.Text = OverlayServer.IsRunning
                ? $"Serving at {OverlayUrl} — use this as the OBS Browser source URL."
                : "Overlay server is not running" +
                  (string.IsNullOrEmpty(OverlayServer.LastError) ? "." : $": {OverlayServer.LastError}. Try another port.");

        // ---- hotkeys ----

        private Action<HotkeyBinding>? _hotkeyCapture;
        private Button? _hotkeyCaptureBtn;
        private string _hotkeyCaptureOldLabel = string.Empty;

        private static MainWindow? Main => Application.Current.MainWindow as MainWindow;

        private void BuildHotkeys()
        {
            EndHotkeyCapture(cancel: true);
            HotkeysContent.Children.Clear();

            void Head(string t) => HotkeysContent.Children.Add(new TextBlock
            {
                Text = t, Foreground = (System.Windows.Media.Brush)FindResource("ThemeAccent"),
                FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 12, 0, 6),
            });
            void Note(string t) => HotkeysContent.Children.Add(new TextBlock
            {
                Text = t, Foreground = Brush("#7a9070"), FontSize = 11,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
            });

            Button KeyBtn(HotkeyBinding? current, Action<HotkeyBinding> apply)
            {
                var b = new Button
                {
                    Content = current?.Display ?? "click to set…",
                    Style = (Style)FindResource("Btn"),
                    MinWidth = 150,
                    Padding = new Thickness(9, 3, 9, 3),
                };
                b.Click += (_, _) => StartHotkeyCapture(b, apply);
                return b;
            }

            Grid Row(string label, HotkeyBinding? current, Action<HotkeyBinding> apply, Action? clear)
            {
                var g = new Grid { Margin = new Thickness(0, 0, 0, 5) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var lb = new TextBlock
                {
                    Text = label, Foreground = Brush("#e8e0c4"), FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                };
                g.Children.Add(lb);
                var kb = KeyBtn(current, apply);
                Grid.SetColumn(kb, 1);
                g.Children.Add(kb);
                if (clear != null)
                {
                    var cb = new Button
                    {
                        Content = "✕", Style = (Style)FindResource("Btn"),
                        Margin = new Thickness(5, 0, 0, 0), Padding = new Thickness(7, 3, 7, 3),
                        ToolTip = "Remove this hotkey",
                    };
                    cb.Click += (_, _) => clear();
                    Grid.SetColumn(cb, 2);
                    g.Children.Add(cb);
                }
                return g;
            }

            Note("Click a box, then press the key combo you want. Esc cancels. Hotkeys work globally, even while you're in-game.");

            Head("SESSION");
            var cfg = SettingsService.LoadHotkeys();
            HotkeysContent.Children.Add(Row("Start / stop session", cfg.Toggle,
                b => { var c = SettingsService.LoadHotkeys(); c.Toggle = b; SettingsService.SaveHotkeys(c); Main?.RefreshHotkeys(); BuildHotkeys(); }, null));
            HotkeysContent.Children.Add(Row("Clip a moment", cfg.Clip,
                b => { var c = SettingsService.LoadHotkeys(); c.Clip = b; SettingsService.SaveHotkeys(c); Main?.RefreshHotkeys(); BuildHotkeys(); }, null));
            HotkeysContent.Children.Add(Row("Quick note", cfg.Note,
                b => { var c = SettingsService.LoadHotkeys(); c.Note = b; SettingsService.SaveHotkeys(c); Main?.RefreshHotkeys(); BuildHotkeys(); }, null));

            Head("EFFECTS");
            HotkeysContent.Children.Add(Row("Panic: stop effects + end morph", cfg.FxStop,
                b => { var c = SettingsService.LoadHotkeys(); c.FxStop = b; SettingsService.SaveHotkeys(c); Main?.RefreshHotkeys(); BuildHotkeys(); }, null));

            Head("REDEEMS (fire free, no point cost)");
            var features = SettingsService.LoadChatFeatures();
            if (features.Redeems.Count == 0)
                Note("No redeems yet — create some in Settings → Chat → Features first.");
            for (int i = 0; i < features.Redeems.Count && i < 24; i++)
            {
                int idx = i;
                var r = features.Redeems[i];
                var label = string.IsNullOrWhiteSpace(r.Command) ? $"(redeem {i + 1})" : r.Command;
                HotkeysContent.Children.Add(Row(label, r.Hotkey,
                    b =>
                    {
                        var f = SettingsService.LoadChatFeatures();
                        if (idx < f.Redeems.Count) f.Redeems[idx].Hotkey = b;
                        SettingsService.SaveChatFeatures(f);
                        Main?.RefreshHotkeys();
                        BuildHotkeys();
                    },
                    () =>
                    {
                        var f = SettingsService.LoadChatFeatures();
                        if (idx < f.Redeems.Count) f.Redeems[idx].Hotkey = null;
                        SettingsService.SaveChatFeatures(f);
                        Main?.RefreshHotkeys();
                        BuildHotkeys();
                    }));
            }

            if (features.Redeems.Count > 0)
            {
                var defBtn = new Button
                {
                    Content = "Apply defaults: Ctrl+Shift+1…9 to the first nine",
                    Style = (Style)FindResource("Btn"),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 6, 0, 0),
                };
                defBtn.Click += (_, _) =>
                {
                    var f = SettingsService.LoadChatFeatures();
                    for (int i = 0; i < f.Redeems.Count && i < 9; i++)
                        f.Redeems[i].Hotkey = new HotkeyBinding(
                            System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift,
                            System.Windows.Input.Key.D1 + i);
                    SettingsService.SaveChatFeatures(f);
                    Main?.RefreshHotkeys();
                    BuildHotkeys();
                };
                HotkeysContent.Children.Add(defBtn);
            }
        }

        private void StartHotkeyCapture(Button b, Action<HotkeyBinding> apply)
        {
            EndHotkeyCapture(cancel: true);
            Main?.PauseHotkeys();          // release globals so the combo reaches us
            _hotkeyCapture = apply;
            _hotkeyCaptureBtn = b;
            _hotkeyCaptureOldLabel = b.Content as string ?? "";
            b.Content = "press keys… (Esc cancels)";
            PreviewKeyDown += HotkeyCapture_KeyDown;
        }

        private void EndHotkeyCapture(bool cancel)
        {
            PreviewKeyDown -= HotkeyCapture_KeyDown;
            if (cancel && _hotkeyCaptureBtn != null)
                _hotkeyCaptureBtn.Content = _hotkeyCaptureOldLabel;
            _hotkeyCapture = null;
            _hotkeyCaptureBtn = null;
            Main?.RefreshHotkeys();        // re-register whatever is saved
        }

        private void HotkeyCapture_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (_hotkeyCapture == null) return;
            var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
            if (key is System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl
                    or System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift
                    or System.Windows.Input.Key.LeftAlt or System.Windows.Input.Key.RightAlt
                    or System.Windows.Input.Key.LWin or System.Windows.Input.Key.RWin) return;
            e.Handled = true;

            if (key == System.Windows.Input.Key.Escape) { EndHotkeyCapture(cancel: true); return; }

            var apply = _hotkeyCapture;
            _hotkeyCapture = null;
            PreviewKeyDown -= HotkeyCapture_KeyDown;
            apply(new HotkeyBinding(System.Windows.Input.Keyboard.Modifiers, key));
        }

        // ---- backup / restore ----

        private void ExportBackup_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export backup",
                Filter = "Game Hunter backup (*.zip)|*.zip",
                FileName = $"GameHunter-backup-{DateTime.Now:yyyy-MM-dd}.zip",
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                BackupService.Export(dlg.FileName);
                BackupStatus.Text = "Backup saved: " + dlg.FileName;
            }
            catch (Exception ex) { BackupStatus.Text = "Export failed: " + ex.Message; }
        }

        private void ImportBackup_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import backup",
                Filter = "Game Hunter backup (*.zip)|*.zip",
            };
            if (dlg.ShowDialog(this) != true) return;
            if (!BackupService.LooksValid(dlg.FileName))
            {
                BackupStatus.Text = "That file doesn't look like a Game Hunter backup.";
                return;
            }
            if (MessageBox.Show(this,
                "Restore this backup? Your current data is saved to a safety backup first, then the app restarts.",
                "Import backup", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            try
            {
                var safety = BackupService.Import(dlg.FileName);
                BackupStatus.Text = "Restored. Safety backup: " + safety + " — restarting…";
                // Relaunch after a short delay so the single-instance mutex is free by the
                // time the new process starts.
                var exe = Environment.ProcessPath;
                if (exe != null)
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                        "cmd.exe", $"/c timeout /t 2 /nobreak >nul & start \"\" \"{exe}\"")
                    { CreateNoWindow = true, UseShellExecute = false });
                Application.Current.Shutdown();
            }
            catch (Exception ex) { BackupStatus.Text = "Import failed: " + ex.Message; }
        }

        /// <summary>Docs capture: switch to a nav panel without a click event.</summary>
        public void ShowPanelForDocs(string panel) =>
            Nav_Click(panel switch
            {
                "chat" => NavChat, "overlay" => NavOverlay,
                "backup" => NavBackup, "help" => NavHelp, "hotkeys" => NavHotkeys,
                _ => NavAppearance,
            }, new RoutedEventArgs());

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosed(EventArgs e)
        {
            _ttsTest.Dispose();
            base.OnClosed(e);
        }

        public class PresetVm
        {
            public ThemeSettings Source { get; }
            public string PresetName => Source.PresetName;
            public Color AccentColor => Parse(Source.Accent);
            public Color Accent2Color => Parse(Source.Accent2);
            public Color BgColor => Parse(Source.BgBase);
            public PresetVm(ThemeSettings s) => Source = s;
            private static Color Parse(string hex)
            {
                try { return (Color)ColorConverter.ConvertFromString(hex); }
                catch { return Colors.Gray; }
            }
        }
    }
}
