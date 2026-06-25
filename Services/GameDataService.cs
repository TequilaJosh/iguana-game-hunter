using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using GameTracker.Models;

namespace GameTracker.Services
{
    public static class GameDataService
    {
        private static readonly string AppData =
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        private static readonly string DataFolder = Path.Combine(AppData, "LazerGuanas Game Hunter");
        private static readonly string DataFile = Path.Combine(DataFolder, "games.json");

        // Rolling backups so a bad write can't wipe the library.
        private static readonly string BackupFolder = Path.Combine(DataFolder, "backups");
        private const int MaxBackups = 10;

        // Previous save location (app was formerly named "Game Tracker").
        private static readonly string LegacyDataFile = Path.Combine(AppData, "GameTracker", "games.json");

        public static List<Game> Load()
        {
            try
            {
                if (!Directory.Exists(DataFolder))
                    Directory.CreateDirectory(DataFolder);

                // One-time migration: carry an existing library over from the old
                // location so players don't lose their games after the rename.
                if (!File.Exists(DataFile) && File.Exists(LegacyDataFile))
                {
                    try { File.Copy(LegacyDataFile, DataFile); } catch { /* fall through to empty */ }
                }

                if (!File.Exists(DataFile))
                    return new List<Game>();

                var json = File.ReadAllText(DataFile);
                return JsonConvert.DeserializeObject<List<Game>>(json) ?? new List<Game>();
            }
            catch
            {
                return new List<Game>();
            }
        }

        public static void Save(List<Game> games)
        {
            try
            {
                if (!Directory.Exists(DataFolder))
                    Directory.CreateDirectory(DataFolder);

                // Keep the previous good save as a rolling backup before overwriting.
                if (File.Exists(DataFile))
                    BackupCurrent();

                var json = JsonConvert.SerializeObject(games, Formatting.Indented);
                File.WriteAllText(DataFile, json);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error saving data: {ex.Message}", "Save Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        private static void BackupCurrent()
        {
            try
            {
                if (!Directory.Exists(BackupFolder))
                    Directory.CreateDirectory(BackupFolder);

                var stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                File.Copy(DataFile, Path.Combine(BackupFolder, $"games_{stamp}.json"), overwrite: true);

                // Prune to the most recent MaxBackups.
                var old = new DirectoryInfo(BackupFolder)
                    .GetFiles("games_*.json");
                if (old.Length > MaxBackups)
                {
                    Array.Sort(old, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                    for (int i = MaxBackups; i < old.Length; i++)
                    {
                        try { old[i].Delete(); } catch { /* ignore */ }
                    }
                }
            }
            catch { /* backups are best-effort; never block a save */ }
        }

        public static string BackupFolderPath => BackupFolder;
    }
}
