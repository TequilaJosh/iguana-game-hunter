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

    /// <summary>
    /// How a game was suggested — used to filter the picker wheel.
    /// User-editable and persisted (see SettingsService); the first entry is the default.
    /// </summary>
    public static class SuggestionTypes
    {
        public const string Fallback = "Suggested";
        private static readonly string[] Builtin = { "Suggested", "Point buy", "Cash buy" };

        /// <summary>The current list of suggestion types (first entry is the default).</summary>
        public static List<string> All { get; private set; } = new(Builtin);

        /// <summary>The default suggestion type (the first in the list).</summary>
        public static string Default => All.Count > 0 ? All[0] : Fallback;

        /// <summary>The built-in starter list, for "reset to defaults".</summary>
        public static List<string> Defaults() => new(Builtin);

        /// <summary>Replace the list (trimmed, de-duplicated, non-empty; falls back to built-in if empty).</summary>
        public static void Set(IEnumerable<string>? types)
        {
            var cleaned = (types ?? Enumerable.Empty<string>())
                .Select(t => (t ?? string.Empty).Trim())
                .Where(t => t.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            All = cleaned.Count > 0 ? cleaned : new List<string>(Builtin);
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
        public string SuggestionType { get; set; } = SuggestionTypes.Default;
        public int Rating { get; set; } = 0;
        public string Notes { get; set; } = string.Empty;

        /// <summary>Scheduled pop-up reminder for this game (null = none). Set from the card's
        /// right-click menu; keeps popping up on its repeat until the streamer turns it off.</summary>
        public GameReminder? Reminder { get; set; }

        [JsonIgnore] public bool HasReminder => Reminder?.Enabled == true;

        /// <summary>Card chip, e.g. "⏰ 7:00 PM" (today), "⏰ Sat 7:00 PM" (this week), "⏰ Oct 30".</summary>
        [JsonIgnore]
        public string ReminderChip => HasReminder ? "⏰ " + GameReminder.ShortWhen(Reminder!.NextAt) : string.Empty;

        [JsonIgnore]
        public string ReminderTip => !HasReminder ? string.Empty
            : $"Next reminder: {Reminder!.NextAt:ddd MMM d, h:mm tt} · repeats {Reminder.RepeatDisplay}" +
              (string.IsNullOrWhiteSpace(Reminder.Note) ? "" : "\n" + Reminder.Note) +
              "\nRight-click → ⏰ Reminder… to change or turn it off.";
        public DateTime DateAdded { get; set; } = DateTime.Now;

        // Saved place in the GameFAQs guide window (last page + scroll position).
        public string GuideUrl { get; set; } = string.Empty;
        public double GuideScroll { get; set; } = 0;

        // User-defined options for this game's custom randomization wheel.
        public List<string> WheelItems { get; set; } = new();

        // How many wheel items appear at once (the rest rotate in as they're landed on).
        public int WheelMax { get; set; } = 20;

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

    /// <summary>
    /// A pop-up reminder for a game: first shows at <see cref="NextAt"/>, then every
    /// <see cref="RepeatMinutes"/> (0 = just once) until the streamer turns it off.
    /// </summary>
    public class GameReminder
    {
        public bool Enabled { get; set; } = true;
        public DateTime NextAt { get; set; } = DateTime.Now;   // local time of the next pop-up
        public int RepeatMinutes { get; set; } = 0;             // 0 = once
        public string Note { get; set; } = string.Empty;        // e.g. "Stream it Saturday!"

        /// <summary>The repeat choices offered in the reminder window (minutes; 0 = once).</summary>
        public static readonly int[] RepeatChoices = { 0, 15, 30, 60, 120, 240, 720, 1440, 10080 };

        [JsonIgnore] public string RepeatDisplay => DescribeRepeat(RepeatMinutes);

        public static string DescribeRepeat(int minutes)
        {
            if (minutes <= 0) return "just once";
            if (minutes % 10080 == 0) return minutes == 10080 ? "every week" : $"every {minutes / 10080} weeks";
            if (minutes % 1440 == 0) return minutes == 1440 ? "every day" : $"every {minutes / 1440} days";
            if (minutes % 60 == 0) return minutes == 60 ? "every hour" : $"every {minutes / 60} hours";
            return $"every {minutes} minutes";
        }

        /// <summary>Compact "when": time today, weekday + time within a week, else the date.</summary>
        public static string ShortWhen(DateTime at)
        {
            var now = DateTime.Now;
            if (at.Date == now.Date) return at.ToString("h:mm tt");
            if (at > now && (at.Date - now.Date).TotalDays < 7) return at.ToString("ddd h:mm tt");
            return at.ToString("MMM d");
        }
    }
}
