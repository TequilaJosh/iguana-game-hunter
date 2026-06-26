using System;
using System.IO;
using Newtonsoft.Json;
using GameTracker.Models;

namespace GameTracker.Services
{
    /// <summary>Persists app settings (currently the configurable hotkeys) to settings.json.</summary>
    public static class SettingsService
    {
        private static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LazerGuanas Game Hunter");
        private static readonly string SettingsFile = Path.Combine(Folder, "settings.json");

        private class AppSettings
        {
            public HotkeyConfig Hotkeys { get; set; } = new();
        }

        public static HotkeyConfig LoadHotkeys()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var s = JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(SettingsFile));
                    if (s?.Hotkeys != null) return s.Hotkeys;
                }
            }
            catch { /* fall through to defaults */ }
            return new HotkeyConfig();
        }

        public static void SaveHotkeys(HotkeyConfig hotkeys)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(SettingsFile,
                    JsonConvert.SerializeObject(new AppSettings { Hotkeys = hotkeys }, Formatting.Indented));
            }
            catch { /* best-effort */ }
        }
    }
}
