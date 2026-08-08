using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NWaves.Effects;
using NWaves.Filters.Base;
using GameTracker.Models;

namespace GameTracker.Services
{
    /// <summary>
    /// Live streamer voice morph. Runs the mic through an NWaves DSP chain continuously
    /// (dry passthrough when no morph is active) and plays it to a chosen output device.
    /// Capture it in OBS with an "Application Audio Capture" source (no virtual cable
    /// needed), or route the output to a virtual cable if you prefer.
    /// Redeems activate a preset for its TimerSeconds; the overlay shows a countdown and
    /// the voice auto-reverts to dry at zero.
    /// </summary>
    public static class VoiceMorphService
    {
        private static readonly object Gate = new();
        private static WasapiCapture? _capture;
        private static WasapiOut? _out;
        private static BufferedWaveProvider? _buf;
        private static int _channels = 1;
        private static volatile Chain? _chain;      // null = dry passthrough
        private static Timer? _revert;
        private static string _activeName = string.Empty;

        public static bool IsRunning { get; private set; }
        public static string LastError { get; private set; } = string.Empty;
        public static string ActiveMorph => _activeName;

        /// <summary>Sentinel output meaning "process but play nothing back to the streamer".</summary>
        public const string NoneOutput = "(none)";

        private sealed class Chain
        {
            public PitchShiftVocoderEffect? Pitch;
            public IOnlineFilter? Fx;
            public float Process(float s)
            {
                if (Pitch != null) s = Pitch.Process(s);
                if (Fx != null) s = Fx.Process(s);
                return s;
            }
        }

        // ---- devices ----

        public static List<string> InputDevices()
        {
            try
            {
                using var e = new MMDeviceEnumerator();
                return e.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                        .Select(d => d.FriendlyName).ToList();
            }
            catch { return new List<string>(); }
        }

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

        private static MMDevice? Find(DataFlow flow, string name)
        {
            var e = new MMDeviceEnumerator();
            var all = e.EnumerateAudioEndPoints(flow, DeviceState.Active);
            var match = all.FirstOrDefault(d => d.FriendlyName == name);
            if (match != null) return match;
            try
            {
                return flow == DataFlow.Capture
                    ? e.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)
                    : e.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }
            catch { return null; }
        }

        // ---- engine ----

        /// <summary>Start (or restart) the mic chain with the saved settings.</summary>
        public static bool Start()
        {
            Stop();
            var s = SettingsService.LoadMorph();
            if (!s.Enabled) return false;

            lock (Gate)
            {
                try
                {
                    bool silent = s.OutputDevice == NoneOutput;
                    var mic = Find(DataFlow.Capture, s.InputDevice);
                    var spk = silent ? null : Find(DataFlow.Render, s.OutputDevice);
                    if (mic == null || (!silent && spk == null)) { LastError = "No audio device found."; return false; }

                    _capture = new WasapiCapture(mic, true, 20);
                    _channels = _capture.WaveFormat.Channels;
                    int sr = _capture.WaveFormat.SampleRate;

                    if (!silent)
                    {
                        _buf = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(sr, 1))
                        {
                            DiscardOnBufferOverflow = true,
                            BufferDuration = TimeSpan.FromSeconds(2),
                        };
                        _out = new WasapiOut(spk, AudioClientShareMode.Shared, true, 60);
                        _out.Init(_buf);
                        _out.Play();
                    }
                    // silent: _buf stays null; OnAudio still runs the chain but discards output.

                    _capture.DataAvailable += OnAudio;
                    _capture.StartRecording();

                    IsRunning = true;
                    LastError = string.Empty;
                    return true;
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    StopCore();
                    return false;
                }
            }
        }

        private static void OnAudio(object? sender, WaveInEventArgs e)
        {
            var chain = _chain;
            var buf = _buf;
            if (buf == null) return;
            try
            {
                // Incoming is IEEE float (WASAPI mix format). Downmix to mono, run the chain.
                var wb = new WaveBuffer(e.Buffer);
                int samples = e.BytesRecorded / 4;
                int frames = samples / Math.Max(1, _channels);
                var outBytes = new byte[frames * 4];
                var outWb = new WaveBuffer(outBytes);
                for (int f = 0; f < frames; f++)
                {
                    float sum = 0;
                    for (int c = 0; c < _channels; c++) sum += wb.FloatBuffer[f * _channels + c];
                    float s = sum / _channels;
                    if (chain != null) s = chain.Process(s);
                    outWb.FloatBuffer[f] = Math.Clamp(s, -1f, 1f);
                }
                buf.AddSamples(outBytes, 0, outBytes.Length);
            }
            catch { /* keep the stream alive */ }
        }

        public static void Stop()
        {
            lock (Gate) StopCore();
        }

        private static void StopCore()
        {
            try { if (_capture != null) { _capture.DataAvailable -= OnAudio; _capture.StopRecording(); _capture.Dispose(); } } catch { }
            try { _out?.Dispose(); } catch { }
            _capture = null; _out = null; _buf = null;
            IsRunning = false;
            ClearMorph(broadcast: false);
        }

        // ---- morph activation ----

        /// <summary>Build the DSP chain for a preset (shared with previews).</summary>
        private static Chain BuildChain(MorphPreset p, int sampleRate)
        {
            var chain = new Chain();
            if (p.PitchSemitones != 0)
                chain.Pitch = new PitchShiftVocoderEffect(sampleRate, Math.Pow(2, p.PitchSemitones / 12.0));
            chain.Fx = (IOnlineFilter?)(p.Effect switch
            {
                "robot" => new RobotEffect(hopSize: 128, fftSize: 512),
                "whisper" => new WhisperEffect(hopSize: 128, fftSize: 512),
                "echo" => new EchoEffect(sampleRate, 0.22f, 0.5f),
                "distortion" => new DistortionEffect(DistortionMode.SoftClipping, 18),
                "flanger" => new FlangerEffect(sampleRate),
                "vibrato" => new VibratoEffect(sampleRate),
                "tremolo" => new TremoloEffect(sampleRate, 0.7f, 7),
                "autowah" => new AutowahEffect(sampleRate),
                _ => (object?)null,
            });
            return chain;
        }

        /// <summary>Activate a saved morph by name; auto-reverts after its TimerSeconds.</summary>
        public static bool ActivateByName(string name)
        {
            var s = SettingsService.LoadMorph();
            var p = s.Presets.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (p == null) return false;
            return Activate(p);
        }

        public static bool Activate(MorphPreset p)
        {
            if (!IsRunning && !Start()) return false;
            int sr = _capture?.WaveFormat.SampleRate ?? 48000;
            _chain = BuildChain(p, sr);
            _activeName = p.Name;

            _revert?.Dispose();
            int secs = Math.Max(5, p.TimerSeconds);
            _revert = new Timer(_ => ClearMorph(broadcast: true), null, TimeSpan.FromSeconds(secs), Timeout.InfiniteTimeSpan);
            OverlayServer.SetMorph(p.Name, secs);
            return true;
        }

        /// <summary>Back to the streamer's normal (dry) voice.</summary>
        public static void ClearMorph(bool broadcast = true)
        {
            _chain = null;
            _activeName = string.Empty;
            _revert?.Dispose();
            _revert = null;
            if (broadcast) OverlayServer.SetMorph(null, 0);
        }
    }
}
