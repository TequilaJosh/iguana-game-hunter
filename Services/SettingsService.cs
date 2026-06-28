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
    }
}
