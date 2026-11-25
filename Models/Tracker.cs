using System;
using System.Collections.Generic;
using System.Linq;

namespace GameTracker.Models
{
    /// <summary>
    /// One thing to tick off during a randomizer run: a key item, a quest step, a boss,
    /// a party member — whatever the streamer wants on the board.
    ///
    /// Most items are a simple on/off toggle (<see cref="Max"/> is 0 or 1). Set
    /// <see cref="Max"/> above 1 for a countable ("Dragon Orbs 3/8") and the dashboard
    /// counts up on each click, wrapping back to zero after the last one.
    /// </summary>
    public class TrackerItem
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>Optional emoji/short glyph shown before the name (dashboard and overlay).</summary>
        public string Icon { get; set; } = string.Empty;

        /// <summary>Ticked off (used when <see cref="Max"/> is 0 or 1).</summary>
        public bool Got { get; set; }

        /// <summary>How many collected so far (only meaningful when <see cref="Max"/> &gt; 1).</summary>
        public int Count { get; set; }

        /// <summary>0 or 1 = a plain toggle; above 1 = a countable item.</summary>
        public int Max { get; set; }

        public bool IsCountable => Max > 1;

        /// <summary>True when this item counts as collected for the progress total.</summary>
        public bool Done => IsCountable ? Count >= Max : Got;

        /// <summary>One click: toggle, or count up and wrap round to zero past the last one.</summary>
        public void Advance(int delta = 1)
        {
            if (IsCountable)
            {
                var n = Count + delta;
                if (n > Max) n = 0;
                if (n < 0) n = Max;
                Count = n;
                Got = Count >= Max;
            }
            else
            {
                Got = !Got;
                Count = Got ? 1 : 0;
            }
        }

        public void Clear() { Got = false; Count = 0; }

        /// <summary>
        /// Read one item written the way the bundled catalog and the "add item" box do it:
        /// a plain name is a tick-box, and a trailing "*8" makes it a counter out of eight
        /// ("Dragon Orbs *8"). The star form is the only bit of syntax the streamer has to
        /// learn, so it is parsed in exactly one place.
        /// </summary>
        public static TrackerItem Parse(string? text)
        {
            var name = (text ?? string.Empty).Trim();
            int max = 0;
            int star = name.LastIndexOf('*');
            if (star > 0 && int.TryParse(name[(star + 1)..].Trim(), out var n) && n > 1)
            {
                max = Math.Min(n, 999);
                name = name[..star].Trim();
            }
            return new TrackerItem { Name = name, Max = max };
        }

        public TrackerItem Clone() => new()
        {
            Name = Name, Icon = Icon, Got = Got, Count = Count, Max = Max,
        };
    }

    /// <summary>A labelled section of a tracker set ("Key Items", "Quests", "Bosses"…).</summary>
    public class TrackerGroup
    {
        public string Name { get; set; } = string.Empty;
        public List<TrackerItem> Items { get; set; } = new();

        public TrackerGroup Clone() => new()
        {
            Name = Name,
            Items = (Items ?? new()).Select(i => i.Clone()).ToList(),
        };
    }

    /// <summary>
    /// A whole board for one game: the groups of items/quests, plus the live tick state
    /// of the current run. Built-in sets from <see cref="TrackerCatalog"/> are copied into
    /// the streamer's own list when added, so editing one never touches the catalog.
    /// </summary>
    public class TrackerSet
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = string.Empty;

        /// <summary>Console this set was written for (NES / SNES / PS1 / PS2 / Other).</summary>
        public string System { get; set; } = string.Empty;

        /// <summary>Optional Game Hunter game title — the board switches to this set by
        /// itself when that game is the one being streamed. Empty = never auto-switch.</summary>
        public string Game { get; set; } = string.Empty;

        /// <summary>Accent colour for the collected items (dashboard and overlay).</summary>
        public string Color { get; set; } = "#7cc44a";

        /// <summary>Catalog entry this was copied from, if any (informational).</summary>
        public string SourceId { get; set; } = string.Empty;

        public List<TrackerGroup> Groups { get; set; } = new();

        public IEnumerable<TrackerItem> AllItems =>
            (Groups ?? new()).SelectMany(g => g.Items ?? new List<TrackerItem>());

        public int Total => AllItems.Count();
        public int Done => AllItems.Count(i => i.Done);

        public string Progress => $"{Done}/{Total}";

        /// <summary>Wipe the run — every item back to not-collected. Keeps the item list.</summary>
        public void ResetRun()
        {
            foreach (var i in AllItems) i.Clear();
        }

        /// <summary>A deep copy. <paramref name="newId"/> gives it a fresh identity (used when
        /// adding a catalog set or duplicating one the streamer already has).</summary>
        public TrackerSet Clone(bool newId = false) => new()
        {
            Id = newId ? Guid.NewGuid().ToString("N") : Id,
            Name = Name, System = System, Game = Game, Color = Color,
            SourceId = SourceId,
            Groups = (Groups ?? new()).Select(g => g.Clone()).ToList(),
        };
    }
}
