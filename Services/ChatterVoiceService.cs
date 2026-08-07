using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace GameTracker.Services
{
    /// <summary>
    /// Assigns each chatter a random voice profile and remembers it (voices.json), so the
    /// same person keeps the same voice across sessions and streams.
    /// </summary>
    public sealed class ChatterVoiceService
    {
        public sealed class Stored
        {
            public string Voice { get; set; } = string.Empty;
            public string Effect { get; set; } = "normal";
        }

        private static readonly string File_ = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LazerGuanas Game Hunter", "voices.json");

        private Dictionary<string, Stored>? _map;
        private List<VoiceProfile> _profiles = new();
        private readonly Random _rng = new();
        private readonly object _gate = new();

        public void SetProfiles(List<VoiceProfile> profiles) => _profiles = profiles ?? new();

        private Dictionary<string, Stored> Load()
        {
            if (_map != null) return _map;
            try
            {
                _map = File.Exists(File_)
                    ? JsonConvert.DeserializeObject<Dictionary<string, Stored>>(File.ReadAllText(File_))
                      ?? new Dictionary<string, Stored>()
                    : new Dictionary<string, Stored>();
            }
            catch { _map = new Dictionary<string, Stored>(); }
            return _map;
        }

        /// <summary>The saved voice for this chatter, assigning a random one on first sight.</summary>
        public (string voice, string effect) For(string platform, string user)
        {
            if (_profiles.Count == 0) return (string.Empty, "normal");
            var key = PointsService.Key(platform, user);
            lock (_gate)
            {
                var map = Load();
                if (map.TryGetValue(key, out var s)) return (s.Voice, s.Effect ?? "normal");
                var pick = _profiles[_rng.Next(_profiles.Count)];
                map[key] = new Stored { Voice = pick.Voice, Effect = pick.Effect };
                Save();
                return (pick.Voice, pick.Effect);
            }
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(File_)!);
                File.WriteAllText(File_, JsonConvert.SerializeObject(_map, Formatting.Indented));
            }
            catch { /* best-effort */ }
        }
    }
}
