using System;
using System.Collections.Generic;
using System.Linq;

namespace GameTracker.Models
{
    /// <summary>
    /// A named on-stream counter (deaths, catchphrases, "chaos" tallies…). It applies to one
    /// or more games (or every game) and keeps a SEPARATE value per game, so "Deaths" in one
    /// game never shares its count with another. Bumped with buttons or global hotkeys.
    /// </summary>
    public class GameCounter
    {
        public string Name { get; set; } = string.Empty;
        public string Color { get; set; } = "#7cc44a";
        public bool Show { get; set; } = true;

        /// <summary>Game titles this counter appears for. Empty = every game.</summary>
        public List<string> Games { get; set; } = new();

        /// <summary>Per-game values, keyed by game title ("" = the no-game / default bucket).</summary>
        public Dictionary<string, int> Values { get; set; } = new();

        public HotkeyBinding? IncHotkey { get; set; }
        public HotkeyBinding? DecHotkey { get; set; }

        // ── legacy fields (v1.0.55/56 single-value counters) — migrated on load ──
        public int Value { get; set; }
        public string Game { get; set; } = string.Empty;

        public bool AppliesTo(string? game) =>
            Games.Count == 0 || Games.Contains(game ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        public int ValueFor(string? game) => Values.TryGetValue(game ?? string.Empty, out var v) ? v : 0;

        public void SetValue(string? game, int value) => Values[game ?? string.Empty] = value;

        public string GamesSummary =>
            Games.Count == 0 ? "All games" : Games.Count == 1 ? Games[0] : $"{Games.Count} games";

        /// <summary>Bring a legacy single-value counter into the per-game shape.</summary>
        public void MigrateLegacy()
        {
            Games ??= new();
            Values ??= new();
            if (Games.Count == 0 && !string.IsNullOrWhiteSpace(Game)) Games.Add(Game);
            if (Values.Count == 0 && Value != 0) Values[Game ?? string.Empty] = Value;
            Value = 0;
            Game = string.Empty;
        }
    }
}
