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
        }

        private static AppSettings LoadAll()
        {
            try
            {
                if (File.Exists(SettingsFile))
                    return JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(SettingsFile))
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
    }
}
