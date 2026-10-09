using System;
using System.Collections.Generic;
using System.Linq;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace GameTracker.Services
{
    /// <summary>
    /// Plays a redeem video's soundtrack from Game Hunter itself (opt-in), while the overlay
    /// shows the video muted. That way an OBS capture of the app's audio — e.g. the "Voice
    /// Morpher" Application Audio Capture — carries redeem sound too, and the browser source
    /// doesn't need "Control audio via OBS".
    ///
    /// Sync: <see cref="Prepare"/> decodes and readies the audio the moment the redeem fires
    /// (so nothing slow happens later); the overlay then reports when the video actually starts
    /// playing and <see cref="Start"/> begins the sound at that point in the clip.
    /// </summary>
    public static class RedeemAudioService
    {
        private sealed class Clip
        {
            public AudioFileReader Reader = null!;
            public IWavePlayer Out = null!;
            public DateTime PreparedUtc;
            public bool Started;
        }

        private static readonly Dictionary<string, Clip> _clips = new();
        private static readonly object Gate = new();

        // A prepared clip the overlay never starts (no overlay open, video failed to load) is
        // dropped after this long.
        private static readonly TimeSpan Unclaimed = TimeSpan.FromSeconds(20);

        // The output adds about this much before sound leaves the device; start the audio this
        // far into the clip so it lines up with the picture.
        private const double OutputLatencySec = 0.06;

        /// <summary>Panic mute (from the chat window): new clips don't start while set.</summary>
        public static bool Muted { get; set; }

        /// <summary>Open the file's audio and get an output ready (silent until <see cref="Start"/>).
        /// False if the app can't decode the soundtrack — the caller then lets the browser play it.</summary>
        public static bool Prepare(string id, string path, double volume)
        {
            AudioFileReader? reader = null;
            IWavePlayer? output = null;
            try
            {
                reader = new AudioFileReader(path) { Volume = (float)Math.Clamp(volume, 0.0, 1.0) };
                output = new WasapiOut(AudioClientShareMode.Shared, 60);
                output.Init(reader);
            }
            catch
            {
                try { output?.Dispose(); } catch { }
                try { reader?.Dispose(); } catch { }
                return false;
            }

            output.PlaybackStopped += (_, _) => Remove(id);
            lock (Gate)
            {
                PruneLocked();
                _clips[id] = new Clip { Reader = reader, Out = output, PreparedUtc = DateTime.UtcNow };
            }
            return true;
        }

        /// <summary>The overlay's video just started, <paramref name="videoSeconds"/> in. Play the
        /// matching sound. Only the first report counts (several overlays may show the same video).</summary>
        public static void Start(string id, double videoSeconds)
        {
            Clip? clip;
            lock (Gate)
            {
                if (!_clips.TryGetValue(id, out clip) || clip.Started) return;
                clip.Started = true;
            }
            if (Muted) { Remove(id); return; }
            try
            {
                var at = Math.Max(0, videoSeconds + OutputLatencySec);
                if (at < clip.Reader.TotalTime.TotalSeconds) clip.Reader.CurrentTime = TimeSpan.FromSeconds(at);
                clip.Out.Play();
            }
            catch { Remove(id); }
        }

        /// <summary>Stop every redeem soundtrack (a new video replaced it, or panic mute).</summary>
        public static void StopAll()
        {
            List<string> ids;
            lock (Gate) ids = _clips.Keys.ToList();
            foreach (var id in ids) Remove(id);
        }

        /// <summary>For tests/diagnostics: is this clip prepared, and has it started?</summary>
        public static (bool prepared, bool started) State(string id)
        {
            lock (Gate) return _clips.TryGetValue(id, out var c) ? (true, c.Started) : (false, false);
        }

        private static void Remove(string id)
        {
            Clip? clip;
            lock (Gate)
            {
                if (!_clips.TryGetValue(id, out clip)) return;
                _clips.Remove(id);
            }
            try { clip.Out.Stop(); } catch { }
            try { clip.Out.Dispose(); } catch { }
            try { clip.Reader.Dispose(); } catch { }
        }

        private static void PruneLocked()
        {
            var cutoff = DateTime.UtcNow - Unclaimed;
            foreach (var id in _clips.Where(kv => !kv.Value.Started && kv.Value.PreparedUtc < cutoff)
                                     .Select(kv => kv.Key).ToList())
            {
                var c = _clips[id];
                _clips.Remove(id);
                try { c.Out.Dispose(); } catch { }
                try { c.Reader.Dispose(); } catch { }
            }
        }
    }
}
