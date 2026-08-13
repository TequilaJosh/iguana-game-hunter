using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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
        private readonly List<IDisposable> _parts = new();   // readers/streams for the current utterance

        public int MaxBacklog { get; set; } = 5;

        // ── Bad-word filter ─────────────────────────────────────────────────
        // When on, listed words are replaced with a chicken "bawk" in the spoken
        // audio. A built-in profanity list always applies; the streamer adds extras.
        public bool BleepBadWords { get; set; } = true;

        // The exact tokens to bleep, taken from the streamer's editable word list
        // (seeded from Models.BadWordDefaults). Whole-word, case-insensitive matching
        // avoids false positives like "spices" from a "spic" entry.
        private readonly HashSet<string> _bleep = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Regex WordRx = new(@"[A-Za-z']+", RegexOptions.Compiled);
        private static readonly Regex SplitRx = new(@"([A-Za-z']+)", RegexOptions.Compiled);

        /// <summary>Rebuild the bleep set from the streamer's (seeded, editable) word list.</summary>
        public void SetBadWords(IEnumerable<string>? words)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (words != null)
                foreach (var w in words)
                {
                    var b = LettersOnly(w).ToLowerInvariant();
                    if (b.Length >= 2) set.Add(b);
                }
            lock (_gate) { _bleep.Clear(); foreach (var w in set) _bleep.Add(w); }
        }

        private static string LettersOnly(string s) =>
            new string(s.Where(char.IsLetter).ToArray());

        // Break text into ordered parts, flagging which are bad words to bleep.
        private List<(bool beep, string text)> Segment(string text)
        {
            var parts = new List<(bool, string)>();
            var buf = new StringBuilder();
            foreach (var piece in SplitRx.Split(text))
            {
                if (string.IsNullOrEmpty(piece)) continue;
                bool isWord = WordRx.IsMatch(piece) && piece.All(c => char.IsLetter(c) || c == '\'');
                bool bad;
                lock (_gate) bad = isWord && _bleep.Contains(LettersOnly(piece));
                if (bad)
                {
                    if (buf.Length > 0) { parts.Add((false, buf.ToString())); buf.Clear(); }
                    parts.Add((true, piece));
                }
                else buf.Append(piece);
            }
            if (buf.Length > 0) parts.Add((false, buf.ToString()));
            return parts;
        }

        // Build a combined provider: clean speech segments with a beep tone over each bad word.
        private async Task<ISampleProvider> BuildBleepedAsync(List<(bool beep, string text)> parts)
        {
            var built = new ISampleProvider?[parts.Count];
            WaveFormat? fmt = null;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i].beep) continue;
                var stream = (await _synth.SynthesizeTextToStreamAsync(parts[i].text)).AsStreamForRead();
                var reader = new WaveFileReader(stream);
                _parts.Add(reader); _parts.Add(stream);
                var sp = reader.ToSampleProvider();
                fmt ??= sp.WaveFormat;
                built[i] = sp;
            }
            int sr = fmt?.SampleRate ?? 22050;
            int ch = fmt?.Channels ?? 1;
            for (int i = 0; i < parts.Count; i++)
                if (parts[i].beep) built[i] = MakeCensor(sr, ch, parts[i].text.Length);

            return new ConcatenatingSampleProvider(built.Where(x => x != null).Select(x => x!).ToList());
        }

        // The bundled chicken "bawk" clip, decoded once to mono float at its native rate.
        private static float[]? _chicken;
        private static int _chickenSr;
        private static readonly object _chickenLock = new();

        private static void EnsureChicken()
        {
            if (_chicken != null) return;
            lock (_chickenLock)
            {
                if (_chicken != null) return;
                try
                {
                    var asm = typeof(TtsService).Assembly;
                    using var s = asm.GetManifestResourceStream("GameTracker.Assets.chicken.wav")
                                  ?? throw new FileNotFoundException("chicken.wav resource missing");
                    using var reader = new WaveFileReader(s);
                    ISampleProvider sp = reader.ToSampleProvider();
                    if (sp.WaveFormat.Channels == 2) sp = new StereoToMonoSampleProvider(sp);
                    _chickenSr = sp.WaveFormat.SampleRate;
                    var all = new List<float>(1 << 16);
                    var tmp = new float[4096];
                    int n;
                    while ((n = sp.Read(tmp, 0, tmp.Length)) > 0)
                        for (int i = 0; i < n; i++) all.Add(tmp[i]);
                    _chicken = all.ToArray();
                }
                catch { _chicken = Array.Empty<float>(); }
            }
        }

        // Censor sound: the bundled chicken "bawk" resampled to the speech format, padded
        // with a little silence. Falls back to a synth squawk if the clip won't load.
        private static ISampleProvider MakeCensor(int sampleRate, int channels, int wordLen)
        {
            EnsureChicken();
            ISampleProvider clip;
            if (_chicken != null && _chicken.Length > 0)
            {
                clip = new ClipSampleProvider(_chicken, _chickenSr);
                if (_chickenSr != sampleRate) clip = new WdlResamplingSampleProvider(clip, sampleRate);
            }
            else
            {
                clip = new ChickenSquawkProvider(sampleRate, 1, 0.5, wordLen);
            }
            if (channels == 2) clip = new MonoToStereoSampleProvider(clip);
            return new OffsetSampleProvider(clip)
            {
                DelayBy = TimeSpan.FromMilliseconds(30),
                LeadOut = TimeSpan.FromMilliseconds(30),
            };
        }

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

                ISampleProvider sp;
                var parts = (BleepBadWords && _bleep.Count > 0) ? Segment(it.Text) : null;
                if (parts != null && parts.Any(p => p.beep))
                {
                    sp = await BuildBleepedAsync(parts);
                }
                else
                {
                    var stream = (await _synth.SynthesizeTextToStreamAsync(it.Text)).AsStreamForRead();
                    var reader = new WaveFileReader(stream);
                    _parts.Add(reader); _parts.Add(stream);
                    sp = reader.ToSampleProvider();
                }
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
            _out = null;
            foreach (var d in _parts) { try { d.Dispose(); } catch { } }
            _parts.Clear();
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
