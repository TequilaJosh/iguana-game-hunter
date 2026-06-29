using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace GameTracker.Services
{
    /// <summary>A window that was open, so it can be reopened after an update restart.</summary>
    public class OpenWindow
    {
        public string Type { get; set; } = string.Empty;   // chat / wheel / guide / hltb / session
        public string? GameId { get; set; }                 // for per-game windows
    }

    /// <summary>
    /// Remembers which windows were open when the app restarts to apply an update,
    /// so they can be reopened automatically afterward. The file is one-shot: it's
    /// written just before the update restart and cleared once consumed on startup.
    /// </summary>
    public static class WindowStateService
    {
        private static readonly string ReopenFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LazerGuanas Game Hunter", "reopen.json");

        public static void Save(List<OpenWindow> windows)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ReopenFile)!);
                File.WriteAllText(ReopenFile, JsonConvert.SerializeObject(windows));
            }
            catch { /* best-effort */ }
        }

        public static List<OpenWindow> LoadAndClear()
        {
            try
            {
                if (!File.Exists(ReopenFile)) return new List<OpenWindow>();
                var json = File.ReadAllText(ReopenFile);
                File.Delete(ReopenFile);
                return JsonConvert.DeserializeObject<List<OpenWindow>>(json) ?? new List<OpenWindow>();
            }
            catch { return new List<OpenWindow>(); }
        }
    }
}
