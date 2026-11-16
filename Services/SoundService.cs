using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Vorbis;
using GameTracker.Models;

namespace GameTracker.Services
{
    /// <summary>
    /// Plays streamer-defined sound alerts when their command appears in chat.
    /// Uses NAudio so it isn't limited to Windows Media Foundation codecs:
    ///   - .ogg            -> NAudio.Vorbis (managed Ogg Vorbis decoder)
    ///   - everything else -> NAudio AudioFileReader (wav/mp3/aiff natively;
    ///                        wma/m4a/aac/flac via Media Foundation)
    /// Output goes through WaveOutEvent, which needs no window or UI thread.
    /// </summary>
    public sealed class SoundService
    {
        // Each alert plus, when it's allowed to fire mid-sentence, a cached whole-word
        // matcher for its bare command (built once in SetAlerts, not per message).
        private sealed class Entry
        {
            public SoundAlert Alert = null!;
            public Regex? Word;
        }

        private List<Entry> _alerts = new();
        private readonly List<IWavePlayer> _active = new(); // keep players alive until they finish

        /// <summary>When true, incoming chat commands won't trigger sounds (panic/anti-spam).</summary>
        public bool Muted { get; set; }

        /// <summary>Global override: treat every alert as mid-sentence, whatever its own setting says.</summary>
        public bool MatchAnywhereGlobally
        {
            get => _matchAnywhereGlobally;
            set { _matchAnywhereGlobally = value; Rebuild(); }
        }
        private bool _matchAnywhereGlobally;

        // Playback target: "" = system default output. Set this to a device OBS isn't
        // capturing to stop alerts being picked up twice (see the Sound Alerts window).
        // Alerts can overlap, so the resolved-device cache is guarded.
        public string OutputDevice { get; set; } = "";
        private MMDevice? _outDev;
        private string _outDevName = "\0";   // sentinel so the first resolve always runs
        private readonly object _devGate = new();

        public static List<string> OutputDevices()
        {
            try
            {
                using var e = new MMDeviceEnumerator();
                return e.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                        .Select(d => d.FriendlyName).ToList();
            }
            catch { return new List<string>(); }
        }

        // Pick the output player: WASAPI to a named device (resampled to the device's mix
        // format by NAudio in shared mode), or the default WaveOut when none is chosen.
        private IWavePlayer BuildOutput()
        {
            lock (_devGate)
            {
                var name = OutputDevice ?? string.Empty;
                if (name != _outDevName)
                {
                    try { _outDev?.Dispose(); } catch { }
                    _outDev = null;
                    _outDevName = name;
                    if (name.Length > 0)
                    {
                        try
                        {
                            var e = new MMDeviceEnumerator();
                            _outDev = e.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                                       .FirstOrDefault(d => d.FriendlyName == name);
                        }
                        catch { _outDev = null; }
                    }
                }
                if (_outDev != null)
                {
                    try { return new WasapiOut(_outDev, AudioClientShareMode.Shared, true, 120); }
                    catch { /* device busy/unsupported — fall back */ }
                }
                return new WaveOutEvent();
            }
        }

        /// <summary>Immediately stop and dispose every currently-playing alert.</summary>
        public void StopAll()
        {
            foreach (var p in _active.ToList())
            {
                try { p.Stop(); } catch { }
                try { p.Dispose(); } catch { }
            }
            _active.Clear();
        }

        public void SetAlerts(IEnumerable<SoundAlert>? alerts)
        {
            _source = (alerts ?? Enumerable.Empty<SoundAlert>())
                .Where(a => !string.IsNullOrWhiteSpace(a.Command) && !string.IsNullOrWhiteSpace(a.FilePath))
                .ToList();
            Rebuild();
        }

        private List<SoundAlert> _source = new();

        private void Rebuild() =>
            _alerts = _source
                .Select(a => new Entry
                {
                    Alert = a,
                    Word = (_matchAnywhereGlobally || a.MatchAnywhere) ? WordMatcher(a.Command) : null,
                })
                .ToList();

        // Whole-word matcher for the command with any leading "!" stripped, so "!right"
        // fires on "you're right" but not on "bright". The lookarounds (rather than \b)
        // keep it correct for commands that start or end with punctuation.
        private static Regex? WordMatcher(string? command)
        {
            var bare = (command ?? string.Empty).Trim().TrimStart('!').Trim();
            if (bare.Length == 0) return null;
            return new Regex(@"(?<!\w)" + Regex.Escape(bare) + @"(?!\w)",
                             RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }

        /// <summary>
        /// Play the alert this message triggers, if any. An exact first-word match on the
        /// full command always wins; otherwise alerts allowed to fire mid-sentence match
        /// their bare word anywhere in the text. Returns the command played.
        /// </summary>
        public string? CheckAndPlay(string? message)
        {
            if (Muted || string.IsNullOrWhiteSpace(message) || _alerts.Count == 0) return null;

            var first = message.TrimStart().Split(new[] { ' ', '\t', '\r', '\n' }, 2)[0];
            var hit = _alerts.FirstOrDefault(e =>
                string.Equals(e.Alert.Command.Trim(), first, StringComparison.OrdinalIgnoreCase));

            hit ??= _alerts.FirstOrDefault(e => e.Word != null && e.Word.IsMatch(message));
            if (hit == null) return null;

            Play(hit.Alert.FilePath, hit.Alert.Volume);
            return hit.Alert.Command;
        }

        public void Play(string filePath) => Play(filePath, 1.0, null);

        /// <summary>Play a sound at <paramref name="volume"/> (0–1). <paramref name="onError"/> reports failures.</summary>
        public void Play(string filePath, double volume, Action<string>? onError = null)
        {
            if (string.IsNullOrWhiteSpace(filePath)) { onError?.Invoke("No sound file set."); return; }
            if (!File.Exists(filePath)) { onError?.Invoke("File not found: " + filePath); return; }

            WaveStream reader;
            try
            {
                var ext = Path.GetExtension(filePath).ToLowerInvariant();
                reader = ext == ".ogg"
                    ? new VorbisWaveReader(filePath)
                    : new AudioFileReader(filePath);
            }
            catch (Exception ex)
            {
                onError?.Invoke("Couldn't open this file: " + ex.Message);
                return;
            }

            var output = BuildOutput();
            void Cleanup()
            {
                try { output.Dispose(); } catch { }
                try { reader.Dispose(); } catch { }
                _active.Remove(output);
            }
            output.PlaybackStopped += (_, e) =>
            {
                if (e.Exception != null) onError?.Invoke("Couldn't play: " + e.Exception.Message);
                Cleanup();
            };

            try
            {
                output.Init(reader);
                // Not every WASAPI endpoint exposes per-stream volume; a failure here
                // shouldn't lose the alert, so fall back to playing it at full volume.
                try { output.Volume = (float)Math.Clamp(volume, 0.0, 1.0); }
                catch { }
                _active.Add(output);
                output.Play();
            }
            catch (Exception ex)
            {
                onError?.Invoke("Couldn't play: " + ex.Message);
                Cleanup();
            }
        }
    }
}
