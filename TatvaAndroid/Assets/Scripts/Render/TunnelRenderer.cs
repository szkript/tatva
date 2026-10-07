using System.Collections.Generic;
using UnityEngine;

namespace Tatva
{
    /// <summary>
    /// Draws one frame of the tunnel into a Painter (port of draw() in index.html) and owns the
    /// purely visual effects: exhaust trail, speed dust, sparks and shockwaves.
    /// Screen space: pixels, origin at the centre, y up.
    /// </summary>
    public sealed class TunnelRenderer
    {
        struct Spark { public Vector2 P, V; public float Life, Max, Size, H, Rot, Vr; }
        struct Wave { public Vector2 P; public float R, Vr, Life, Max, H, W; }
        struct Dot { public float A, R, S, Life, Max; }
        struct Dust { public float A, R, S; }

        readonly List<Spark> sparks = new List<Spark>(512);
        readonly List<Wave> waves = new List<Wave>(16);
        readonly List<Dot> trail = new List<Dot>(128);
        readonly Dust[] dust = new Dust[170];
        readonly float[] zs = new float[25];
        readonly Vector2[] tmp = new Vector2[8];
        readonly System.Random rng = new System.Random(7);

        float W = 1080, H = 1920, dp = 2, F0 = 900, FF = 900;
        float PX, PY, PS;

        public Vector2 ShipPos { get; private set; }

        public TunnelRenderer()
        {
            for (int j = 0; j < zs.Length; j++) zs[j] = Sim.ZNEAR * Mathf.Pow(Sim.ZFAR / Sim.ZNEAR, j / 24f);
            ResetDust(0);
        }

