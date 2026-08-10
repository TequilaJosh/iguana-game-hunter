using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Windows.Media.SpeechSynthesis;

namespace GameTracker.Services
{
    /// <summary>A "funny voice" = a base engine voice + an effect (pitch/rate + audio DSP).</summary>
    public sealed class VoiceProfile
    {
        public string Label { get; set; } = string.Empty;   // e.g. "David (robot)"
        public string Voice { get; set; } = string.Empty;   // OneCore voice display name
        public string Effect { get; set; } = "normal";      // effect key
    }

    /// <summary>
    /// Text-to-speech via the Windows OneCore engine, with offline "funny voice" effects
    /// (chipmunk/deep/robot/ghost/alien/demon) layered on with NAudio. Utterances play one
    /// at a time; on floods, extra messages are dropped.
    /// </summary>
    public sealed class TtsService : IDisposable
    {
        private readonly SpeechSynthesizer _synth = new();
        private readonly Queue<Item> _q = new();
        private readonly object _gate = new();
        private bool _speaking;
        private WaveOutEvent? _out;
        private WaveFileReader? _reader;
        private Stream? _stream;

        public int MaxBacklog { get; set; } = 5;

        private readonly record struct Item(string Text, string Voice, string Effect, int Rate, int Volume);

        // The effect palette. Pitch/Rate feed the engine; Dsp is applied to the audio.
        // Pool = included in the shipped defaults (voice picker + per-chatter random pool).
        // Echo-based effects stay defined — saved custom voices and existing per-chatter
        // assignments keep working, and the Voice Lab still offers them deliberately —
        // but they're no longer handed out by default.
        public sealed record Effect(string Key, string Label, double Pitch, double Rate, string Dsp,
                                    bool Pool = true);

        public static readonly Effect[] Effects =
        {
            new("normal",   "",         1.00, 1.00, "none"),
            new("deep",     "deep",     0.80, 0.95, "none"),
            new("high",     "high",     1.30, 1.05, "none"),
            new("chipmunk", "chipmunk", 1.75, 1.35, "none"),
            new("robot",    "robot",    1.00, 1.00, "robot"),
            new("ghost",    "ghost",    0.90, 0.95, "echo", Pool: false),
            new("alien",    "alien",    1.15, 1.00, "tremolo"),
            new("demon",    "demon",    0.50, 0.85, "echo", Pool: false),
            // NWaves-powered extras (shared with the streamer voice-morph engine)
            new("whisper",  "whisper",  1.00, 0.95, "nw:whisper"),
            new("gremlin",  "gremlin",  1.45, 1.10, "nw:distortion"),
            new("underwater","underwater",0.95, 0.95, "nw:autowah"),
            new("wobbly",   "wobbly",   1.00, 1.00, "nw:flanger"),
            new("haunted",  "haunted",  0.75, 0.90, "nw:vibrato"),
        };

        private static Effect Find(string? key) =>
            Effects.FirstOrDefault(e => e.Key == key) ?? Effects[0];

        // Voices to hide from the picker (odd/unwanted OneCore entries).
        private static readonly string[] Blocked = { "Jakub", "Helle" };

