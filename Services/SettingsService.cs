using System;
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
    }
}
