using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tatva
{
    public enum Mode { Title, Play, Dead }

    public sealed class Obstacle
    {
        public bool Wall;
        public float S, A, W, Rot, Th, MinE, Hv;
        public bool Done;
    }

    /// <summary>What the player is asking for this frame: an angle to fly to, or a rotation axis.</summary>
    public struct SteerCmd
    {
        public bool HasTarget;
        public float Target; // screen angle, radians, y-up, counter-clockwise
        public float Axis;   // -1..1, used when there is no target
        public bool HasRate;
        public float Rate;   // angular speed in rad/s (gyro steering wheel), counter-clockwise positive
    }

    /// <summary>
    /// The whole game rule set: tunnel, level generator, collisions, scoring, zones.
    /// Pure logic (no rendering, no audio) so the editor self-test can run it headless.
    /// Port of index.html. Angles are y-up counter-clockwise; the ship starts at the bottom (-PI/2).
    /// </summary>
    public sealed class Sim
    {
        public const float TAU = Mathf.PI * 2f;
        public const int N = 12;
        public const float SEG = TAU / N;
        public const float R = 1f, WH = 0.38f, PR = 0.8f;    // tunnel radius, wall height, ship orbit radius
        public const float ZP = 2f, ZNEAR = 0.32f, ZFAR = 44f; // ship depth, clip planes
        public const float D = 1.6f;                           // world distance per beat
        public const float HB = 0.035f, GRAZE = 0.19f;         // hit forgiveness, graze window (radians)
        public const float Bottom = -Mathf.PI / 2f;
        public const float MaxRate = 12f;                      // fastest the ship can circle under tilt steering, rad/s

        public static readonly float[] PAL = { 326, 188, 42, 268, 150, 14, 205, 295 };
        public static readonly string[] ZNAMES = { "Ébredés", "Sodrás", "Aranyér", "Örvény", "Zöld fény", "Izzás", "Mélykék", "Tátva" };

        public Mode Mode = Mode.Title;
        public float T, Dist, Bpm = 112, BpmT = 112, Ts = 1, TsT = 1, SlowT;
        public float Theta = Bottom, Omega, Roll, Bx, By;
        public readonly List<Obstacle> Obs = new List<Obstacle>(256);
        public float NextBeat = 6;
        public int Zone;
        public float Hue = PAL[0], HueT = PAL[0];
        public int Score, Mult = 1, MultBeats, Grazes, Orbs, OrbStreak, Beats;
        public int LastStep = -1;
        public float DeadT;
        public float Shake, Flash, Warp, Punch;
        float lastGrazeS = -1;

        public event Action<int> Grazed;        // points
        public event Action<int, int> OrbTaken; // points, streak
        public event Action Died;
        public event Action<int> ZoneChanged;
        public event Action<int> Step;          // sixteenth-note index

        readonly System.Random rng;
        readonly List<Obstacle> group = new List<Obstacle>(8);

        public Sim(int seed) { rng = new System.Random(seed); }

        // ---------- helpers ----------
        public static float Mod(float x) { x %= TAU; return x < 0 ? x + TAU : x; }
        public static float AngDiff(float a, float b) { float d = Mod(a - b); return d > Mathf.PI ? d - TAU : d; }
        /// <summary>&lt;0: inside the arc by that much; &gt;=0: distance to the arc.</summary>
        public static float ArcInfo(float x, float a, float w)
        {
            float d = Mod(x - a);
            return d < w ? -Mathf.Min(d, w - d) : Mathf.Min(d - w, TAU - d);
        }
        float Rnd(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        int Ri(int n) => rng.Next(n);
        int Rs() => rng.NextDouble() < 0.5 ? -1 : 1;
        public float Velocity => D * Bpm / 60f;

        public void Reset(Mode mode)
        {
            Obs.Clear();
            Mode = mode; Dist = 0; NextBeat = 6; Zone = 0; HueT = PAL[0];
            Bpm = BpmT = 112; Ts = TsT = 1; SlowT = 0;
            Theta = Bottom; Omega = 0; Roll = 0;
            Score = 0; Mult = 1; MultBeats = 0; Grazes = 0; Orbs = 0; OrbStreak = 0; Beats = 0; lastGrazeS = -1;
            LastStep = -1; DeadT = 0;
            Shake = 0; Flash = 0; Punch = 0; Warp = 1;
        }

        // ---------- level generator ----------
        void WallGap(float beat, int gapLane, int gapLanes, float rot, bool? orb)
        {
            int lane = ((gapLane + gapLanes) % N + N) % N;
            Obs.Add(new Obstacle { Wall = true, S = beat * D, A = lane * SEG, W = (N - gapLanes) * SEG, Rot = rot, Th = 0.4f, MinE = 9 });
            if (orb ?? rng.NextDouble() < 0.45)
                Obs.Add(new Obstacle { Wall = false, S = beat * D, A = Mod((gapLane + gapLanes / 2f) * SEG), Rot = rot });
        }

        float Gen(float b)
        {
            int L = Mathf.Min(Mathf.FloorToInt(b / 32f), 9);
            int gap = L < 2 ? 4 : L < 7 ? 3 : 2;
            float rotK = L < 2 ? 0 : Mathf.Min(0.035f * L, 0.2f);
            // weighted pattern table, unlocked by level
            int count = 3 + (L >= 1 ? 2 : 0) + (L >= 2 ? 1 : 0) + (L >= 3 ? 2 : 0);
            int pick = Ri(count);
            string t = pick < 2 ? "gate" : pick < 3 ? "spiral" : pick < 4 ? "screw" : pick < 5 ? "twin" : pick < 6 ? "swing" : "helix";
            float nb = b;
            switch (t)
            {
                case "gate":
                {
                    int n = 3 + Ri(3); float sp = L < 4 ? 2 : 1.5f;
                    for (int i = 0; i < n; i++) { WallGap(nb, Ri(N), gap, Rs() * Rnd(0, rotK), null); nb += sp; }
                    break;
                }
                case "spiral":
                {
                    int n = 6 + Mathf.Min(L, 6), dir = Rs(), g0 = Ri(N);
                    for (int i = 0; i < n; i++) { WallGap(nb, g0 + dir * i, Mathf.Max(gap, 3), 0, i % 2 == 0); nb += 1; }
                    break;
                }
                case "screw":
                {
                    int n = 3 + Ri(2); float rot = Rs() * Rnd(0.12f, 0.2f + 0.02f * L);
                    for (int i = 0; i < n; i++) { WallGap(nb, Ri(N), gap, i % 2 == 1 ? rot : -rot, true); nb += 2; }
                    break;
                }
                case "twin":
                {
                    int gw = Mathf.Max(2, gap - 1), half = N / 2;
                    for (int i = 0; i < 3; i++)
                    {
                        int g = Ri(N); float rot = Rs() * rotK;
                        for (int off = 0; off <= half; off += half)
                        {
                            int lane = ((g + off + gw) % N + N) % N;
                            Obs.Add(new Obstacle { Wall = true, S = nb * D, A = lane * SEG, W = (half - gw) * SEG, Rot = rot, Th = 0.4f, MinE = 9, Hv = 20 });
                        }
                        Obs.Add(new Obstacle { Wall = false, S = nb * D, A = Mod((g + gw / 2f) * SEG), Rot = rot });
                        nb += 2;
                    }
                    break;
                }
                case "swing":
                {
                    int n = 4 + Ri(3), g = Ri(N);
                    for (int i = 0; i < n; i++) { WallGap(nb, g + (i % 2) * 6, 5, 0, i == n - 1); nb += 1.5f; }
                    break;
                }
                default: // helix: a twisting corridor on half beats
                {
                    int n = 12 + 2 * Mathf.Min(L, 6), dir = Rs(), g0 = Ri(N);
                    for (int i = 0; i < n; i++) { WallGap(nb, g0 + dir * i, gap + 1, 0, i % 3 == 0); nb += 0.5f; }
                    break;
                }
            }
            return nb + (L < 2 ? 2 : 1);
        }

        /// <summary>Attract-mode pilot: nearest free angle at the next wall group.</summary>
        public float AiTarget()
        {
            float sMin = float.PositiveInfinity;
            // a wall still alongside the ship (dz in [-Th, 0]) counts until it has fully passed
            foreach (var o in Obs) if (o.Wall && !o.Done && o.S - Dist > -o.Th && o.S < sMin) sMin = o.S;
            if (float.IsInfinity(sMin) || sMin - Dist > 12) return Theta;
            group.Clear();
            foreach (var o in Obs) if (o.Wall && Mathf.Abs(o.S - sMin) < 1e-4f) group.Add(o);
            float bestA = Theta, bestD = float.PositiveInfinity;
            for (int i = 0; i < 96; i++)
            {
                float a = i * TAU / 96f;
                bool ok = true;
                foreach (var o in group) if (ArcInfo(a, o.A, o.W) < 0.22f) { ok = false; break; }
                if (!ok) continue;
                float d = Mathf.Abs(AngDiff(a, Theta));
                if (d < bestD) { bestD = d; bestA = a; }
            }
            return bestA;
        }

        void SetZone(int z)
        {
            Zone = z;
            HueT = PAL[z % 8];
            BpmT = Mathf.Min(112 + z * 9, 180);
            Warp = 1;
            if (Mode == Mode.Play) { Flash = Mathf.Max(Flash, 0.35f); ZoneChanged?.Invoke(z); }
        }

        void Graze(Obstacle o)
        {
            if (lastGrazeS == o.S) return;
            lastGrazeS = o.S;
            Mult = Mathf.Min(Mult + 1, 12); MultBeats = 0; Grazes++;
            int pts = 50 * Mult; Score += pts;
            SlowT = 0.24f; Punch = 1; Shake = Mathf.Max(Shake, 0.18f);
            Grazed?.Invoke(pts);
        }

        void Collect()
        {
            Orbs++; OrbStreak++; MultBeats = 0;
            if (OrbStreak % 3 == 0) Mult = Mathf.Min(Mult + 1, 12);
            int pts = 25 * Mult; Score += pts;
            OrbTaken?.Invoke(pts, OrbStreak);
        }

        void Die()
        {
            Mode = Mode.Dead; DeadT = 0; TsT = 0.035f; Ts = 0.2f;
            Shake = 1; Flash = 1;
            Died?.Invoke();
        }

        // ---------- update ----------
        public void Update(float dtR, SteerCmd cmd)
        {
            T += dtR;
            if (Mode == Mode.Dead) { DeadT += dtR; TsT = DeadT < 0.8f ? 0.035f : 0.32f; }
            else if (SlowT > 0) { SlowT -= dtR; TsT = 0.38f; }
            else TsT = 1;
            Ts += (TsT - Ts) * Mathf.Min(1, dtR * (Mode == Mode.Dead ? 3 : 12));
            float dt = dtR * Ts;

            Bpm += (BpmT - Bpm) * Mathf.Min(1, dtR * 0.7f);
            float prev = Dist;
            Dist += Velocity * dt;

            int z = Mathf.FloorToInt(Dist / D / 32f);
            if (z != Zone) SetZone(z);
            Hue += AngDiff(HueT * Mathf.Deg2Rad, Hue * Mathf.Deg2Rad) * Mathf.Rad2Deg * Mathf.Min(1, dtR * 1.2f);

            // tunnel bend and roll grow with the zone
            float B = Mathf.Min(Zone, 6) * 0.0021f + (Mode == Mode.Title ? 0.004f : 0);
            Bx = Mathf.Sin(Dist * 0.07f) * B;
            By = Mathf.Sin(Dist * 0.053f + 1.3f) * B * 0.75f;
            float rollV = Zone >= 2 || Mode == Mode.Title ? Mathf.Sin(Dist * 0.031f) * 0.28f * Mathf.Min(1, Mathf.Max(Zone - 1, 1) / 3f) : 0;
            Roll += rollV * dt;

            while (NextBeat * D < Dist + ZFAR) NextBeat = Gen(NextBeat);

            // steering
            if (Mode != Mode.Dead)
            {
                float want;
                if (Mode == Mode.Title) want = Mathf.Clamp(AngDiff(AiTarget(), Theta) * 14, -9, 9);
                else if (cmd.HasTarget) want = Mathf.Clamp(AngDiff(cmd.Target - Roll, Theta) * 14, -10, 10);
                else if (cmd.HasRate) want = Mathf.Clamp(cmd.Rate, -MaxRate, MaxRate);
                else want = Mathf.Clamp(cmd.Axis, -1, 1) * 7;
                // tilt steering responds almost instantly; pointer/keys keep a little smoothing
                Omega += (want - Omega) * Mathf.Min(1, dtR * (cmd.HasRate ? 45 : 18));
                Theta = Mod(Theta + Omega * dtR * Mathf.Max(Ts, 0.6f));
            }

            // collisions, grazes and orbs
            for (int i = 0; i < Obs.Count; i++)
            {
                var o = Obs[i];
                if (o.Done || Mode == Mode.Dead) continue;
                float dz = o.S - Dist, dzPrev = o.S - prev;
                if (o.Wall)
                {
                    if (dz <= 0 && dzPrev > -o.Th)
                    {
                        float e = ArcInfo(Theta, o.A, o.W);
                        if (e < o.MinE) o.MinE = e;
                        if (e < -HB && Mode == Mode.Play) { Die(); break; }
                    }
                    if (dz < -o.Th)
                    {
                        o.Done = true;
                        if (Mode == Mode.Play && o.MinE >= -HB && o.MinE < GRAZE) Graze(o);
                    }
                }
                else if (dz <= 0)
                {
                    o.Done = true;
                    if (Mode == Mode.Play)
                    {
                        if (Mathf.Abs(AngDiff(Theta, o.A)) < 0.26f) Collect(); else OrbStreak = 0;
                    }
                }
            }
            if (Obs.Count > 0 && Obs[0].S - Dist < -4) Obs.RemoveAll(o => o.S - Dist <= -4);

            // beats: score and combo decay
            if (Mode == Mode.Play)
            {
                int b0 = Mathf.FloorToInt(prev / D), b1 = Mathf.FloorToInt(Dist / D);
                for (int b = b0; b < b1; b++)
                {
                    Beats++; Score += 10 * Mult;
                    if (++MultBeats >= 16 && Mult > 1) { Mult--; MultBeats = 0; }
                }
            }
            int step = Mathf.FloorToInt(Dist / D * 4);
            if (step - LastStep > 4) LastStep = step - 1;
            while (LastStep < step) { LastStep++; Step?.Invoke(LastStep); }

            Shake = Mathf.Max(0, Shake - dtR * 1.6f);
            Flash = Mathf.Max(0, Flash - dtR * 2.2f);
            Punch = Mathf.Max(0, Punch - dtR * 3);
            Warp = Mathf.Max(0, Warp - dtR * 0.8f);
        }
    }
}
