using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace GameTracker.Models
{
    public enum GameStatus
    {
        NotStarted,
        InProgress,
        Beaten
    }

    /// <summary>A timestamped moment within a session — e.g. a "clip this" marker for VOD editing.</summary>
    public class SessionMarker
    {
        public DateTime At { get; set; } = DateTime.Now;
        public string Text { get; set; } = string.Empty;

        [JsonIgnore] public string TextOrClip => string.IsNullOrWhiteSpace(Text) ? "(clip)" : Text;

        [JsonIgnore]
        public string Display =>
            $"{At:h:mm tt}  {TextOrClip}";
    }

    public class PlaySession
    {
        public DateTime Start { get; set; } = DateTime.Now;
        public DateTime? End { get; set; }
        public string Note { get; set; } = string.Empty;
        public List<SessionMarker> Markers { get; set; } = new();

        // Optional stream-time goal for this session, in minutes (0 = none).
        public double GoalMinutes { get; set; } = 0;

        [JsonIgnore] public bool IsActive => End == null;
        [JsonIgnore] public TimeSpan Duration => (End ?? DateTime.Now) - Start;

        [JsonIgnore] public string StartDisplay => Start.ToString("MMM d, yyyy  h:mm tt");

        [JsonIgnore]
        public string DurationDisplay
        {
            get
            {
                if (IsActive) return "● live";
                return FormatSpan(Duration);
            }
        }

        public static string FormatSpan(TimeSpan t)
        {
            if (t.TotalSeconds < 60) return $"{Math.Max(0, (int)t.TotalSeconds)}s";
            int h = (int)t.TotalHours;
            int m = t.Minutes;
            return h > 0 ? $"{h}h {m}m" : $"{m}m";
        }
    }

    public class Game
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Title { get; set; } = string.Empty;
        public GameStatus Status { get; set; } = GameStatus.NotStarted;
        public string Platform { get; set; } = string.Empty;
        public string Genre { get; set; } = string.Empty;
        public string Requester { get; set; } = string.Empty;
        public int Rating { get; set; } = 0;
        public string Notes { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; } = DateTime.Now;

        // Saved place in the GameFAQs guide window (last page + scroll position).
        public string GuideUrl { get; set; } = string.Empty;
        public double GuideScroll { get; set; } = 0;

        // User-defined options for this game's custom randomization wheel.
        public List<string> WheelItems { get; set; } = new();

        // Results rolled from the wheel — the active "challenges" list (also shown on the OBS overlay).
        public List<string> WheelResults { get; set; } = new();

        [JsonIgnore] public bool HasGuide => !string.IsNullOrWhiteSpace(GuideUrl);

        // Legacy quick-log timestamps (pre-session-tracking). Retained for back-compat.
        public List<DateTime> PlaySessions { get; set; } = new();

        // Timed play sessions with optional notes.
        public List<PlaySession> Sessions { get; set; } = new();

        [JsonIgnore] public PlaySession? ActiveSession => Sessions.FirstOrDefault(s => s.IsActive);
        [JsonIgnore] public bool IsSessionActive => ActiveSession != null;

        [JsonIgnore]
        public TimeSpan TotalPlayTime
        {
            get
            {
                var total = TimeSpan.Zero;
                foreach (var s in Sessions) total += s.Duration;
                return total;
            }
        }

        [JsonIgnore] public string TotalPlayTimeDisplay => PlaySession.FormatSpan(TotalPlayTime);

        [JsonIgnore] public bool HasPlayTime => TotalPlayTime.TotalSeconds >= 1;

        [JsonIgnore] public string PlayButtonLabel => IsSessionActive ? "■ Stop" : "▶ Start";

        [JsonIgnore]
        public DateTime? LastPlayed
        {
            get
            {
                DateTime? last = PlaySessions.Count > 0 ? PlaySessions[^1] : (DateTime?)null;
                foreach (var s in Sessions)
                {
                    var t = s.End ?? s.Start;
                    if (last == null || t > last) last = t;
                }
                return last;
            }
        }

        [JsonIgnore]
        public string LastPlayedDisplay
        {
            get
            {
                if (IsSessionActive) return "Playing now";
                if (LastPlayed == null) return "Never played";
                var days = (DateTime.Now - LastPlayed.Value).Days;
                if (days == 0) return "Today";
                if (days == 1) return "Yesterday";
                return $"{days} days ago";
            }
        }

        [JsonIgnore]
        public bool IsStale => !IsSessionActive && LastPlayed.HasValue &&
                               (DateTime.Now - LastPlayed.Value).Days > 30;
    }
}
