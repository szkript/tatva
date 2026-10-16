using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tatva
{
    public enum Mode { Title, Play, Dead }
    public enum Kind { Box, Pyr, Shield, Bomb }

    public sealed class Obstacle
    {
        public Kind K;
        public float S, U, W, D, H, Hv;         // depth, lateral centre, width, length, height, hue offset
        public float Amp, Fr, Ph;               // sliding blocks: lateral amplitude, frequency, phase
        public bool Rise, Passed, Dead;          // rising column, already passed, destroyed or collected
        public float MinL = 9;                   // closest lateral clearance while alongside the ship
    }

    /// <summary>What the player is asking for this frame.</summary>
    public struct SteerCmd
    {
        public bool HasTunnelTarget; // gyro: Target is a track angle (lateral position / R) to fly to
        public float Target;
        public float Axis;           // keys / touch: -1..1 turn rate
        public bool Boost;
    }

    /// <summary>
    /// The whole SpeedX-style rule set (port of index.html on the speedx branch): the track, level generator,
    /// collisions, pickups, scoring and zones. Pure logic, so the editor self-test runs it headless.
    ///
    /// The track is a surface with lateral coordinate U (wraps every C) and forward coordinate S. Its cross-section
    /// bends with curvature K: 1/R = inside a tube, 0 = flat plain, -1/R = outside a tube. The ship's lateral
    /// position is kept as an angle, Theta = U / R, so one lap is 2π like the old ring tunnel and the gyro
    /// steering (which works in angles) carries over unchanged.
    /// </summary>
    public sealed class Sim
    {
        public const float TAU = Mathf.PI * 2f;
        public const float R = 3f, C = TAU * R, LW = C / NL;  // tube radius, circumference, lane width
        public const int NL = 16;                             // lanes
        public const float PW = 0.44f;                        // ship width
        public const float V0 = 26f, ACC = 0.42f, VMAX = 92f, BOOST = 1.45f, KMH = 9f;
        public const float ZL = 1200f;                        // distance per zone
        public const float VIEW = 105f;                       // obstacles are generated this far ahead
        public const float Bottom = -Mathf.PI / 2f;           // gravity reference for the gyro fusion
        public const float MaxRate = 12f;                     // fastest the ship can move under gyro steering, rad/s

        public static readonly float[] PAL = { 326, 188, 42, 268, 150, 14, 205, 295 };
        public static readonly string[] ZNAMES = { "Ébredés", "Sodrás", "Aranyér", "Örvény", "Zöld fény", "Izzás", "Mélykék", "Tátva" };
        public static readonly int[] SHAPES = { 1, 1, 0, 1, -1, 1, 0, -1 };
        public static string ShapeName(int sh) => sh > 0 ? "Cső" : sh == 0 ? "Síkság" : "Külső pálya";

        public Mode Mode = Mode.Title;
        public bool God;                                      // self-test only: collisions never kill
        public float T, Gt, Dist, Vb = 34, V = 34, RunT, Ts = 1, TsT = 1, SlowT, BoostA, MaxV;
        public float Theta, Omega, MaxR = 8, K = 1 / R, KT = 1 / R, Bx, By;
        public readonly List<Obstacle> Obs = new List<Obstacle>(512);
        public float NextS = 30;
        public int Zone;
        public float Hue = PAL[0], HueT = PAL[0];
        public float Bpm = 112, BpmT = 112, BeatPh;
        public int LastStep = -1;
        public float ScoreF; public int Score, Mult = 1, Grazes;
        public float MultT;
        public bool Shield; public float Inv;
        public float DarkV; public bool InDark;
        public float DeadT, Shake, Flash, Warp, Punch;
        public bool Boosting;
        readonly List<Vector2> darks = new List<Vector2>();
        float darkEnd = -1e9f, lastGrazeS = -1;

        public float U => Theta * R;
        public int Shape => SHAPES[Zone % 8];

        public event Action<int> Grazed;               // points
        public event Action ShieldTaken;
        public event Action<Obstacle> ShieldBroke;     // the obstacle it absorbed
        public event Action<int, List<Obstacle>> Bombed; // points, destroyed obstacles
        public event Action Died;
        public event Action<int> ZoneChanged;
        public event Action DarkStarted;
        public event Action<int> Step;                 // sixteenth-note index

        readonly System.Random rng;
        readonly List<Obstacle> blasted = new List<Obstacle>(64);

        public Sim(int seed) { rng = new System.Random(seed); }

        // ---------- helpers ----------
        public static float Mod(float x) { x %= TAU; return x < 0 ? x + TAU : x; }
        public static float AngDiff(float a, float b) { float d = Mod(a - b); return d > Mathf.PI ? d - TAU : d; }
        public static float WrapU(float x) => x - C * Mathf.Round(x / C);
        static float Smooth(float x) => x * x * (3 - 2 * x);
        float Rnd(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        int Ri(int n) => rng.Next(n);
        int Rs() => rng.NextDouble() < 0.5 ? -1 : 1;
        bool Chance(double p) => rng.NextDouble() < p;

        public float ObsU(Obstacle o) => o.Amp != 0 ? o.U + o.Amp * Mathf.Sin(Gt * o.Fr + o.Ph) : o.U;
        public static float RiseF(Obstacle o, float ds) => o.Rise ? Smooth(Mathf.Clamp01((24 - ds) / 7f)) : 1;
        /// <summary>Lateral clearance between the ship and an obstacle (&lt;0 = overlapping).</summary>
        public float Clearance(Obstacle o)
        {
            float hw = o.K == Kind.Pyr ? o.W * 0.36f : o.W / 2;
            return Mathf.Abs(WrapU(ObsU(o) - U)) - hw - PW / 2;
        }

        public void Reset(Mode mode)
        {
            Obs.Clear(); darks.Clear(); darkEnd = -1e9f;
            Mode = mode; Dist = 0; NextS = 100; Zone = 0; HueT = PAL[0];
            Vb = V = mode == Mode.Play ? V0 : 34; RunT = 0; Gt = 0; MaxV = 0; BoostA = 0;
            Bpm = BpmT = 112; Ts = TsT = 1; SlowT = 0; BeatPh = 0; LastStep = -1;
            Theta = 0; Omega = 0; K = KT = 1 / R;
            ScoreF = 0; Score = 0; Mult = 1; MultT = 0; Grazes = 0; lastGrazeS = -1; Shield = false; Inv = 0;
            InDark = false; DeadT = 0; Shake = 0; Flash = 0; Punch = 0; Warp = 1;
        }

        /// <summary>Starts a run further in (testing): at the beginning of zone z+1 with the speed it would have there.</summary>
        public void SkipTo(int z)
        {
            if (z <= 0) return;
            Dist = z * ZL - 60; NextS = Dist + 60; RunT = z * 25;
        }

        // ---------- level generator ----------
        Obstacle AddB(float s, float lane, Kind k = Kind.Box, float w = LW * 0.8f, float d = 1.4f, float h = 1f, float hv = 0)
        {
            var o = new Obstacle { K = k, S = s, U = (lane + 0.5f) * LW, W = w, D = d, H = h, Hv = hv };
            Obs.Add(o);
            return o;
        }
        // one curved block covering `lanes` neighbouring lanes, starting at lane `from`
        void Slab(float s, int from, int lanes, float d, float h, float hv)
        {
            Obs.Add(new Obstacle { K = Kind.Box, S = s, U = (from + lanes / 2f) * LW, W = lanes * LW - 0.08f, D = d, H = h, Hv = hv });
        }
        void Solo(float s, int lane)
        {
            int t = Ri(3);
            if (t == 0) AddB(s, lane);
            else if (t == 1) AddB(s, lane, Kind.Pyr, LW * 0.85f, 1.3f, 1.15f, 30);
            else AddB(s, lane, Kind.Box, 0.7f, 0.7f, 2.6f, -30);
        }
        int ShipLane => Mathf.FloorToInt(Mod(Theta) * R / LW);

        float Gen(float s)
        {
            int lz = Mathf.FloorToInt(s / ZL);
            bool play = Mode == Mode.Play;
            int L = play ? Mathf.Min(lz, 9) : 1, outside = SHAPES[lz % 8] < 0 ? 1 : 0;
            float sc = Mathf.Pow(Mathf.Max(1, Vb / 28f), 0.75f); // keep the time between obstacles sane as speed climbs
            var types = new List<int> { 0, 1, 2 };                // walls, scatter, pyramids
            if (L >= 1) { types.Add(3); types.Add(4); }           // snake, movers
            if (L >= 2) { types.Add(5); types.Add(0); }           // risers, walls
            if (L >= 4) { types.Add(3); types.Add(1); }
            int t = types[Ri(types.Count)];

            switch (t)
            {
                case 0: // walls with a gap
                {
                    int n = 2 + Ri(2) + (L >= 4 ? 1 : 0), gap = (L < 2 ? 4 : L < 5 ? 3 : 2) + outside, g = Ri(NL);
                    for (int i = 0; i < n; i++)
                    {
                        Slab(s, g + gap, NL - gap, 1.1f, 0.95f, 0);
                        s += (21 - L) * sc; g = (g + Rs() * (2 + Ri(3)) + NL) % NL;
                    }
                    break;
                }
                case 1: // scatter
                {
                    int n = 6 + 2 * L;
                    for (int i = 0; i < n; i++)
                    {
                        Solo(s, Chance(0.3) ? ShipLane + Ri(3) - 1 : Ri(NL));
                        if (L >= 3 && Chance(0.4)) Solo(s, Ri(NL));
                        s += Rnd(5, 9) * sc;
                    }
                    break;
                }
                case 2: // pyramid field
                {
                    int n = 10 + 2 * L;
                    for (int i = 0; i < n; i++) { AddB(s, Ri(NL), Kind.Pyr, LW * 0.85f, 1.3f, 1.15f, 30); s += Rnd(2.5f, 5) * sc; }
                    break;
                }
                case 3: // winding corridor
                {
                    int n = 9 + L, gw = (L < 6 ? 4 : 3) + outside, g = Ri(NL), dir = Rs();
                    float step = 4 * sc;
                    for (int i = 0; i < n; i++)
                    {
                        Slab(s, g + gw, NL - gw, step - 0.12f, 0.55f, 50);
                        s += step;
                        if (Chance(0.5)) g = (g + dir + NL) % NL;
                        if (Chance(0.2)) dir = -dir;
                    }
                    break;
                }
                case 4: // sliding blocks
                {
                    int n = 3 + Ri(3) + (L >> 2);
                    for (int i = 0; i < n; i++)
                    {
                        int m = L >= 4 && Chance(0.5) ? 2 : 1;
                        for (int j = 0; j < m; j++)
                        {
                            var o = AddB(s, Ri(NL), Kind.Box, LW * 1.1f, 1.6f, 0.9f, 90);
                            o.Amp = Rnd(1.5f, 3.5f) * LW; o.Fr = Rnd(1.2f, 2.2f); o.Ph = Rnd(0, TAU);
                        }
                        s += 13 * sc;
                    }
                    break;
                }
                default: // columns that rise out of the floor near the ship's lane
                {
                    int n = 4 + Ri(3);
                    for (int i = 0; i < n; i++) { AddB(s, ShipLane + Ri(5) - 2, Kind.Box, 0.8f, 0.8f, 2.8f, -60).Rise = true; s += Rnd(8, 11) * sc; }
                    break;
                }
            }

            float br = Rnd(12, 20) * sc;
            if (play)
            {
                if (Chance(0.16)) Obs.Add(new Obstacle { K = Kind.Shield, S = s + br / 2, U = (Ri(NL) + 0.5f) * LW });
                else if (L >= 1 && Chance(0.12)) Obs.Add(new Obstacle { K = Kind.Bomb, S = s + br / 2, U = (Ri(NL) + 0.5f) * LW });
                if (lz >= 2 && Chance(0.1) && s > darkEnd + 250) { darkEnd = s + 300 * sc; darks.Add(new Vector2(s, darkEnd)); }
            }
            return s + br;
        }

        /// <summary>Demo pilot: the free lateral offset (in U units) that needs the least steering.</summary>
        float AiOffset()
        {
            float look = 10 + V * 0.45f, best = 0, bestC = float.PositiveInfinity;
            for (int i = -24; i <= 24; i++)
            {
                float du = i * C / 48f, c = Mathf.Abs(du) * 0.4f;
                foreach (var o in Obs)
                {
                    if (o.Dead || (o.K != Kind.Box && o.K != Kind.Pyr)) continue;
                    float ds = o.S - Dist;
                    if (ds < -1 || ds > look) continue;
                    if (Mathf.Abs(WrapU(ObsU(o) - U - du)) < o.W / 2 + 0.6f) c += 20 + look - ds;
                }
                if (c < bestC) { bestC = c; best = du; }
            }
            return best;
        }
        /// <summary>Demo pilot's choice as a track angle (also the "nearest free gap" in the steering log).</summary>
        public float AiTarget() => Mod(Theta + AiOffset() / R);

        void SetZone(int z)
        {
            Zone = z;
            HueT = PAL[z % 8];
            BpmT = Mathf.Min(112 + z * 9, 180);
            KT = SHAPES[z % 8] / R;
            Warp = 1;
            if (Mode == Mode.Play) { Flash = Mathf.Max(Flash, 0.35f); ZoneChanged?.Invoke(z); }
        }

        void Graze(Obstacle o)
        {
            if (lastGrazeS == o.S) return;
            lastGrazeS = o.S;
            Mult = Mathf.Min(Mult + 1, 8); MultT = 0; Grazes++;
            int pts = 100 * Mult; ScoreF += pts;
            SlowT = 0.18f; Punch = 1; Shake = Mathf.Max(Shake, 0.15f);
            Grazed?.Invoke(pts);
        }

        void Bomb()
        {
            blasted.Clear();
            foreach (var o in Obs)
            {
                if (o.Dead || (o.K != Kind.Box && o.K != Kind.Pyr)) continue;
                float ds = o.S - Dist;
                if (ds < -0.5f || ds > 80) continue;
                o.Dead = true; blasted.Add(o);
            }
            int pts = 50 * blasted.Count * Mult; ScoreF += pts;
            Flash = 0.8f; Shake = Mathf.Max(Shake, 0.6f); Warp = Mathf.Max(Warp, 0.6f);
            Bombed?.Invoke(pts, blasted);
        }

        void Hit(Obstacle o)
        {
            if (God) return;
            if (!Shield) { Die(); return; }
            Shield = false; Inv = 1; o.Dead = true;
            Flash = 0.5f; Shake = Mathf.Max(Shake, 0.5f);
            ShieldBroke?.Invoke(o);
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
            else if (SlowT > 0) { SlowT -= dtR; TsT = 0.45f; }
            else TsT = 1;
            Ts += (TsT - Ts) * Mathf.Min(1, dtR * (Mode == Mode.Dead ? 3 : 12));
            float dt = dtR * Ts;

            // speed climbs for the whole run; boost multiplies it
            if (Mode == Mode.Play) { RunT += dt; Vb = Mathf.Min(V0 + ACC * RunT, VMAX); }
            else if (Mode == Mode.Title) Vb = 34;
            Boosting = Mode == Mode.Play && cmd.Boost;
            BoostA += ((Boosting ? 1 : 0) - BoostA) * Mathf.Min(1, dtR * 6);
            V = Vb * (1 + (BOOST - 1) * BoostA);
            if (Mode == Mode.Play) MaxV = Mathf.Max(MaxV, V);
            float prev = Dist;
            Dist += V * dt; Gt += dt;

            int z = Mathf.FloorToInt(Dist / ZL);
            if (z != Zone) SetZone(z);
            Hue += AngDiff(HueT * Mathf.Deg2Rad, Hue * Mathf.Deg2Rad) * Mathf.Rad2Deg * Mathf.Min(1, dtR * 1.2f);
            K += (KT - K) * Mathf.Min(1, dtR * 0.9f);
            Bpm += (BpmT - Bpm) * Mathf.Min(1, dtR * 0.7f);

            // gentle bends grow with the zone
            float B = 0.00035f + Mathf.Min(Zone, 6) * 0.00011f;
            Bx = Mathf.Sin(Dist * 0.011f) * B;
            By = Mathf.Sin(Dist * 0.008f + 1.3f) * B * 0.6f;

            // blackout stretches
            bool dark = false;
            foreach (var d in darks) if (Dist >= d.x && Dist < d.y) { dark = true; break; }
            if (dark && !InDark && Mode == Mode.Play) DarkStarted?.Invoke();
            InDark = dark;
            DarkV += ((dark ? 1 : 0) - DarkV) * Mathf.Min(1, dtR * 2);
            if (darks.Count > 0 && darks[0].y < Dist - 10) darks.RemoveAt(0);

            while (NextS < Dist + VIEW + 10) NextS = Gen(NextS);

            // steering, in track angle: lateral speed grows with forward speed
            MaxR = 7 + V * 0.1f;
            if (Mode != Mode.Dead)
            {
                float rate = MaxR / R, want, ease = 12;
                if (Mode == Mode.Title) want = Mathf.Clamp(AiOffset() * 1.5f, -1, 1) * rate;
                else if (cmd.HasTunnelTarget) { want = Mathf.Clamp(AngDiff(cmd.Target, Theta) * 18, -MaxRate, MaxRate); ease = 45; }
                else want = Mathf.Clamp(cmd.Axis, -1, 1) * rate;
                Omega += (want - Omega) * Mathf.Min(1, dtR * ease);
                Theta = Mod(Theta + Omega * dtR * Mathf.Max(Ts, 0.6f));
            }

            // collisions, grazes and pickups
            for (int i = 0; i < Obs.Count; i++)
            {
                var o = Obs[i];
                if (o.Dead || Mode == Mode.Dead) continue;
                float ds = o.S - Dist;
                if (o.K == Kind.Box || o.K == Kind.Pyr)
                {
                    float front = o.S - o.D / 2, back = o.S + o.D / 2;
                    if (back >= prev && front <= Dist)
                    {
                        float gap = Clearance(o);
                        if (gap < o.MinL) o.MinL = gap;
                        if (gap < -0.05f && Mode == Mode.Play && Inv <= 0 && RiseF(o, ds) > 0.5f) { Hit(o); continue; }
                    }
                    if (!o.Passed && back < Dist)
                    {
                        o.Passed = true;
                        if (Mode == Mode.Play && o.MinL >= -0.05f && o.MinL < 0.3f) Graze(o);
                    }
                }
                else if (Mode == Mode.Play)
                {
                    float r = o.K == Kind.Bomb ? 1.3f : 0.4f;
                    if (o.S + r >= prev && o.S - r <= Dist && Mathf.Abs(WrapU(o.U - U)) < (o.K == Kind.Bomb ? LW / 2 : 0.75f))
                    {
                        o.Dead = true;
                        if (o.K == Kind.Shield) { Shield = true; ShieldTaken?.Invoke(); }
                        else Bomb();
                    }
                }
            }
            if (Obs.Count > 0 && Obs[0].S - Dist < -10) Obs.RemoveAll(o => o.S - Dist <= -10);

            // score and combo decay
            if (Mode == Mode.Play)
            {
                ScoreF += V * dt * 0.25f * Mult * (1 + 0.5f * BoostA);
                Score = Mathf.FloorToInt(ScoreF);
                MultT += dt;
                if (MultT > 6 && Mult > 1) { Mult--; MultT = 0; }
                Inv = Mathf.Max(0, Inv - dtR);
            }

            // the music runs on its own clock (sixteenth notes)
            BeatPh += dt * Bpm / 60f * 4;
            int step = Mathf.FloorToInt(BeatPh);
            if (step - LastStep > 4) LastStep = step - 1;
            while (LastStep < step) { LastStep++; Step?.Invoke(LastStep); }

            Shake = Mathf.Max(0, Shake - dtR * 1.6f);
            Flash = Mathf.Max(0, Flash - dtR * 2.2f);
            Punch = Mathf.Max(0, Punch - dtR * 3);
            Warp = Mathf.Max(0, Warp - dtR * 0.8f);
        }
    }
}
