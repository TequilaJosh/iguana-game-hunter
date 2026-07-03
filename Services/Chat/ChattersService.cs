using System;
using System.Collections.Generic;
using System.Linq;
using GameTracker.Models;

namespace GameTracker.Services.Chat
{
    public enum ChatterState { Active, Lurking }

    public class Chatter
    {
        public string User = string.Empty;
        public string Platform = string.Empty;
        public DateTime LastChat = DateTime.Now;
        public double AccruedSeconds;              // time on the list, for interval point awards
        public ChatterState State = ChatterState.Active;
    }

    /// <summary>
    /// Tracks who is currently "in the room": anyone who chatted recently.
    /// Active (green) -> Lurking (yellow) after LurkMinutes idle -> removed after
    /// RemoveMinutes more. Everyone still on the list accrues interval points.
    /// </summary>
    public sealed class ChattersService
    {
        private readonly Dictionary<string, Chatter> _chatters = new();

        public void OnMessage(ChatMessage m)
        {
            if (string.IsNullOrWhiteSpace(m.User)) return;
            var key = PointsService.Key(m.Platform, m.User);
            if (_chatters.TryGetValue(key, out var c))
            {
                c.LastChat = DateTime.Now;
                c.State = ChatterState.Active;
            }
            else
            {
                _chatters[key] = new Chatter { User = m.User, Platform = m.Platform ?? string.Empty };
            }
        }

        /// <summary>
        /// Advance timers. Returns true if any state changed / anyone was removed,
        /// and how many point awards were handed out (already applied to PointsService).
        /// </summary>
        public (bool changed, int awards) Tick(double elapsedSeconds, ChatFeatureSettings f)
        {
            bool changed = false;
            int awards = 0;
            var lurkAfter = TimeSpan.FromMinutes(Math.Max(1, f.LurkMinutes));
            var removeAfter = lurkAfter + TimeSpan.FromMinutes(Math.Max(1, f.RemoveMinutes));
            var now = DateTime.Now;

            foreach (var (key, c) in _chatters.ToList())
            {
                var idle = now - c.LastChat;
                if (idle >= removeAfter)
                {
                    _chatters.Remove(key);
                    changed = true;
                    continue;
                }

                var newState = idle >= lurkAfter ? ChatterState.Lurking : ChatterState.Active;
                if (newState != c.State) { c.State = newState; changed = true; }

                if (f.PointsEnabled && f.PointsPerInterval > 0)
                {
                    c.AccruedSeconds += elapsedSeconds;
                    double interval = Math.Max(1, f.PointsIntervalMinutes) * 60.0;
                    while (c.AccruedSeconds >= interval)
                    {
                        c.AccruedSeconds -= interval;
                        PointsService.Add(c.Platform, c.User, f.PointsPerInterval);
                        awards++;
                    }
                }
            }

            if (awards > 0) PointsService.Save();
            return (changed, awards);
        }

        public int Count => _chatters.Count;

        public Dictionary<string, int> PerSource() =>
            _chatters.Values.GroupBy(c => c.Platform)
                     .ToDictionary(g => g.Key, g => g.Count());

        public List<Chatter> Snapshot() =>
            _chatters.Values
                     .OrderBy(c => c.State)                     // active first
                     .ThenBy(c => c.User, StringComparer.OrdinalIgnoreCase)
                     .ToList();
    }
}
