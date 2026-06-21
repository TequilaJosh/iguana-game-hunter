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

                var json = JsonConvert.SerializeObject(games, Formatting.Indented);
                File.WriteAllText(DataFile, json);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error saving data: {ex.Message}", "Save Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }
    }
}
