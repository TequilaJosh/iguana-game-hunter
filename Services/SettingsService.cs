using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using GameTracker.Models;

namespace GameTracker.Services
{
    /// <summary>Per-source chat connection settings, remembered between runs.</summary>
    public class ChatSettings
    {
        public string TwitchChannel { get; set; } = string.Empty;
        public string SsnSession { get; set; } = string.Empty;
        public string RestreamToken { get; set; } = string.Empty;
        public bool AutoConnect { get; set; } = true; // connect saved sources when a session starts
        public double Opacity { get; set; } = 1.0;    // chat window transparency (0.25–1.0)
        public string SendTarget { get; set; } = "";  // outgoing SSN target platform ("" = all)
    }

    /// <summary>Persists app settings (hotkeys, chat connections) to settings.json.</summary>
    public static class SettingsService
    {
        private static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LazerGuanas Game Hunter");
        private static readonly string SettingsFile = Path.Combine(Folder, "settings.json");

        private class AppSettings
        {
            public HotkeyConfig Hotkeys { get; set; } = new();
            public ChatSettings Chat { get; set; } = new();
            public List<string>? SuggestionTypes { get; set; } // null = use built-in defaults
            public List<string> SeedsApplied { get; set; } = new(); // bundled games already offered
            public List<SoundAlert> SoundAlerts { get; set; } = new(); // chat command -> sound file
            public int OverlayPort { get; set; } = 3620; // port for the live OBS overlay server
            public int OverlayChatLines { get; set; } = 20; // chat lines shown on the overlay
            public string? OverlayLayout { get; set; } // JSON: per-element position/size/font (null = default)
            public string? OverlayPresets { get; set; } // JSON array of saved layout presets
            public ChatFeatureSettings Features { get; set; } = new(); // counts/chatters/points/style/redeems
            public List<TextPanel> TextPanels { get; set; } = new();   // custom OBS text overlays (max 5)
            public ThemeSettings Theme { get; set; } = new();          // app colour theme
            public ChatTtsSettings Tts { get; set; } = new();          // read chat aloud
            public List<StreamGoal> Goals { get; set; } = new();       // overlay goal bars
            public int MigrationRev { get; set; }                      // one-time defaults migrations applied
            public MorphSettings Morph { get; set; } = new();          // live mic voice morph
        }

        // Replace initialized collections instead of appending to them — otherwise lists
        // with non-empty defaults (e.g. Features.BoxColors) duplicate on every load/save.
        private static readonly JsonSerializerSettings LoadSettings = new()
        {
            ObjectCreationHandling = ObjectCreationHandling.Replace,
        };

        private static AppSettings LoadAll()
        {
            try
            {
                if (File.Exists(SettingsFile))
                    return JsonConvert.DeserializeObject<AppSettings>(
                               File.ReadAllText(SettingsFile), LoadSettings)
                           ?? new AppSettings();
            }
            catch { /* fall through to defaults */ }
            return new AppSettings();
        }

