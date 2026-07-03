using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace GameTracker.Services
{
    /// <summary>
    /// Persistent per-viewer points (points.json). Keyed by "platform|username"
    /// so the same name on different platforms stays separate.
    /// </summary>
    public static class PointsService
    {
        private static readonly string File_ = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LazerGuanas Game Hunter", "points.json");

        private static Dictionary<string, long>? _points;
        private static readonly object Gate = new();

        public static string Key(string platform, string user) =>
            (platform ?? "").ToLowerInvariant() + "|" + (user ?? "").Trim().ToLowerInvariant();

        private static Dictionary<string, long> Load()
        {
            if (_points != null) return _points;
            try
            {
                _points = File.Exists(File_)
                    ? JsonConvert.DeserializeObject<Dictionary<string, long>>(File.ReadAllText(File_))
                      ?? new Dictionary<string, long>()
                    : new Dictionary<string, long>();
            }
            catch { _points = new Dictionary<string, long>(); }
            return _points;
        }

        public static long Get(string platform, string user)
        {
            lock (Gate) return Load().TryGetValue(Key(platform, user), out var v) ? v : 0;
        }

        public static void Add(string platform, string user, long amount)
        {
            lock (Gate)
            {
                var p = Load();
                var k = Key(platform, user);
                p[k] = Math.Max(0, (p.TryGetValue(k, out var v) ? v : 0) + amount);
            }
        }

        /// <summary>Deduct if the viewer can afford it. Returns false (no change) otherwise.</summary>
        public static bool TrySpend(string platform, string user, long cost)
        {
            lock (Gate)
            {
                var p = Load();
                var k = Key(platform, user);
                var bal = p.TryGetValue(k, out var v) ? v : 0;
                if (bal < cost) return false;
                p[k] = bal - cost;
                return true;
            }
        }

        public static void Save()
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(File_)!);
                    File.WriteAllText(File_, JsonConvert.SerializeObject(Load(), Formatting.Indented));
                }
            }
            catch { /* best-effort */ }
        }
    }
}
