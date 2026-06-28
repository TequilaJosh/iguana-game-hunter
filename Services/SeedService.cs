using System;
using System.Collections.Generic;
using System.Linq;
using GameTracker.Models;

namespace GameTracker.Services
{
    /// <summary>
    /// One-time, ADDITIVE seeding of bundled games (e.g. shared challenge wheels).
    /// Guarantees:
    ///   • only ever appends to the library — never edits or removes existing games;
    ///   • each seed is offered at most once (tracked in settings), so deleting it
    ///     won't make it reappear, and updates won't duplicate it;
    ///   • skips a seed if the user already has it (matched by Id OR title).
    /// Callers must only invoke this when the library loaded cleanly
    /// (GameDataService.LastLoadSucceeded), so a failed read can never be overwritten.
    /// </summary>
    public static class SeedService
    {
        private static IEnumerable<Game> Seeds() => new[] { FinalFantasyRenaissance() };

        /// <summary>Append any not-yet-applied bundled games. Returns true if the library changed.</summary>
        public static bool ApplySeeds(List<Game> games)
        {
            var applied = new HashSet<string>(SettingsService.LoadAppliedSeeds(),
                                              StringComparer.OrdinalIgnoreCase);
            bool gamesChanged = false, appliedChanged = false;

            foreach (var seed in Seeds())
            {
                var key = seed.Id.ToString();
                if (applied.Contains(key)) continue;           // already offered before

                applied.Add(key);
                appliedChanged = true;

                bool alreadyHave = games.Any(g =>
                    g.Id == seed.Id ||
                    string.Equals(g.Title?.Trim(), seed.Title, StringComparison.OrdinalIgnoreCase));

                if (!alreadyHave)
                {
                    games.Add(seed);                            // ADD only — existing games untouched
                    gamesChanged = true;
                }
            }

            if (appliedChanged) SettingsService.SaveAppliedSeeds(applied.ToList());
            return gamesChanged;
        }

        // A shared challenge wheel: spin to draft from 100 challenges (with intentional
        // duplicates — VETO ×3, CHAT'S CHOICE ×2, LAZER'S CHOICE ×2, -1 Party Member ×3).
        private static Game FinalFantasyRenaissance() => new Game
        {
            Id = Guid.Parse("f5ed0001-0000-4000-8000-000000000001"),
            Title = "Final Fantasy Renaissance",
            Status = GameStatus.NotStarted,
            Platform = "Nintendo Entertainment System",
            Genre = "RPG",
            SuggestionType = "Suggested",
            Notes = "Shared challenge wheel — spin to draft from 100 challenges.",
            DateAdded = new DateTime(2026, 6, 28),
            WheelItems = new List<string>
            {
                "-1 Party Member (cumulative)",
                "-1 Party Member (cumulative)",
                "-1 Party Member (cumulative)",
                "50 HEALS Max",
                "Any Armored",
                "Any Black",
                "Any DPR",
                "Any Mage",
                "Any Nature",
                "Any Non-Magic",
                "Any Support",
                "Any Weapon Specialist",
                "Any WEEB",
                "Any White",
                "Archer",
                "Bard",
                "Bla>Wh>Ti>R>G>Blu",
                "Black Mage",
                "Black SPELL MINIMUS",
                "Blue Mage",
                "CHAT'S CHOICE",
                "CHAT'S CHOICE",
                "CLONE LAST CLASS",
                "Dancer",
                "Dark Knight",
                "Fi>Thi>DK>Dra>MnK>SB",
                "Geomancer",
                "Green Mage",
                "Lancer",
                "LAZER'S CHOICE",
                "LAZER'S CHOICE",
                "Machinist",
                "Monk",
                "Must Defeat HADES",
                "Must Defeat TREX",
                "Must Defeat WARMECH",
                "No Bane Sword",
                "No Buckler",
                "No Class Side Quests",
                "No Class Skills",
                "No Cure Spells",
                "No Defense Blade",
                "No Dragon Armor",
                "No EXIT",
                "No FADE",
                "No FAST",
                "No Fire Damage",
                "No Flame Armor",
                "No FLOAT",
                "No Giant's Hall",
                "No Gold Bracelet",
                "No HEAL Spells",
                "No Healing Helm",
                "No Healing Staff",
                "No Holy Damage",
                "No Ice Armor",
                "No Ice Damage",
                "No KO",
                "No KYZOKU Farming",
                "No Life Magic",
                "No Lightning Damage",
                "No Limit",
                "No Mage Staff",
                "No Masamune",
                "No Non-Magic Healing Abilities",
                "No NUKE",
                "No Opal Bracelet",
                "No Overworld Abilities",
                "No Poison Damage",
                "No PoP",
                "No ProCape",
                "No ProRing",
                "No Regen Abilities",
                "No Regen Spells",
                "No Repels",
                "No Running",
                "No RUSE",
                "No Status Effect Spells",
                "No TELE",
                "No Thor Hammer",
                "No TMPR",
                "No WALL",
                "No WARP",
                "No Water Damage",
                "No White Shirt",
                "No Wind Damage",
                "No Wizard Staff",
                "No Zeus Gauntlet",
                "Red Mage",
                "Ronin",
                "Summoner",
                "Thief",
                "Time Mage",
                "Trainer",
                "VETO",
                "VETO",
                "VETO",
                "Warrior",
                "White Mage",
                "White SPELL MINIMUS",
            },
        };
    }
}