        public static List<string> InstalledVoices()
        {
            try
            {
                return SpeechSynthesizer.AllVoices
                    .Select(v => v.DisplayName)
                    .Where(name => !Blocked.Any(b => name.Contains(b, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }
            catch { return new List<string>(); }
        }

        public static List<VoiceProfile> BuildProfiles()
        {
            var list = new List<VoiceProfile>();
            foreach (var v in InstalledVoices())
            {
                var shortName = v.Replace("Microsoft ", "").Trim();
                foreach (var e in Effects.Where(x => x.Pool))
                    list.Add(new VoiceProfile
                    {
                        Voice = v,
                        Effect = e.Key,
                        Label = e.Label.Length == 0 ? shortName : $"{shortName} ({e.Label})",
                    });
            }
            return list;
        }

        /// <summary>The user's custom voices first, then the full built-in palette.</summary>
        public static List<VoiceProfile> AllProfiles(IEnumerable<Models.CustomVoice>? custom)
        {
            var list = new List<VoiceProfile>();
            if (custom != null)
                foreach (var c in custom)
                    if (!string.IsNullOrWhiteSpace(c.Name) && !string.IsNullOrWhiteSpace(c.Voice))
                        list.Add(new VoiceProfile { Label = "★ " + c.Name, Voice = c.Voice, Effect = c.Effect });
            list.AddRange(BuildProfiles());
            return list;
        }

        public void Speak(string text, string? voice, string? effect, int rate, int volume)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            lock (_gate)
            {
                if (_q.Count >= MaxBacklog) return;
                _q.Enqueue(new Item(text, voice ?? string.Empty, effect ?? "normal", rate, volume));
            }
            Pump();
        }

        private async void Pump()
        {
            Item it;
            lock (_gate)
            {
                if (_speaking || _q.Count == 0) return;
                it = _q.Dequeue();
                _speaking = true;
            }
            try
            {
                var fx = Find(it.Effect);
                if (!string.IsNullOrEmpty(it.Voice))
                {
                    var v = SpeechSynthesizer.AllVoices.FirstOrDefault(x => x.DisplayName == it.Voice);
                    if (v != null) _synth.Voice = v;
                }
                _synth.Options.AudioPitch = Math.Clamp(fx.Pitch, 0.0, 2.0);
                _synth.Options.SpeakingRate = Math.Clamp(fx.Rate * (1.0 + it.Rate * 0.05), 0.5, 6.0);
                _synth.Options.AudioVolume = Math.Clamp(it.Volume, 0, 100) / 100.0;

                var synthStream = await _synth.SynthesizeTextToStreamAsync(it.Text);
                _stream = synthStream.AsStreamForRead();
                _reader = new WaveFileReader(_stream);

                ISampleProvider sp = _reader.ToSampleProvider();
                int sr = sp.WaveFormat.SampleRate;
                sp = fx.Dsp switch
                {
                    "robot" => new RingModProvider(sp),
                    "echo" => new EchoProvider(sp),
                    "tremolo" => new TremoloProvider(sp),
                    "nw:whisper" => new NWavesProvider(sp, new NWaves.Effects.WhisperEffect(hopSize: 128, fftSize: 512)),
                    "nw:distortion" => new NWavesProvider(sp,
                        new NWaves.Effects.DistortionEffect(NWaves.Effects.DistortionMode.SoftClipping, 18)),
                    "nw:autowah" => new NWavesProvider(sp, new NWaves.Effects.AutowahEffect(sr)),
                    "nw:flanger" => new NWavesProvider(sp, new NWaves.Effects.FlangerEffect(sr)),
                    "nw:vibrato" => new NWavesProvider(sp, new NWaves.Effects.VibratoEffect(sr)),
                    _ => sp,
                };

                _out = new WaveOutEvent();
                _out.PlaybackStopped += (_, _) =>
                {
                    CleanupPlayback();
                    lock (_gate) { _speaking = false; }
                    Pump();
                };
                _out.Init(sp);
                _out.Play();
            }
            catch
            {
                CleanupPlayback();
                lock (_gate) { _speaking = false; }
            }
        }

        private void CleanupPlayback()
        {
            try { _out?.Dispose(); } catch { }
            try { _reader?.Dispose(); } catch { }
            try { _stream?.Dispose(); } catch { }
            _out = null; _reader = null; _stream = null;
        }

        public void StopAll()
        {
            lock (_gate) { _q.Clear(); }
            try { _out?.Stop(); } catch { }
            lock (_gate) { _speaking = false; }
        }

        public void Dispose()
        {
            StopAll();
            CleanupPlayback();
            try { _synth.Dispose(); } catch { }
        }
    }
}
