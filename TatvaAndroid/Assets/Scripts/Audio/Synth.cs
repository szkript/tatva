using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Tatva
{
    /// <summary>
    /// Small sample-level synthesizer (port of the WebAudio instruments in index.html).
    /// The main thread queues timestamped notes; Render() runs on the audio thread.
    /// Signal path: voices -> (send -> dotted-eighth delay) -> master low-pass -> soft clip.
    /// </summary>
    public sealed class Synth
    {
        public enum Wave { Sine, Square, Saw, Triangle }
        public enum Filt { None, Low, High, Band }
        enum Kind { Tone, Noise, Bass, Pad }

        struct Ev
        {
            public long At; public Kind K; public float Dur, Vol, Att, F0, F1, Ramp;
            public Wave Wv; public bool Send; public Filt F; public float Fc0, Fc1, FcRamp, Q;
            public float N0, N1, N2;
        }

        sealed class Voice
        {
            public bool On; public long Start; public Kind K; public bool Send;
            public int Age, Len, AttS; public float Vol, Env, DecMul;
            public float Freq, FreqMul; public int FreqRamp;
            public Wave Wv; public float Ph;
            public readonly float[] Phs = new float[6], Inc = new float[6]; public int NOsc;
            public Filt F; public float Fc, FcMul; public int FcRamp; public float Kq, Ic1, Ic2, A1, A2, A3;
        }

        public readonly int Sr;
        readonly Voice[] voices = new Voice[48];
        readonly List<Ev> pending = new List<Ev>(64), taking = new List<Ev>(64);
        readonly float[] dly;
        int dw, dlyLen;
        long pos;
        uint seed = 22222;
        float gain = 0.8f, gainT = 0.8f;
        float cut = 20000, cutT = 20000, mIc1, mIc2, mA1, mA2, mA3;
        const float MK = 1f / 0.8f;

        public long Now => Interlocked.Read(ref pos);

        public Synth(int sampleRate)
        {
            Sr = sampleRate;
            for (int i = 0; i < voices.Length; i++) voices[i] = new Voice();
            dly = new float[(int)(sampleRate * 1.6f)];
            SetBpm(112);
            MasterCoeffs();
        }

        public void SetBpm(float bpm) { dlyLen = Mathf.Clamp((int)(0.75f * 60f / bpm * Sr), 1, dly.Length - 1); }
        public void SetMuted(bool m) { gainT = m ? 0 : 0.8f; }
        public void SetCutoff(float hz) { cutT = hz; }
        public static float Mtof(float m) => 440f * Mathf.Pow(2f, (m - 69f) / 12f);

        void Push(Ev e) { lock (pending) pending.Add(e); }

        // ---------- instruments ----------
        public void Tone(long at, Wave w, float f0, float f1, float ramp, float dur, float vol, bool send = false)
            => Push(new Ev { At = at, K = Kind.Tone, Wv = w, F0 = f0, F1 = f1, Ramp = ramp, Dur = dur, Vol = vol, Att = 0.004f, Send = send });

        public void Noise(long at, float dur, float vol, Filt f, float fc0, float fc1 = 0, float q = 0.8f)
            => Push(new Ev { At = at, K = Kind.Noise, Dur = dur, Vol = vol, Att = 0.001f, F = f, Fc0 = fc0, Fc1 = fc1, FcRamp = dur, Q = q });

        public void Bass(long at, float f, float dur, int zone)
        {
            float top = 420 + Mathf.Min(zone, 7) * 230;
            Push(new Ev { At = at, K = Kind.Bass, Wv = Wave.Saw, F0 = f, Dur = dur, Vol = 0.3f, Att = 0.006f, F = Filt.Low, Fc0 = top * 2.2f, Fc1 = 110, FcRamp = dur, Q = 7 });
        }

        public void Pad(long at, int n0, int n1, int n2, float dur)
            => Push(new Ev { At = at, K = Kind.Pad, Dur = dur, Vol = 0.03f, Att = dur * 0.3f, F = Filt.Low, Fc0 = 1300, Q = 0.7f, N0 = n0, N1 = n1, N2 = n2, Send = true });

        // ---------- audio thread ----------
        Voice Alloc()
        {
            Voice best = voices[0];
            foreach (var v in voices) { if (!v.On) return v; if (v.Age > best.Age) best = v; }
            return best;
        }

        void Start(Ev e)
        {
            var v = Alloc();
            v.On = true; v.Start = e.At; v.K = e.K; v.Send = e.Send;
            v.Len = Mathf.Max(1, (int)(e.Dur * Sr)); v.AttS = Mathf.Clamp((int)(e.Att * Sr), 1, v.Len - 1 > 0 ? v.Len - 1 : 1);
            v.Age = 0; v.Vol = e.Vol; v.Env = e.Vol;
            v.DecMul = Mathf.Pow(1e-4f, 1f / Mathf.Max(1, v.Len - v.AttS));
            v.Wv = e.Wv; v.Ph = 0; v.Freq = e.F0;
            if (e.F1 > 0 && e.Ramp > 0) { v.FreqRamp = Mathf.Max(1, (int)(e.Ramp * Sr)); v.FreqMul = Mathf.Pow(e.F1 / e.F0, 1f / v.FreqRamp); }
            else { v.FreqRamp = 0; v.FreqMul = 1; }
            v.NOsc = 0;
            if (e.K == Kind.Pad)
            {
                float[] notes = { e.N0, e.N1, e.N2 };
                foreach (var n in notes)
                    for (int d = -1; d <= 1; d += 2)
                    {
                        v.Inc[v.NOsc] = Mtof(n) * Mathf.Pow(2f, d * 8f / 1200f) / Sr;
                        v.Phs[v.NOsc] = (float)(v.NOsc * 0.17 % 1.0);
                        v.NOsc++;
                    }
            }
            v.F = e.F; v.Fc = e.Fc0; v.Kq = 1f / Mathf.Max(0.3f, e.Q); v.Ic1 = v.Ic2 = 0;
            if (e.Fc1 > 0 && e.FcRamp > 0) { v.FcRamp = Mathf.Max(1, (int)(e.FcRamp * Sr)); v.FcMul = Mathf.Pow(e.Fc1 / e.Fc0, 1f / v.FcRamp); }
            else { v.FcRamp = 0; v.FcMul = 1; }
            Coeffs(v);
        }

        void Coeffs(Voice v)
        {
            float g = Mathf.Tan(Mathf.PI * Mathf.Min(v.Fc, Sr * 0.45f) / Sr);
            v.A1 = 1f / (1f + g * (g + v.Kq)); v.A2 = g * v.A1; v.A3 = g * v.A2;
        }

        void MasterCoeffs()
        {
            float g = Mathf.Tan(Mathf.PI * Mathf.Min(cut, Sr * 0.45f) / Sr);
            mA1 = 1f / (1f + g * (g + MK)); mA2 = g * mA1; mA3 = g * mA2;
        }

        float Rand()
        {
            seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
            return (seed & 0xFFFFFF) / 8388608f - 1f;
        }

        static float Blep(float t, float dt)
        {
            if (t < dt) { t /= dt; return t + t - t * t - 1f; }
            if (t > 1f - dt) { t = (t - 1f) / dt; return t * t + t + t + 1f; }
            return 0f;
        }

        static float Osc(Wave w, ref float ph, float inc)
        {
            float y;
            switch (w)
            {
                case Wave.Sine: y = Mathf.Sin(ph * 2f * Mathf.PI); break;
                case Wave.Saw: y = 2f * ph - 1f - Blep(ph, inc); break;
                case Wave.Square:
                    y = (ph < 0.5f ? 1f : -1f) + Blep(ph, inc) - Blep((ph + 0.5f) % 1f, inc); break;
                default: y = 1f - 4f * Mathf.Abs(ph - 0.5f); break;
            }
            ph += inc; if (ph >= 1f) ph -= 1f;
            return y;
        }

        float Next(Voice v)
        {
            float env;
            if (v.Age < v.AttS) env = v.Vol * (v.Age + 1) / v.AttS;
            else { v.Env *= v.DecMul; env = v.Env; }
            v.Age++;
            if (v.Age >= v.Len) v.On = false;

            float x;
            switch (v.K)
            {
                case Kind.Noise: x = Rand(); break;
                case Kind.Pad:
                    x = 0;
                    for (int i = 0; i < v.NOsc; i++)
                    {
                        float p = v.Phs[i], inc = v.Inc[i];
                        x += 2f * p - 1f - Blep(p, inc);
                        p += inc; if (p >= 1f) p -= 1f; v.Phs[i] = p;
                    }
                    x *= 0.35f;
                    break;
                default:
                    x = Osc(v.Wv, ref v.Ph, v.Freq / Sr);
                    if (v.FreqRamp > 0) { v.Freq *= v.FreqMul; v.FreqRamp--; }
                    break;
            }

            if (v.F != Filt.None)
            {
                if (v.FcRamp > 0) { v.Fc *= v.FcMul; v.FcRamp--; if ((v.FcRamp & 15) == 0) Coeffs(v); }
                float v3 = x - v.Ic2, v1 = v.A1 * v.Ic1 + v.A2 * v3, v2 = v.Ic2 + v.A2 * v.Ic1 + v.A3 * v3;
                v.Ic1 = 2f * v1 - v.Ic1; v.Ic2 = 2f * v2 - v.Ic2;
                x = v.F == Filt.Low ? v2 : v.F == Filt.Band ? v1 : x - v.Kq * v1 - v2;
            }
            return x * env;
        }

        /// <summary>Fill an interleaved buffer. Called from OnAudioFilterRead (or the self-test).</summary>
        public void Render(float[] data, int channels)
        {
            int n = data.Length / channels;
            lock (pending) { taking.AddRange(pending); pending.Clear(); }
            foreach (var e in taking) Start(e);
            taking.Clear();

            long p0 = pos;
            for (int i = 0; i < n; i++)
            {
                long now = p0 + i;
                float mix = 0, send = 0;
                for (int k = 0; k < voices.Length; k++)
                {
                    var v = voices[k];
                    if (!v.On || now < v.Start) continue;
                    float y = Next(v);
                    mix += y; if (v.Send) send += y;
                }
                int dr = dw - dlyLen; if (dr < 0) dr += dly.Length;
                float dOut = dly[dr];
                dly[dw] = send + dOut * 0.38f;
                if (++dw >= dly.Length) dw = 0;
                mix += dOut * 0.45f;

                if ((i & 63) == 0)
                {
                    cut += (cutT - cut) * 0.08f;
                    gain += (gainT - gain) * 0.1f;
                    MasterCoeffs();
                }
                if (cut < 15000f)
                {
                    float v3 = mix - mIc2, v1 = mA1 * mIc1 + mA2 * v3, v2 = mIc2 + mA2 * mIc1 + mA3 * v3;
                    mIc1 = 2f * v1 - mIc1; mIc2 = 2f * v2 - mIc2;
                    mix = v2;
                }
                mix *= gain * 1.2f;
                mix = Mathf.Clamp(mix, -3f, 3f);
                mix = mix * (27f + mix * mix) / (27f + 9f * mix * mix); // soft clip
                for (int c = 0; c < channels; c++) data[i * channels + c] = mix;
            }
            Interlocked.Exchange(ref pos, p0 + n);
        }
    }

    /// <summary>The song: A minor, Am-F-C-G, layers added per zone. Port of music() in index.html.</summary>
    public static class Music
    {
        static readonly int[] Roots = { 33, 29, 36, 31 };
        static readonly int[][] Chords = { new[] { 57, 60, 64 }, new[] { 53, 57, 60 }, new[] { 55, 60, 64 }, new[] { 55, 59, 62 } };
        static readonly int[] BassPat = { 1, 0, 1, 1, 0, 1, 1, 1 };
        static readonly int[] Penta = { 0, 3, 5, 7, 10, 12, 15, 17, 19, 22, 24 };

        public static void Step(Synth s, int step, int z, float bpm, long at)
        {
            int bar = step / 16, st = step % 16, ci = bar % 4;
            var ch = Chords[ci];
            float stepDur = 60f / bpm / 4f;
            if (st % 4 == 0) s.Tone(at, Synth.Wave.Sine, 165, 42, 0.11f, 0.34f, 0.95f);
            if (st % 4 == 2) s.Noise(at, 0.06f, 0.32f, Synth.Filt.High, 7500);
            if (z >= 1 && st % 2 == 1) s.Noise(at, 0.035f, 0.12f, Synth.Filt.High, 9000);
            if (z >= 1 && (st == 4 || st == 12))
            {
                s.Noise(at, 0.17f, 0.45f, Synth.Filt.Band, 1500, 0, 0.9f);
                s.Noise(at + (long)(0.012f * s.Sr), 0.12f, 0.25f, Synth.Filt.Band, 1800, 0, 0.9f);
            }
            if (st % 2 == 0 && BassPat[st / 2] == 1)
                s.Bass(at, Synth.Mtof(Roots[ci] + (st == 6 || st == 14 ? 12 : 0)), stepDur * 1.8f, z);
            if (z >= 1 && st == 0) s.Pad(at, ch[0], ch[1], ch[2], stepDur * 16);
            if (z >= 2 && (z >= 4 || st % 2 == 0))
            {
                int[] arp = { ch[0], ch[1], ch[2], ch[0] + 12 };
                s.Tone(at, z >= 5 ? Synth.Wave.Saw : Synth.Wave.Square, Synth.Mtof(arp[st % 4] + 12 + (z >= 3 && st >= 8 ? 12 : 0)), 0, 0, stepDur * 1.6f, 0.04f, true);
            }
        }

        public static void Graze(Synth s, long at)
        {
            s.Noise(at, 0.24f, 0.55f, Synth.Filt.Band, 900, 6500, 2.2f);
            s.Tone(at, Synth.Wave.Sine, 1760, 2637, 0.08f, 0.3f, 0.12f, true);
        }

        public static void Orb(Synth s, long at, int k)
        {
            float n = 76 + Penta[Mathf.Clamp(k, 0, Penta.Length - 1)];
            s.Tone(at, Synth.Wave.Sine, Synth.Mtof(n), 0, 0, 0.4f, 0.17f, true);
            s.Tone(at, Synth.Wave.Triangle, Synth.Mtof(n + 12), 0, 0, 0.18f, 0.06f);
        }

        public static void Death(Synth s, long at)
        {
            s.Noise(at, 1.8f, 0.95f, Synth.Filt.Low, 5000, 70, 1.2f);
            s.Tone(at, Synth.Wave.Sine, 320, 26, 0.9f, 1.3f, 0.8f);
            s.Tone(at, Synth.Wave.Saw, 110, 28, 1.1f, 1.2f, 0.14f);
        }

        public static void Zone(Synth s, long at)
        {
            s.Noise(at, 0.9f, 0.35f, Synth.Filt.Band, 300, 9000, 1.6f);
            s.Noise(at, 1.6f, 0.22f, Synth.Filt.High, 5200, 0, 0.5f);
        }

        public static void Click(Synth s, long at) => s.Tone(at, Synth.Wave.Sine, 1320, 0, 0, 0.08f, 0.15f);
    }
}
