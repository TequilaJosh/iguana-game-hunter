using System;
using System.Collections.Generic;
using System.Linq;
using GameTracker.Models;

namespace GameTracker.Services
{
    /// <summary>
    /// The one owner of the randomizer tracker boards. The dashboard can be in two places
    /// at once (docked under chat and popped out into its own window) and the OBS overlay
    /// is a third view of the same thing, so they all read and write through here instead
    /// of each holding their own copy.
    ///
    /// Every mutation goes: change the model → <see cref="Save"/> → persisted, pushed to
    /// the overlay, and <see cref="Changed"/> raised so the other views redraw.
    /// </summary>
    public static class TrackerService
    {
        /// <summary>Raised after any change — on the thread that made it. UI handlers marshal.</summary>
        public static event Action? Changed;

        private static List<TrackerSet>? _sets;
        private static string _activeId = string.Empty;
        private static readonly object Gate = new();

        // Boards built before the colour became optional carry the old hard-coded green.
        // Nobody picked it deliberately, so hand them back to the app theme.
        private const string LegacyDefaultColor = "#7cc44a";

        private static void Ensure()
        {
            if (_sets != null) return;
            _sets = SettingsService.LoadTrackers();
            foreach (var s in _sets)
                if (string.Equals(s.Color, LegacyDefaultColor, StringComparison.OrdinalIgnoreCase))
                    s.Color = string.Empty;
            _activeId = SettingsService.LoadTrackerActiveId();
        }

        /// <summary>The streamer's boards, newest edits included. Mutate, then call <see cref="Save"/>.</summary>
        public static List<TrackerSet> Sets
        {
            get { lock (Gate) { Ensure(); return _sets!; } }
        }

        /// <summary>The board on the dashboard and the overlay (null when there are none).</summary>
        public static TrackerSet? Active
        {
            get
            {
                lock (Gate)
                {
                    Ensure();
                    return _sets!.FirstOrDefault(s => s.Id == _activeId) ?? _sets!.FirstOrDefault();
                }
            }
        }

        public static string ActiveId
        {
            get { lock (Gate) { Ensure(); return Active?.Id ?? string.Empty; } }
        }

        public static void SetActive(string? id)
        {
            lock (Gate) { Ensure(); _activeId = id ?? string.Empty; }
            Save();
        }

        /// <summary>Persist, push to OBS, and tell the open views to redraw.</summary>
        public static void Save()
        {
            CancelPending();
            Persist();
            Push();
            try { Changed?.Invoke(); } catch { /* a view blowing up must not break the save */ }
        }

        /// <summary>
        /// Something small changed — a single item ticked off. The overlay and the other
        /// open views update at once; the write to settings.json is held back briefly so
        /// clicking through a dozen items isn't a dozen whole-file writes.
        /// </summary>
        public static void Touch()
        {
            Push();
            try { Changed?.Invoke(); } catch { }
            lock (Gate)
            {
                _dirty = true;
                _persist?.Dispose();
                _persist = new System.Threading.Timer(_ => Persist(), null, 600,
                                                      System.Threading.Timeout.Infinite);
            }
        }

        /// <summary>Write anything still pending right now (window closing, app quitting).</summary>
        public static void Flush()
        {
            bool dirty;
            lock (Gate) dirty = _dirty;
            if (dirty) { CancelPending(); Persist(); }
        }

        private static System.Threading.Timer? _persist;
        private static bool _dirty;

        private static void CancelPending()
        {
            lock (Gate) { _persist?.Dispose(); _persist = null; }
        }

        private static void Persist()
        {
            List<TrackerSet> snapshot;
            string active;
            lock (Gate)
            {
                Ensure();
                snapshot = _sets!.Select(s => s.Clone()).ToList();  // decouple from live edits
                active = Active?.Id ?? string.Empty;
                _activeId = active;
                _dirty = false;
            }
            SettingsService.SaveTrackers(snapshot, active);
        }

        /// <summary>Send the active board to the overlay (no save).</summary>
        public static void Push() => OverlayServer.PushTracker(Active);

        /// <summary>Add a board and make it the active one. Returns what was added.</summary>
        public static TrackerSet Add(TrackerSet set)
        {
            lock (Gate)
            {
                Ensure();
                if (string.IsNullOrWhiteSpace(set.Id) || _sets!.Any(s => s.Id == set.Id))
                    set.Id = Guid.NewGuid().ToString("N");
                _sets!.Add(set);
                _activeId = set.Id;
            }
            Save();
            return set;
        }

        /// <summary>Copy a bundled catalog board into the streamer's own list (editable, fresh id).</summary>
        public static TrackerSet? AddFromCatalog(string catalogId)
        {
            var src = TrackerCatalog.ById(catalogId);
            if (src == null) return null;
            var copy = src.Clone(newId: true);
            copy.SourceId = src.Id;
            copy.ResetRun();
            return Add(copy);
        }

        public static void Remove(TrackerSet set)
        {
            lock (Gate)
            {
                Ensure();
                _sets!.Remove(set);
                if (_activeId == set.Id) _activeId = _sets!.FirstOrDefault()?.Id ?? string.Empty;
            }
            Save();
        }

        /// <summary>
        /// Switch to the board tied to this game title, if there is one. Called when a
        /// session starts so the right board is up without the streamer touching anything.
        /// </summary>
        public static bool SyncToGame(string? gameTitle)
        {
            if (string.IsNullOrWhiteSpace(gameTitle)) return false;
            string? id;
            lock (Gate)
            {
                Ensure();
                id = _sets!.FirstOrDefault(s =>
                    string.Equals(s.Game?.Trim(), gameTitle.Trim(), StringComparison.OrdinalIgnoreCase))?.Id;
                if (id == null || id == _activeId) return false;
            }
            SetActive(id);
            return true;
        }

        /// <summary>Re-read from disk (used after an external change, e.g. a settings import).</summary>
        public static void Reload()
        {
            lock (Gate) { _sets = null; Ensure(); }
            Push();
            try { Changed?.Invoke(); } catch { }
        }
    }
}
