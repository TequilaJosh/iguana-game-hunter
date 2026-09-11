using System;
using NAudio.Wave;

namespace GameTracker.Services
{
    // Runs one or more NWaves streaming effects (in order) over an NAudio sample stream.
    internal sealed class NWavesProvider : ISampleProvider
    {
        private readonly ISampleProvider _src;
        private readonly NWaves.Filters.Base.IOnlineFilter[] _fx;

        public NWavesProvider(ISampleProvider src, params NWaves.Filters.Base.IOnlineFilter[] fx)
        {
            _src = src;
            _fx = fx;
        }

        public WaveFormat WaveFormat => _src.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int n = _src.Read(buffer, offset, count);
            for (int i = 0; i < n; i++)
            {
                float s = buffer[offset + i];
                foreach (var f in _fx) s = f.Process(s);
                buffer[offset + i] = Math.Clamp(s, -1f, 1f);
            }
            return n;
        }
    }

    // Wraps an effect with a wet/dry mix so a mixer fader can dial its intensity 0..1
    // (0 = dry/off, 1 = full effect). Used by the Voice Mixer's per-effect bars.
    internal sealed class WetDryFilter : NWaves.Filters.Base.IOnlineFilter
    {
        private readonly NWaves.Filters.Base.IOnlineFilter _inner;
        private readonly float _wet;
        private readonly float _dry;

        public WetDryFilter(NWaves.Filters.Base.IOnlineFilter inner, float mix)
        {
            _inner = inner;
            _wet = Math.Clamp(mix, 0f, 1f);
            _dry = 1f - _wet;
        }

        public float Process(float x) => _dry * x + _wet * _inner.Process(x);
        public void Reset() => _inner.Reset();
    }

    // A compact mono reverb (Freeverb-style: parallel comb filters into series allpass
    // filters). NWaves has no reverb of its own, so this provides the big, spacious tail
    // used by the "heaven"/"cathedral"/"angelic" voices — implemented as an NWaves
    // IOnlineFilter so it drops into the same effect chains as everything else.
    internal sealed class ReverbFilter : NWaves.Filters.Base.IOnlineFilter
    {
        private readonly Comb[] _combs;
        private readonly Allpass[] _allpasses;
        private readonly float _wet;
        private readonly float _dry;

        // roomSize 0..1 (bigger = longer tail), damp 0..1 (more = darker), wet 0..1 (mix).
        public ReverbFilter(int sampleRate, float roomSize = 0.85f, float damp = 0.25f, float wet = 0.5f)
        {
            float scale = sampleRate / 44100f;
            int[] combTuning = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 };
            int[] apTuning = { 556, 441, 341, 225 };
            float feedback = roomSize * 0.28f + 0.7f;   // Freeverb room mapping
            float d = damp * 0.4f;

            _combs = new Comb[combTuning.Length];
            for (int i = 0; i < combTuning.Length; i++)
                _combs[i] = new Comb((int)(combTuning[i] * scale) + 1, feedback, d);

            _allpasses = new Allpass[apTuning.Length];
            for (int i = 0; i < apTuning.Length; i++)
                _allpasses[i] = new Allpass((int)(apTuning[i] * scale) + 1, 0.5f);

            _wet = Math.Clamp(wet, 0f, 1f);
            _dry = 1f - _wet * 0.5f;   // keep the voice present under the tail
        }

        public float Process(float x)
        {
            float input = x * 0.015f;   // Freeverb fixed input gain
            float wet = 0f;
            for (int i = 0; i < _combs.Length; i++) wet += _combs[i].Process(input);
            for (int i = 0; i < _allpasses.Length; i++) wet = _allpasses[i].Process(wet);
            return _dry * x + _wet * 3f * wet;
        }

        public void Reset()
        {
            foreach (var c in _combs) c.Reset();
            foreach (var a in _allpasses) a.Reset();
        }

        private sealed class Comb
        {
            private readonly float[] _buf;
            private readonly float _feedback, _damp1, _damp2;
            private int _pos;
            private float _store;
            public Comb(int size, float feedback, float damp)
            {
                _buf = new float[Math.Max(1, size)];
                _feedback = feedback; _damp1 = damp; _damp2 = 1f - damp;
            }
            public float Process(float x)
            {
                float y = _buf[_pos];
                _store = y * _damp2 + _store * _damp1;
                _buf[_pos] = x + _store * _feedback;
                if (++_pos >= _buf.Length) _pos = 0;
                return y;
            }
            public void Reset() { Array.Clear(_buf, 0, _buf.Length); _store = 0; _pos = 0; }
        }

        private sealed class Allpass
        {
            private readonly float[] _buf;
            private readonly float _feedback;
            private int _pos;
            public Allpass(int size, float feedback)
            {
                _buf = new float[Math.Max(1, size)];
                _feedback = feedback;
            }
            public float Process(float x)
            {
                float bufout = _buf[_pos];
                float y = -x + bufout;
                _buf[_pos] = x + bufout * _feedback;
                if (++_pos >= _buf.Length) _pos = 0;
                return y;
            }
            public void Reset() { Array.Clear(_buf, 0, _buf.Length); _pos = 0; }
        }
    }

    // Ring modulation — multiplies the signal by a low sine, giving a robotic/metallic tone.
    internal sealed class RingModProvider : ISampleProvider
    {
        private readonly ISampleProvider _src;
        private readonly double _inc;
        private double _phase;

        public RingModProvider(ISampleProvider src, double freqHz = 50)
        {
            _src = src;
            _inc = 2 * Math.PI * freqHz / src.WaveFormat.SampleRate;
        }

        public WaveFormat WaveFormat => _src.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int n = _src.Read(buffer, offset, count);
            int ch = Math.Max(1, WaveFormat.Channels);
            for (int i = 0; i < n; i += ch)
            {
                float m = (float)(0.5 + 0.5 * Math.Sin(_phase));
                for (int c = 0; c < ch && i + c < n; c++)
                    buffer[offset + i + c] *= m;
                _phase += _inc;
                if (_phase > Math.PI * 2) _phase -= Math.PI * 2;
            }
            return n;
        }
    }

    // Feedback echo — a spooky "ghost" repeat.
    internal sealed class EchoProvider : ISampleProvider
    {
        private readonly ISampleProvider _src;
        private readonly float[] _delay;
        private readonly float _decay;
        private int _pos;

        public EchoProvider(ISampleProvider src, double delaySeconds = 0.22, float decay = 0.5f)
        {
            _src = src;
            _decay = decay;
            int len = Math.Max(1, (int)(src.WaveFormat.SampleRate * delaySeconds) * Math.Max(1, src.WaveFormat.Channels));
            _delay = new float[len];
        }

        public WaveFormat WaveFormat => _src.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int n = _src.Read(buffer, offset, count);
            for (int i = 0; i < n; i++)
            {
                float echoed = buffer[offset + i] + _delay[_pos] * _decay;
                _delay[_pos] = echoed;
                buffer[offset + i] = echoed;
                if (++_pos >= _delay.Length) _pos = 0;
            }
            return n;
        }
    }

    // Tremolo — wobbling amplitude for an "alien" warble.
    internal sealed class TremoloProvider : ISampleProvider
    {
        private readonly ISampleProvider _src;
        private readonly double _inc;
        private readonly float _depth;
        private double _phase;

        public TremoloProvider(ISampleProvider src, double freqHz = 7, float depth = 0.6f)
        {
            _src = src;
            _depth = depth;
            _inc = 2 * Math.PI * freqHz / src.WaveFormat.SampleRate;
        }

        public WaveFormat WaveFormat => _src.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int n = _src.Read(buffer, offset, count);
            int ch = Math.Max(1, WaveFormat.Channels);
            for (int i = 0; i < n; i += ch)
            {
                float m = (float)(1.0 - _depth * (0.5 + 0.5 * Math.Sin(_phase)));
                for (int c = 0; c < ch && i + c < n; c++)
                    buffer[offset + i + c] *= m;
                _phase += _inc;
                if (_phase > Math.PI * 2) _phase -= Math.PI * 2;
            }
            return n;
        }
    }

    // Plays a cached mono float buffer once (used for the bundled chicken "bawk" clip).
    internal sealed class ClipSampleProvider : ISampleProvider
    {
        private readonly float[] _data;
        private readonly WaveFormat _fmt;
        private int _pos;

        public ClipSampleProvider(float[] data, int sampleRate)
        {
            _data = data;
            _fmt = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
        }

        public WaveFormat WaveFormat => _fmt;

        public int Read(float[] buffer, int offset, int count)
        {
            int n = Math.Min(count, _data.Length - _pos);
            if (n <= 0) return 0;
            Array.Copy(_data, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }
    }

    // Procedural cartoon chicken "bawk" — fallback censor if the bundled clip won't load. Generates a
    // buzzy, warbling squawk with a rise-then-fall pitch contour and a two-bump envelope,
    // so no audio file needs to be shipped. Finite length; returns 0 once done.
    internal sealed class ChickenSquawkProvider : ISampleProvider
    {
        private readonly WaveFormat _fmt;
        private readonly int _channels;
        private readonly double _sr;
        private readonly long _total;    // total frames
        private readonly double _baseHz;
        private long _pos;
        private double _phase;

        public ChickenSquawkProvider(int sampleRate, int channels, double seconds, int variation)
        {
            _fmt = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, Math.Max(1, channels));
            _channels = Math.Max(1, channels);
            _sr = sampleRate;
            _total = Math.Max(1, (long)(seconds * sampleRate));
            _baseHz = 600 + (variation % 6) * 30;   // slight per-word variety
        }

        public WaveFormat WaveFormat => _fmt;

        public int Read(float[] buffer, int offset, int count)
        {
            int frames = count / _channels;
            int written = 0;
            for (int f = 0; f < frames && _pos < _total; f++, _pos++)
            {
                double t = (double)_pos / _total;                 // 0..1 across the squawk
                double contour = t < 0.15 ? 0.7 + 2.0 * t          // quick rise …
                                          : 1.0 - 0.5 * (t - 0.15) / 0.85;  // … then fall
                double warble = 1.0 + 0.18 * Math.Sin(2 * Math.PI * 32 * t);
                _phase += _baseHz * contour * warble / _sr;
                double saw = 2.0 * (_phase - Math.Floor(_phase + 0.5));       // rich harmonics
                double sq = Math.Sign(Math.Sin(2 * Math.PI * _phase));        // nasal edge
                double tone = 0.75 * saw + 0.25 * sq;

                double atk = Math.Min(1.0, t / 0.02);              // 20 ms attack
                double rel = Math.Min(1.0, (1.0 - t) / 0.15);      // release near the end
                double bump = 0.6 + 0.4 * Math.Sin(2 * Math.PI * 1.3 * t);    // "b'GAWK" two-bump
                float s = (float)(0.28 * tone * atk * rel * bump);

                for (int c = 0; c < _channels; c++) buffer[offset + written++] = s;
            }
            return written;
        }
    }
}
