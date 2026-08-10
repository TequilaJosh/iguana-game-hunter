using System;
using System.Collections.Generic;
using System.Linq;

namespace GameTracker.Services
{
    /// <summary>
    /// One live chat poll at a time. Viewers vote with "!vote N" (one vote each; revoting
    /// switches). Results broadcast to the overlay's Poll block.
    /// </summary>
    public static class PollService
    {
        private static readonly object Gate = new();
        private static string _question = string.Empty;
        private static List<string> _options = new();
        private static readonly Dictionary<string, int> Votes = new();   // voter key -> option index
        private static bool _open;

        public static bool IsOpen { get { lock (Gate) return _open; } }
        public static bool HasPoll { get { lock (Gate) return _question.Length > 0; } }

        public static void Start(string question, List<string> options)
        {
            lock (Gate)
            {
                _question = question.Trim();
                _options = options.Select(o => o.Trim()).Where(o => o.Length > 0).Take(6).ToList();
                Votes.Clear();
                _open = _question.Length > 0 && _options.Count >= 2;
            }
            Push();
            if (IsOpen)
                OverlayServer.Toast("📊 New poll! Vote with !vote 1-" + CountOptions());
        }

        private static int CountOptions() { lock (Gate) return _options.Count; }

        /// <summary>Register a vote. Returns true if it counted (poll open + valid option).</summary>
        public static bool Vote(string platform, string user, int option1Based)
        {
            lock (Gate)
            {
                if (!_open || option1Based < 1 || option1Based > _options.Count) return false;
                Votes[PointsService.Key(platform, user)] = option1Based - 1;
            }
            Push();
            return true;
        }

        /// <summary>Close voting and highlight the winner on the overlay.</summary>
        public static void End()
        {
            string? winner = null;
            lock (Gate)
            {
                if (!_open) return;
                _open = false;
                var counts = CountVotes();
                int max = counts.Length > 0 ? counts.Max() : 0;
                if (max > 0)
                {
                    int wi = Array.IndexOf(counts, max);
                    winner = _options[wi];
                }
            }
            Push();
            OverlayServer.Toast(winner != null ? $"📊 Poll result: {winner}!" : "📊 Poll ended — no votes!",
                confetti: winner != null);
        }

        /// <summary>Remove the poll from the overlay entirely.</summary>
        public static void Clear()
        {
            lock (Gate)
            {
                _question = string.Empty;
                _options = new List<string>();
                Votes.Clear();
                _open = false;
            }
            Push();
        }

        private static int[] CountVotes()
        {
            var counts = new int[_options.Count];
            foreach (var v in Votes.Values)
                if (v >= 0 && v < counts.Length) counts[v]++;
            return counts;
        }

        /// <summary>Wire form for the overlay ("null" clears the block).</summary>
        public static object? Wire()
        {
            lock (Gate)
            {
                if (_question.Length == 0) return null;
                var counts = CountVotes();
                int max = counts.Length > 0 ? counts.Max() : 0;
                return new
                {
                    question = _question,
                    open = _open,
                    options = _options.Select((text, i) => new
                    {
                        text,
                        votes = counts[i],
                        winner = !_open && max > 0 && counts[i] == max,
                    }).ToArray(),
                };
            }
        }

        private static void Push() => OverlayServer.PushPoll(Wire());
    }
}
