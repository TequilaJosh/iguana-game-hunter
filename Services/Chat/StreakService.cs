using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace GameTracker.Services.Chat
{
    /// <summary>
    /// First-chatter and daily-streak perks. Streaks persist (streaks.json): chatting on
    /// consecutive stream days grows the streak; missing more than a day resets it.
    /// </summary>
    public sealed class StreakService
    {
        public sealed class Entry
        {
            public string LastDay { get; set; } = "";   // yyyy-MM-dd of last counted visit
            public int Streak { get; set; }
        }

        private static readonly string File_ = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LazerGuanas Game Hunter", "streaks.json");

        private Dictionary<string, Entry>? _map;
        private bool _firstAwarded;                   // first chatter of this app run
        private readonly object _gate = new();

        private Dictionary<string, Entry> Load()
        {
            if (_map != null) return _map;
            try
            {
                _map = File.Exists(File_)
                    ? JsonConvert.DeserializeObject<Dictionary<string, Entry>>(File.ReadAllText(File_))
                      ?? new Dictionary<string, Entry>()
                    : new Dictionary<string, Entry>();
            }
            catch { _map = new Dictionary<string, Entry>(); }
            return _map;
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

        /// <summary>
        /// Record a message. Returns (firstChatter, newStreakDay, streakLength):
        /// firstChatter is true once per app run for the very first person to chat;
        /// newStreakDay is true the first time each person chats today (streak updated).
        /// </summary>
        public (bool firstChatter, bool newStreakDay, int streak) OnMessage(string platform, string user)
        {
            var key = PointsService.Key(platform, user);
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            lock (_gate)
            {
                bool first = !_firstAwarded;
                _firstAwarded = true;

                var map = Load();
                if (!map.TryGetValue(key, out var e))
                {
                    map[key] = new Entry { LastDay = today, Streak = 1 };
                    Save();
                    return (first, true, 1);
                }
                if (e.LastDay == today)
                    return (first, false, e.Streak);

                // Yesterday (or today across midnight) continues the streak; older resets.
                bool continues = DateTime.TryParse(e.LastDay, out var last) &&
                                 (DateTime.Now.Date - last.Date).TotalDays <= 1.0;
                e.Streak = continues ? e.Streak + 1 : 1;
                e.LastDay = today;
                Save();
                return (first, true, e.Streak);
            }
        }
    }
}
