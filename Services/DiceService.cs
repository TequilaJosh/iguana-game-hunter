using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace GameTracker.Services
{
    /// <summary>
    /// Dice for the "!roll" chat command: "1d10", "d20", "3d6", "2d6+3", "1d20-1".
    /// One die reports its number; several dice list each roll, then the total.
    /// </summary>
    public static class DiceService
    {
        public const int MaxDice = 20;      // keeps the listed rolls to one readable chat line
        public const int MaxSides = 1000;
        public const int MaxModifier = 1000;
        public const string DefaultSpec = "1d20";

        // [count] d sides [+/- modifier], spaces allowed around the modifier sign.
        private static readonly Regex SpecRx = new(
            @"^\s*(\d{0,3})\s*[dD]\s*(\d{1,4})\s*(?:([+-])\s*(\d{1,4}))?\s*$",
            RegexOptions.Compiled);

        public sealed record Roll(int Count, int Sides, int Modifier, IReadOnlyList<int> Results)
        {
            public int Total => Results.Sum() + Modifier;

            /// <summary>Canonical spec, e.g. "3d6", "1d20+5".</summary>
            public string Spec => $"{Count}d{Sides}" +
                (Modifier > 0 ? $"+{Modifier}" : Modifier < 0 ? Modifier.ToString() : "");

            /// <summary>"7" for one die, "4, 2, 6 = 12" for several, with any modifier shown.</summary>
            public string Describe()
            {
                var listed = string.Join(", ", Results);
                var mod = Modifier > 0 ? $" (+{Modifier})" : Modifier < 0 ? $" ({Modifier})" : "";
                if (Results.Count == 1 && Modifier == 0) return listed;
                return $"{listed}{mod} = {Total}";
            }
        }

        /// <summary>Parse and roll. An empty spec rolls <see cref="DefaultSpec"/>.
        /// Returns false (with a short reason) for anything out of range or unparseable.</summary>
        public static bool TryRoll(string? spec, out Roll? roll, out string error)
        {
            roll = null;
            error = string.Empty;
            var s = string.IsNullOrWhiteSpace(spec) ? DefaultSpec : spec.Trim();

            var m = SpecRx.Match(s);
            if (!m.Success)
            {
                error = "that's not a dice roll";
                return false;
            }

            int count = m.Groups[1].Value.Length == 0 ? 1 : int.Parse(m.Groups[1].Value);
            int sides = int.Parse(m.Groups[2].Value);
            int mod = m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : 0;
            if (m.Groups[3].Value == "-") mod = -mod;

            if (count < 1 || count > MaxDice) { error = $"roll 1 to {MaxDice} dice at a time"; return false; }
            if (sides < 2 || sides > MaxSides) { error = $"dice need 2 to {MaxSides} sides"; return false; }
            if (Math.Abs(mod) > MaxModifier) { error = $"keep the modifier within ±{MaxModifier}"; return false; }

            var results = new int[count];
            for (int i = 0; i < count; i++)
                results[i] = RandomNumberGenerator.GetInt32(1, sides + 1);   // fair, uniform 1..sides

            roll = new Roll(count, sides, mod, results);
            return true;
        }
    }
}