        float Rnd(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        public void ResetDust(float dist)
        {
            for (int i = 0; i < dust.Length; i++)
                dust[i] = new Dust { A = Rnd(0, Sim.TAU), R = Rnd(0.12f, 0.97f), S = dist + Rnd(0, Sim.ZFAR) };
            sparks.Clear(); waves.Clear(); trail.Clear();
        }

        /// <param name="dpScale">physical pixels per "CSS pixel" (line widths, spark sizes)</param>
        public void Resize(float w, float h, float dpScale)
        {
            W = w; H = h; dp = dpScale;
            F0 = Mathf.Min(W, H) * 0.92f;
            FF = F0;
        }

        void Proj(Sim s, float z)
        {
            PS = FF / z;
            float q = (z - Sim.ZP) * (z - Sim.ZP);
            PX = s.Bx * q * PS;
            PY = s.By * q * PS;
        }

        static float Fog(float z)
        {
            if (z < Sim.ZP) return Mathf.Max(0, (z - Sim.ZNEAR) / (Sim.ZP - Sim.ZNEAR));
            float u = 1 - (z - Sim.ZP) / (Sim.ZFAR - Sim.ZP);
            return u <= 0 ? 0 : Mathf.Pow(u, 1.5f);
        }

        float Lw(float z) => dp * Mathf.Clamp(2.4f * Sim.ZP / z, 0.7f, 5f);

        Vector2 ComputeShip(Sim s, float ff)
        {
            float sc = ff / Sim.ZP, a = s.Theta + s.Roll;
            return new Vector2(Mathf.Cos(a) * Sim.PR * sc, Mathf.Sin(a) * Sim.PR * sc);
        }

        // ---------- effects ----------
        public void Burst(Vector2 p, int n, float hue, float spd, float life, float size)
        {
            for (int i = 0; i < n; i++)
            {
                float a = Rnd(0, Sim.TAU), v = Rnd(0.2f, 1f) * spd * dp;
                sparks.Add(new Spark
                {
                    P = p, V = new Vector2(Mathf.Cos(a) * v, Mathf.Sin(a) * v), Max = Rnd(0.4f, 1f) * life,
                    Size = Rnd(0.5f, 1f) * size * dp, H = hue, Rot = Rnd(0, Sim.TAU), Vr = Rnd(-12, 12)
                });
            }
        }

        public void OnGraze(Sim s) => Burst(ShipPos, 22, s.Hue, 700, 0.5f, 3);

        public void OnOrb(Sim s)
        {
            Burst(ShipPos, 14, s.Hue + 150, 450, 0.45f, 2.5f);
            waves.Add(new Wave { P = ShipPos, R = 6 * dp, Vr = 900 * dp, Max = 0.35f, H = s.Hue + 150, W = 3 });
        }

        public void OnDeath(Sim s)
        {
            Burst(ShipPos, 160, s.Hue + 180, 2400, 2.2f, 5);
            Burst(ShipPos, 70, s.Hue, 1500, 2.6f, 7);
            for (int i = 0; i < 3; i++)
                waves.Add(new Wave { P = ShipPos, R = 4 * dp, Vr = (1600 + i * 900) * dp, Max = 1.4f + i * 0.3f, H = i == 1 ? s.Hue + 180 : s.Hue, W = 6 - i * 1.5f });
        }

        public void Tick(Sim s, float dtR)
        {
            float dt = dtR * s.Ts;
            if (s.Mode != Mode.Dead)
                for (int i = 0; i < 2; i++)
                    trail.Add(new Dot { A = s.Theta + Rnd(-0.03f, 0.03f), R = Sim.PR + Rnd(-0.02f, 0.02f), S = s.Dist - Rnd(0, 0.05f), Max = Rnd(0.25f, 0.45f) });
            for (int i = trail.Count - 1; i >= 0; i--)
            {
                var p = trail[i]; p.Life += dtR;
                if (p.Life > p.Max) trail.RemoveAt(i); else trail[i] = p;
            }
            float drag = Mathf.Pow(0.12f, dt);
            for (int i = sparks.Count - 1; i >= 0; i--)
            {
                var p = sparks[i]; p.Life += dt;
                if (p.Life > p.Max) { sparks.RemoveAt(i); continue; }
                p.P += p.V * dt; p.V *= drag; p.Rot += p.Vr * dt;
                sparks[i] = p;
            }
            for (int i = waves.Count - 1; i >= 0; i--)
            {
                var w = waves[i]; w.Life += dt; w.R += w.Vr * dt * (1 - w.Life / w.Max);
                if (w.Life > w.Max) waves.RemoveAt(i); else waves[i] = w;
            }
        }

        // ---------- frame ----------
        public void Draw(Sim s, Painter p)
        {
            float h = s.Hue;
            float beatFrac = Mathf.Repeat(s.Dist / Sim.D, 1f), bp = Mathf.Pow(1 - beatFrac, 5);
            FF = F0 * (1 + 0.06f * s.Punch + (s.Mode == Mode.Play ? 0.018f * bp : 0) + 0.1f * s.Warp * s.Warp);
            ShipPos = ComputeShip(s, FF);
            p.Clear();

            // light at the end of the tunnel
            Proj(s, Sim.ZFAR * 0.85f);
            p.Glow(new Vector2(PX, PY), FF * 0.42f, Painter.Hsl(h, 100, 62, 0.4f));

            // beat rings
            int k0 = Mathf.CeilToInt((s.Dist + Sim.ZNEAR - Sim.ZP) / Sim.D), k1 = Mathf.FloorToInt((s.Dist + Sim.ZFAR - Sim.ZP) / Sim.D);
            for (int k = k1; k >= k0; k--)
            {
                float z = Sim.ZP + k * Sim.D - s.Dist, f = Fog(z);
                if (f < 0.01f) continue;
                Proj(s, z);
                float near = Mathf.Exp(-Mathf.Abs(z - Sim.ZP) * 2.2f);
                bool bar = ((k % 4) + 4) % 4 == 0;
                float a = f * (bar ? 0.5f : 0.2f) + near * 0.5f + s.Warp * 0.3f * f;
                p.Ring(new Vector2(PX, PY), Sim.R * PS, Lw(z) * (bar ? 1.2f : 0.8f), Painter.Hsl(h + 30, 90, 60 + near * 30, Mathf.Min(1, a)));
            }

            // lane rails, per depth slice so they fade with the fog
            for (int j = zs.Length - 2; j >= 0; j--)
            {
                float za = zs[j], zb = zs[j + 1], f = Fog((za + zb) / 2);
                if (f < 0.01f) continue;
                Proj(s, za); float ax = PX, ay = PY, aS = PS;
                Proj(s, zb);
                var col = Painter.Hsl(h + 40, 80, 62, f * 0.28f);
                for (int i = 0; i < Sim.N; i++)
                {
                    float an = i * Sim.SEG + s.Roll, c = Mathf.Cos(an), sn = Mathf.Sin(an);
                    p.Line(new Vector2(ax + c * Sim.R * aS, ay + sn * Sim.R * aS), new Vector2(PX + c * Sim.R * PS, PY + sn * Sim.R * PS), Lw(za) * 0.7f, col);
                }
            }

            // speed dust
            float v = s.Velocity * s.Ts, streak = Mathf.Max(0.05f, v * 0.05f * (1 + s.Warp * 10));
            for (int i = 0; i < dust.Length; i++)
            {
                var d = dust[i];
                float z = Sim.ZP + d.S - s.Dist;
                if (z < Sim.ZNEAR)
                {
                    d.S += Sim.ZFAR - Sim.ZNEAR + Rnd(0, 1); d.A = Rnd(0, Sim.TAU); d.R = Rnd(0.12f, 0.97f);
                    dust[i] = d; z = Sim.ZP + d.S - s.Dist;
                }
                if (z > Sim.ZFAR) continue;
                float f = Fog(z);
                if (f < 0.02f) continue;
                float an = d.A + s.Roll, c = Mathf.Cos(an) * d.R, sn = Mathf.Sin(an) * d.R;
                Proj(s, z); var a0 = new Vector2(PX + c * PS, PY + sn * PS);
                Proj(s, z + streak);
                p.Line(a0, new Vector2(PX + c * PS, PY + sn * PS), dp * 1.2f, Painter.White(f * 0.5f));
            }

            // obstacles, far to near
            for (int i = s.Obs.Count - 1; i >= 0; i--)
            {
                var o = s.Obs[i];
                float dz = o.S - s.Dist;
                if (o.Wall) DrawWall(s, p, o, dz);
                else if (!o.Done) DrawOrb(s, p, o, dz);
            }

            // exhaust trail: points left behind in the world, streaming toward the camera
            foreach (var d in trail)
            {
                float z = Sim.ZP + d.S - s.Dist;
                if (z < Sim.ZNEAR) continue;
                Proj(s, z);
                float an = d.A + s.Roll, k = 1 - d.Life / d.Max;
                p.Disc(new Vector2(PX + Mathf.Cos(an) * d.R * PS, PY + Mathf.Sin(an) * d.R * PS), 0.012f * PS * k + dp, Painter.Hsl(h + 180, 100, 70, k * 0.55f));
            }

            if (s.Mode != Mode.Dead) DrawShip(s, p);

            foreach (var w in waves)
            {
                float k = 1 - w.Life / w.Max;
                p.Ring(w.P, w.R, w.W * dp * k + dp, Painter.Hsl(w.H, 100, 70, k));
            }
            foreach (var sp in sparks)
            {
                float k = 1 - sp.Life / sp.Max, sz = sp.Size * (0.4f + k);
                float c = Mathf.Cos(sp.Rot), sn = Mathf.Sin(sp.Rot);
                Vector2 fx = new Vector2(c, sn), fy = new Vector2(-sn, c);
                p.Triangle(sp.P + fx * sz * 2.2f, sp.P - fx * sz + fy * sz * 0.8f, sp.P - fx * sz - fy * sz * 0.8f, Painter.Hsl(sp.H, 100, 60 + k * 30, k));
            }

            // vignette and flash (normal blending)
            float diag = Mathf.Sqrt(W * W + H * H) * 0.6f;
            p.RadialBand(Vector2.zero, Mathf.Min(W, H) * 0.3f, diag, Painter.Black(0), Painter.Black(0.72f));
            if (s.Flash > 0) p.Rect(-W, -H, W, H, Painter.White(s.Flash * 0.75f, false));
        }

        void DrawWall(Sim s, Painter p, Obstacle o, float dz)
        {
            float z0 = Sim.ZP + dz, z1 = z0 + o.Th;
            if (z1 < Sim.ZNEAR || z0 > Sim.ZFAR) return;
            float za = Mathf.Max(z0, Sim.ZNEAR), f = Fog(za);
            if (f < 0.01f) return;
            float a0 = o.A + o.Rot * Mathf.Max(dz, 0) + s.Roll, a1 = a0 + o.W;
            float hot = 0;
            if (s.Mode == Mode.Play && dz > -o.Th && dz < 7 && Sim.ArcInfo(s.Theta, o.A, o.W) < 0) hot = 1 - Mathf.Max(dz, 0) / 7f;
            float h = s.Hue + o.Hv, sat = 100 - hot * 55, L = 58 + hot * 32;
            const float ri = Sim.R - Sim.WH;

            Proj(s, z1); var b = new Vector2(PX, PY); float bs = PS;
            var back = Painter.Hsl(h, sat, L, f * 0.45f);
            float lwb = Lw(z1);
            p.Arc(b, Sim.R * bs, a0, a1, lwb, back);
            p.Arc(b, ri * bs, a0, a1, lwb, back);

            Proj(s, za); var fr = new Vector2(PX, PY);
            float c0 = Mathf.Cos(a0), s0 = Mathf.Sin(a0), c1 = Mathf.Cos(a1), s1 = Mathf.Sin(a1);
            p.Line(b + new Vector2(c0, s0) * Sim.R * bs, fr + new Vector2(c0, s0) * Sim.R * PS, lwb, back);
            p.Line(b + new Vector2(c0, s0) * ri * bs, fr + new Vector2(c0, s0) * ri * PS, lwb, back);
            p.Line(b + new Vector2(c1, s1) * Sim.R * bs, fr + new Vector2(c1, s1) * Sim.R * PS, lwb, back);
            p.Line(b + new Vector2(c1, s1) * ri * bs, fr + new Vector2(c1, s1) * ri * PS, lwb, back);
            p.Line(b + new Vector2(c0, s0) * Sim.R * bs, b + new Vector2(c0, s0) * ri * bs, lwb, back);
            p.Line(b + new Vector2(c1, s1) * Sim.R * bs, b + new Vector2(c1, s1) * ri * bs, lwb, back);

            p.Sector(fr, ri * PS, Sim.R * PS, a0, a1, Painter.Hsl(h, sat, L - 12, f * (0.15f + hot * 0.3f)));
            var edge = Painter.Hsl(h, sat, L + 10, f);
            float lw = Lw(za) * 1.35f;
            p.Arc(fr, Sim.R * PS, a0, a1, lw, edge);
            p.Arc(fr, ri * PS, a0, a1, lw, edge);
            p.Line(fr + new Vector2(c0, s0) * Sim.R * PS, fr + new Vector2(c0, s0) * ri * PS, lw, edge);
            p.Line(fr + new Vector2(c1, s1) * Sim.R * PS, fr + new Vector2(c1, s1) * ri * PS, lw, edge);

            if (f > 0.25f)
            {
                int lanes = Mathf.RoundToInt(o.W / Sim.SEG);
                var col = Painter.Hsl(h, sat, L, f * 0.5f);
                for (int k = 1; k < lanes; k++)
                {
                    float a = a0 + k * Sim.SEG, c = Mathf.Cos(a), sn = Mathf.Sin(a);
                    p.Line(fr + new Vector2(c, sn) * Sim.R * PS, fr + new Vector2(c, sn) * ri * PS, Lw(za) * 0.6f, col);
                }
            }
        }

        void DrawOrb(Sim s, Painter p, Obstacle o, float dz)
        {
            float z = Sim.ZP + dz;
            if (z < Sim.ZNEAR || z > Sim.ZFAR) return;
            float f = Fog(z);
            Proj(s, z);
            float a = o.A + o.Rot * Mathf.Max(dz, 0) + s.Roll;
            var pos = new Vector2(PX + Mathf.Cos(a) * Sim.PR * PS, PY + Mathf.Sin(a) * Sim.PR * PS);
            float r = 0.045f * PS * (1 + 0.18f * Mathf.Sin(s.T * 9 + o.S)), h = s.Hue + 150;
            p.Glow(pos, r * 3f, Painter.Hsl(h, 100, 60, f * 0.6f));
            p.Disc(pos, r, Painter.Hsl(h, 100, 72, f));
            p.Disc(pos, r * 0.45f, Painter.White(f));
        }

        void DrawShip(Sim s, Painter p)
        {
            float sc = FF / Sim.ZP, a = s.Theta + s.Roll, c = Mathf.Cos(a), sn = Mathf.Sin(a);
            Vector2 x = ShipPos;
            float sz = 0.085f * sc;
            Vector2 t = new Vector2(-sn, c), n = new Vector2(-c, -sn); // tangent, toward centre
            float bank = Mathf.Clamp(s.Omega / 9f, -1, 1), ph = s.Hue + 180;
            p.Glow(x, sz * 2.6f, Painter.Hsl(ph, 100, 60, 0.45f));
            tmp[0] = x + n * sz * 1.3f - t * bank * sz * 0.3f;
            tmp[1] = x + t * sz * (1 + 0.25f * bank) - n * sz * 0.55f;
            tmp[2] = x - n * sz * 0.15f;
            tmp[3] = x - t * sz * (1 - 0.25f * bank) - n * sz * 0.55f;
            // concave chevron: two triangles around the notch
            p.Triangle(tmp[0], tmp[1], tmp[2], Painter.Hsl(ph, 100, 68, 0.95f));
            p.Triangle(tmp[0], tmp[2], tmp[3], Painter.Hsl(ph, 100, 68, 0.95f));
            p.PolyLine(tmp, 4, true, 2 * dp, Painter.White(1));
            float fl = 0.6f + Rnd(0, 0.4f);
            p.Disc(x - n * sz * 0.15f, sz * 0.22f * fl, Painter.White(fl));
        }
    }
}