        private static void SaveAll(AppSettings s)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(SettingsFile, JsonConvert.SerializeObject(s, Formatting.Indented));
            }
            catch { /* best-effort */ }
        }

        public static HotkeyConfig LoadHotkeys() => LoadAll().Hotkeys ?? new HotkeyConfig();

        public static void SaveHotkeys(HotkeyConfig hotkeys)
        {
            var s = LoadAll();
            s.Hotkeys = hotkeys;
            SaveAll(s);
        }

        public static ChatSettings LoadChat() => LoadAll().Chat ?? new ChatSettings();

        public static void SaveChat(ChatSettings chat)
        {
            var s = LoadAll();
            s.Chat = chat;
            SaveAll(s);
        }

        /// <summary>Saved suggestion types, or null if the user has never customized them.</summary>
        public static List<string>? LoadSuggestionTypes() => LoadAll().SuggestionTypes;

        public static void SaveSuggestionTypes(List<string> types)
        {
            var s = LoadAll();
            s.SuggestionTypes = types;
            SaveAll(s);
        }

        public static List<string> LoadAppliedSeeds() => LoadAll().SeedsApplied ?? new List<string>();

        public static void SaveAppliedSeeds(List<string> keys)
        {
            var s = LoadAll();
            s.SeedsApplied = keys;
            SaveAll(s);
        }

        public static List<SoundAlert> LoadSoundAlerts() => LoadAll().SoundAlerts ?? new List<SoundAlert>();

        public static void SaveSoundAlerts(List<SoundAlert> alerts)
        {
            var s = LoadAll();
            s.SoundAlerts = alerts;
            SaveAll(s);
        }

        /// <summary>TCP port for the live OBS overlay server (defaults to 3620).</summary>
        public static int LoadOverlayPort()
        {
            int p = LoadAll().OverlayPort;
            return (p is >= 1 and <= 65535) ? p : 3620;
        }

        public static void SaveOverlayPort(int port)
        {
            var s = LoadAll();
            s.OverlayPort = (port is >= 1 and <= 65535) ? port : 3620;
            SaveAll(s);
        }

        /// <summary>How many chat lines the OBS overlay shows (clamped 5–100, default 20).</summary>
        public static int LoadOverlayChatLines()
        {
            int n = LoadAll().OverlayChatLines;
            return (n is >= 5 and <= 100) ? n : 20;
        }

        public static void SaveOverlayChatLines(int lines)
        {
            var s = LoadAll();
            s.OverlayChatLines = (lines is >= 5 and <= 100) ? lines : 20;
            SaveAll(s);
        }

        /// <summary>Saved overlay layout JSON (per-element position/size/font), or null for the default.</summary>
        public static string? LoadOverlayLayout() => LoadAll().OverlayLayout;

        public static void SaveOverlayLayout(string? layoutJson)
        {
            var s = LoadAll();
            s.OverlayLayout = layoutJson;
            SaveAll(s);
        }

        /// <summary>Saved overlay layout presets (JSON array), or null if none.</summary>
        public static string? LoadOverlayPresets() => LoadAll().OverlayPresets;

        public static void SaveOverlayPresets(string? presetsJson)
        {
            var s = LoadAll();
            s.OverlayPresets = presetsJson;
            SaveAll(s);
        }

        /// <summary>One-time default migrations for users updating from older versions.</summary>
        public static void RunMigrations()
        {
            var s = LoadAll();
            if (s.MigrationRev >= 1) return;

            // Rev 1: points became on-by-default at 25 per 5 min. Flip users still on the
            // old shipped default (off @ 10) — anyone who customized keeps their numbers.
            s.Features ??= new ChatFeatureSettings();
            if (!s.Features.PointsEnabled)
            {
                s.Features.PointsEnabled = true;
                if (s.Features.PointsPerInterval == 10) s.Features.PointsPerInterval = 25;
            }
            s.MigrationRev = 1;
            SaveAll(s);
        }

        public static ChatFeatureSettings LoadChatFeatures()
        {
            var f = LoadAll().Features ?? new ChatFeatureSettings();
            // Repair files written before the append-duplication fix (cap is 10 colors).
            if (f.BoxColors.Count > 10)
                f.BoxColors = f.BoxColors.GetRange(0, 10);
            return f;
        }

        public static void SaveChatFeatures(ChatFeatureSettings features)
        {
            var s = LoadAll();
            s.Features = features;
            SaveAll(s);
        }

        public static List<TextPanel> LoadTextPanels() => LoadAll().TextPanels ?? new List<TextPanel>();

        public static void SaveTextPanels(List<TextPanel> panels)
        {
            var s = LoadAll();
            s.TextPanels = panels;
            SaveAll(s);
        }

        public static ThemeSettings LoadTheme() => LoadAll().Theme ?? new ThemeSettings();

        public static void SaveTheme(ThemeSettings theme)
        {
            var s = LoadAll();
            s.Theme = theme;
            SaveAll(s);
        }

        public static MorphSettings LoadMorph() => LoadAll().Morph ?? new MorphSettings();

        public static void SaveMorph(MorphSettings morph)
        {
            var s = LoadAll();
            s.Morph = morph;
            SaveAll(s);
        }

        public static List<StreamGoal> LoadGoals() => LoadAll().Goals ?? new List<StreamGoal>();

        public static void SaveGoals(List<StreamGoal> goals)
        {
            var s = LoadAll();
            s.Goals = goals;
            SaveAll(s);
        }

        public static ChatTtsSettings LoadTts() => LoadAll().Tts ?? new ChatTtsSettings();

        public static void SaveTts(ChatTtsSettings tts)
        {
            var s = LoadAll();
            s.Tts = tts;
            SaveAll(s);
        }
    }
}
