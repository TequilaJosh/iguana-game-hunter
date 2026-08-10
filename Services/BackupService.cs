using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace GameTracker.Services
{
    /// <summary>
    /// One-file backup/restore of everything in the app's data folder (games, settings,
    /// points, custom voices, chatter voices, streaks).
    /// </summary>
    public static class BackupService
    {
        private static string DataDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LazerGuanas Game Hunter");

        /// <summary>Zip the whole data folder to the given path.</summary>
        public static void Export(string zipPath)
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            foreach (var file in Directory.GetFiles(DataDir, "*.json", SearchOption.TopDirectoryOnly))
                zip.CreateEntryFromFile(file, Path.GetFileName(file));
        }

        /// <summary>
        /// Restore a backup zip: first snapshot the current data to a safety zip inside the
        /// data folder, then extract the backup's json files over the data folder.
        /// Returns the safety-zip path.
        /// </summary>
        public static string Import(string zipPath)
        {
            Directory.CreateDirectory(DataDir);
            var safetyDir = Path.Combine(DataDir, "backups");
            Directory.CreateDirectory(safetyDir);
            var safety = Path.Combine(safetyDir,
                $"pre-import-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            using (var zip = ZipFile.Open(safety, ZipArchiveMode.Create))
                foreach (var file in Directory.GetFiles(DataDir, "*.json", SearchOption.TopDirectoryOnly))
                    zip.CreateEntryFromFile(file, Path.GetFileName(file));

            using var src = ZipFile.OpenRead(zipPath);
            foreach (var entry in src.Entries)
            {
                // Only accept flat .json entries — nothing outside the data folder.
                if (!entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                if (entry.Name != entry.FullName) continue;
                entry.ExtractToFile(Path.Combine(DataDir, entry.Name), overwrite: true);
            }
            return safety;
        }

        /// <summary>True if the zip looks like one of ours (contains games or settings json).</summary>
        public static bool LooksValid(string zipPath)
        {
            try
            {
                using var zip = ZipFile.OpenRead(zipPath);
                return zip.Entries.Any(e =>
                    e.Name.Equals("games.json", StringComparison.OrdinalIgnoreCase) ||
                    e.Name.Equals("settings.json", StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }
    }
}
