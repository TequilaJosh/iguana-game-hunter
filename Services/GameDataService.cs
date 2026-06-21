using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using GameTracker.Models;

namespace GameTracker.Services
{
    public static class GameDataService
    {
        private static readonly string DataFolder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GameTracker");
        private static readonly string DataFile = Path.Combine(DataFolder, "games.json");

        public static List<Game> Load()
        {
            try
            {
                if (!Directory.Exists(DataFolder))
                    Directory.CreateDirectory(DataFolder);

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
