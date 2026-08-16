using System;
using System.Collections.Generic;
using System.Linq;

namespace GameTracker.Services
{
    /// <summary>
    /// Running stats for the current stream (since app launch): messages, chatters,
    /// redeems, points given, games played, challenges rolled.
    /// </summary>
    public static class StreamStatsService
    {
        private static readonly object Gate = new();
        private static readonly Dictionary<string, int> MessagesByUser = new();   // display name -> count
        private static readonly Dictionary<string, int> MessagesByPlatform = new();
        private static readonly HashSet<string> GamesPlayed = new();
        private static readonly Dictionary<string, int> GiftCoinsByUser = new(); // gifter -> total coins
        private static int _redeems, _requests, _pointsGiven, _challenges, _morphs, _videos, _clips;
        private static int _follows, _subs, _giftCount, _giftCoins;
        public static DateTime StartedAt { get; } = DateTime.Now;

        public static void CountMessage(string platform, string user)
        {
            lock (Gate)
            {
                var u = string.IsNullOrWhiteSpace(user) ? "?" : user;
                MessagesByUser[u] = MessagesByUser.TryGetValue(u, out var n) ? n + 1 : 1;
                var p = string.IsNullOrWhiteSpace(platform) ? "other" : platform;
                MessagesByPlatform[p] = MessagesByPlatform.TryGetValue(p, out var pn) ? pn + 1 : 1;
            }
        }

        public static void CountRedeem() { lock (Gate) _redeems++; }
        public static void CountRequest() { lock (Gate) _requests++; }
        public static void CountPoints(int amount) { lock (Gate) _pointsGiven += amount; }
        public static void CountChallenges(int rolled) { lock (Gate) _challenges += rolled; }
        public static void CountMorph() { lock (Gate) _morphs++; }
        public static void CountVideo() { lock (Gate) _videos++; }
        public static void CountClip() { lock (Gate) _clips++; }
        public static void CountFollow() { lock (Gate) _follows++; }
        public static void CountSubscribe() { lock (Gate) _subs++; }
        public static void CountGift(string user, int coins)
        {
            lock (Gate)
            {
                _giftCount++;
                _giftCoins += Math.Max(0, coins);
                var u = string.IsNullOrWhiteSpace(user) ? "?" : user;
                GiftCoinsByUser[u] = (GiftCoinsByUser.TryGetValue(u, out var n) ? n : 0) + Math.Max(0, coins);
            }
        }
        public static void CountGame(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return;
            lock (Gate) GamesPlayed.Add(title.Trim());
        }

        public sealed record Snapshot(
            TimeSpan Uptime, int TotalMessages, int UniqueChatters,
            List<(string user, int count)> TopChatters,
            List<(string platform, int count)> ByPlatform,
            List<string> Games, int Redeems, int Requests, int PointsGiven, int Challenges,
            int Morphs, int Videos, int Clips,
            int Follows, int Subs, int GiftCount, int GiftCoins,
            List<(string user, int coins)> TopGifters);

        public static Snapshot Get()
        {
            lock (Gate)
            {
                return new Snapshot(
                    DateTime.Now - StartedAt,
                    MessagesByUser.Values.Sum(),
                    MessagesByUser.Count,
                    MessagesByUser.OrderByDescending(kv => kv.Value).Take(5)
                                  .Select(kv => (kv.Key, kv.Value)).ToList(),
                    MessagesByPlatform.OrderByDescending(kv => kv.Value)
                                      .Select(kv => (kv.Key, kv.Value)).ToList(),
                    GamesPlayed.ToList(),
                    _redeems, _requests, _pointsGiven, _challenges, _morphs, _videos, _clips,
                    _follows, _subs, _giftCount, _giftCoins,
                    GiftCoinsByUser.OrderByDescending(kv => kv.Value).Take(5)
                                   .Select(kv => (kv.Key, kv.Value)).ToList());
            }
        }

        /// <summary>A Discord-ready text summary.</summary>
        public static string AsText()
        {
            var s = Get();
            var lines = new List<string>
            {
                "🦎 Stream recap",
                $"⏱ Uptime: {(int)s.Uptime.TotalHours}h {s.Uptime.Minutes}m",
                $"💬 {s.TotalMessages} messages from {s.UniqueChatters} chatters",
            };
            if (s.TopChatters.Count > 0)
                lines.Add("🏆 Top chatters: " + string.Join(", ",
                    s.TopChatters.Select(t => $"{t.user} ({t.count})")));
            if (s.Games.Count > 0)
                lines.Add("🎮 Played: " + string.Join(", ", s.Games));
            if (s.GiftCount > 0)
            {
                lines.Add($"🎁 Gifts: {s.GiftCount}" + (s.GiftCoins > 0 ? $" ({s.GiftCoins} coins)" : ""));
                if (s.TopGifters.Count > 0 && s.GiftCoins > 0)
                    lines.Add("💝 Top gifters: " + string.Join(", ", s.TopGifters.Select(t => $"{t.user} ({t.coins})")));
            }
            if (s.Follows > 0) lines.Add($"➕ New followers: {s.Follows}");
            if (s.Subs > 0) lines.Add($"⭐ New subs/members: {s.Subs}");
            if (s.Challenges > 0) lines.Add($"🎡 Challenges rolled: {s.Challenges}");
            if (s.Redeems > 0) lines.Add($"✨ Redeems fired: {s.Redeems}" + (s.Morphs > 0 ? $" ({s.Morphs} voice morphs)" : ""));
            if (s.Requests > 0) lines.Add($"📥 Game requests: {s.Requests}");
            if (s.Clips > 0) lines.Add($"🎬 Clips: {s.Clips}");
            if (s.PointsGiven > 0) lines.Add($"🪙 Points handed out: {s.PointsGiven}");
            return string.Join(Environment.NewLine, lines);
        }
    }
}
