using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Tatva
{
    /// <summary>
    /// Draws the SpeedX-style track (port of draw() in index.html on the speedx branch).
    ///
    /// The world is rebuilt every frame relative to the ship: x = right, y = up from the track under the ship,
    /// z = distance ahead. The track's cross-section bends with Sim.K, so the floor under the ship is always "down"
    /// and steering rotates the world around it. Solid geometry (floor tiles, obstacles, the ship) goes into one
    /// mesh drawn by a perspective camera with a depth buffer and the Tatva/Solid edge shader. Effects (speed
    /// streaks, sparks, glows, vignette, flash) are 2D and go into the Painter overlay, a plane glued in front
    /// of the camera with 1 unit = 1 screen pixel, origin at the screen centre.
    /// </summary>
    public sealed class TrackRenderer
    {
        public const float CH = 1.0f, CB = 2.8f, PITCH = 0.07f; // camera height above the ship, distance behind it, downward tilt
        public const float TL = 4f, NEAR = 0.15f, FAR = Sim.VIEW + 30f;
        const float C = Sim.C, LW = Sim.LW, VIEW = Sim.VIEW;
        static readonly float[] Offs = { -C, 0, C };

        struct Spark { public Vector2 P, V; public float Life, Max, Size, H, Rot, Vr; }
        struct Wave { public Vector2 P; public float R, Vr, Life, Max, H, W; }
        struct Dot { public float U, Hh, S, Life, Max; }
        struct Dust { public float U, Hh, S; }

        readonly List<Spark> sparks = new List<Spark>(512);
        readonly List<Wave> waves = new List<Wave>(16);
        readonly List<Dot> trail = new List<Dot>(64);
        readonly Dust[] dust = new Dust[110];
        readonly System.Random rng = new System.Random(7);
        float Rnd(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        public readonly Mesh Solid;
        readonly List<Vector3> v = new List<Vector3>(1 << 15);
        readonly List<Color> col = new List<Color>(1 << 15);
        readonly List<Vector2> uv = new List<Vector2>(1 << 15), e = new List<Vector2>(1 << 15);
        readonly List<int> idx = new List<int>(1 << 16);
        readonly Vector2[] poly = new Vector2[8];

        float W = 1080, H = 1920, dp = 2, F = 1000, ppY, lim = C / 2, k, bx, by;
        Vector3 camPos;
        Quaternion rot = Quaternion.identity, inv = Quaternion.identity;
        Color bg;
        float SX, SY;

        public Vector2 ShipPos { get; private set; }
        public Color Background => bg;
        public int VertexCount => v.Count;

        public TrackRenderer()
        {
            Solid = new Mesh { name = "TatvaTrack", indexFormat = IndexFormat.UInt32 };
            Solid.MarkDynamic();
            ResetDust(0);
        }

        public void ResetDust(float dist)
        {
            for (int i = 0; i < dust.Length; i++) dust[i] = new Dust { U = Rnd(0, C), Hh = Rnd(0.4f, 2.8f), S = dist + Rnd(0, VIEW) };
            trail.Clear(); sparks.Clear(); waves.Clear();
        }

        public void Resize(float w, float h, float dpScale) { W = w; H = h; dp = dpScale; }

        // ---------- geometry ----------
        void Surf(float du, float hh)
        {
            if (Mathf.Abs(k) < 1e-4f) { SX = du; SY = hh; return; }
            float a = k * du, s = Mathf.Sin(a), c = Mathf.Cos(a);
            SX = s / k - hh * s; SY = (1 - c) / k + hh * c;
        }
        /// <summary>Track point at lateral offset du, height hh above the surface, ds ahead of the ship (with the bends).</summary>
        Vector3 Wp(float du, float hh, float ds)
        {
            Surf(du, hh);
            float z = Mathf.Max(ds + CB, 0), b = z * z;
            return new Vector3(SX + bx * b, SY + by * b, ds);
        }
        /// <summary>World point to overlay pixels (from the screen centre, y up). False when behind the near plane.</summary>
        bool Proj(Vector3 p, out Vector2 sp, out float z)
        {
            var l = inv * (p - camPos);
            z = l.z;
            if (z < NEAR) { sp = default; return false; }
            sp = new Vector2(F * l.x / z, F * l.y / z + ppY);
            return true;
        }
        static float Fog(float ds) => ds <= 30 ? 0 : Mathf.Min(1, Mathf.Pow((ds - 30) / (VIEW - 30), 1.2f));

        public void ApplyCamera(Camera cam, Transform overlay)
        {
            cam.transform.localPosition = camPos;
            cam.transform.localRotation = rot;
            cam.nearClipPlane = NEAR; cam.farClipPlane = FAR;
            float n = NEAR;
            cam.projectionMatrix = Matrix4x4.Frustum(-n * W / 2 / F, n * W / 2 / F, -n * (0.5f * H + ppY) / F, n * (0.5f * H - ppY) / F, n, FAR);
            cam.backgroundColor = bg;
            overlay.localPosition = new Vector3(0, -ppY / F, 1);
            overlay.localRotation = Quaternion.identity;
            overlay.localScale = Vector3.one / F;
        }

        // ---------- mesh building ----------
        int Vert(Vector3 p, Color c, float u, float w, float mode, float str)
        {
            v.Add(p); col.Add(c); uv.Add(new Vector2(u, w)); e.Add(new Vector2(mode, str));
            return v.Count - 1;
        }
        void Tri(int a, int b, int c) { idx.Add(a); idx.Add(b); idx.Add(c); }
        void Quad(int a, int b, int c, int d) { Tri(a, b, c); Tri(a, c, d); }
        Color Mix(Color c, float f) { var m = Color.Lerp(c, bg, f); m.a = 1 - f; return m; }

        void Floor(Sim s, float h, float dk, float bp)
        {
            int rows = Mathf.CeilToInt((VIEW + CB) / TL) + 1, baseIdx = Mathf.FloorToInt((s.Dist - CB) / TL);
            float line = (0.3f + 0.45f * bp) * (1 - 0.6f * dk), u0 = s.U;
            for (int r = 0; r <= rows; r++)
            {
                int ri = baseIdx + r;
                float sa = ri * TL - s.Dist, sb = Mathf.Min(sa + TL, VIEW);
                if (sb < -CB - 0.5f || sa > VIEW) continue;
                float fa = Fog(sa), fb = Fog(sb);
                for (int i = 0; i < Sim.NL; i++)
                {
                    int par = (i + ri) & 1;
                    float L0 = (par == 1 ? 22 : 12) * (1 - 0.85f * dk) + ((ri & 1) != 0 ? 2 : 0);
                    var baseC = Painter.Hsl(h, 70, L0, 1, false);
                    Color ca = Mix(baseC, fa), cb = Mix(baseC, fb);
                    float d0 = Sim.WrapU(i * LW - u0);
                    foreach (float off in Offs)
                    {
                        float a = Mathf.Max(d0 + off, -lim), b = Mathf.Min(d0 + off + LW, lim);
                        if (b - a < 0.01f) continue;
                        for (int q = 0; q < 2; q++)
                        {
                            float x0 = Mathf.Lerp(a, b, q / 2f), x1 = Mathf.Lerp(a, b, (q + 1) / 2f);
                            float t0 = (x0 - d0 - off) / LW, t1 = (x1 - d0 - off) / LW;
                            Quad(Vert(Wp(x0, 0, sa), ca, t0, 0.5f, 2, line), Vert(Wp(x1, 0, sa), ca, t1, 0.5f, 2, line),
                                 Vert(Wp(x1, 0, sb), cb, t1, 0.5f, 2, line), Vert(Wp(x0, 0, sb), cb, t0, 0.5f, 2, line));
                        }
                    }
                }
            }
        }

        /// <summary>A flat patch on the track (bomb tile, warning strip), split along its length so it follows the bends.</summary>
        void Patch(float du0, float du1, float ds0, float ds1, Color c, float mode)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt((ds1 - ds0) / 4));
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.Lerp(ds0, ds1, i / (float)n), b = Mathf.Lerp(ds0, ds1, (i + 1) / (float)n);
                float va = mode == 0 ? i / (float)n : 0.5f, vb = mode == 0 ? (i + 1) / (float)n : 0.5f;
                Color ca = Mix(c, Fog(a)), cb = Mix(c, Fog(b));
                Quad(Vert(Wp(du0, 0.04f, a), ca, 0, va, mode, 1), Vert(Wp(du1, 0.04f, a), ca, 1, va, mode, 1),
                     Vert(Wp(du1, 0.04f, b), cb, 1, vb, mode, 1), Vert(Wp(du0, 0.04f, b), cb, 0, vb, mode, 1));
            }
        }

        void ObjColors(Sim s, Obstacle o, float ds, float f, out Color top, out Color near, out Color side, out float edge)
        {
            float dk = s.DarkV, hl = Mathf.Exp(-Mathf.Max(ds, 0) / 22f);
            float m = 1 - dk + dk * hl * 0.45f, hh = s.Hue + 180 + o.Hv;
            top = Mix(Painter.Hsl(hh, 85, 62 * m, 1, false), f);
            near = Mix(Painter.Hsl(hh, 85, 47 * m, 1, false), f);
            side = Mix(Painter.Hsl(hh, 85, 33 * m, 1, false), f);
            edge = (1 - dk + dk * Mathf.Max(hl, 0.25f)) * (s.Inv > 0 ? 0.5f + 0.5f * Mathf.Sin(s.T * 40) : 1);
        }

        void Box(Sim s, Obstacle o, float duC, float ds, float hf, float f)
        {
            float hh = o.H * hf;
            if (hh < 0.03f) return;
            float z0 = ds - o.D / 2, z1 = ds + o.D / 2;
            ObjColors(s, o, ds, f, out var top, out var near, out var side, out float es);
            foreach (float off in Offs)
            {
                float a = duC + off - o.W / 2, b = duC + off + o.W / 2;
                float ca = Mathf.Max(a, -lim), cb = Mathf.Min(b, lim);
                if (cb - ca < 0.01f) continue;
                int n = Mathf.Max(1, Mathf.CeilToInt((cb - ca) / (LW * 0.5f)));
                if (a >= -lim)
                    Quad(Vert(Wp(a, 0, z1), side, 0, 0, 0, es), Vert(Wp(a, 0, z0), side, 1, 0, 0, es),
                         Vert(Wp(a, hh, z0), side, 1, 1, 0, es), Vert(Wp(a, hh, z1), side, 0, 1, 0, es));
                if (b <= lim)
                    Quad(Vert(Wp(b, 0, z0), side, 0, 0, 0, es), Vert(Wp(b, 0, z1), side, 1, 0, 0, es),
                         Vert(Wp(b, hh, z1), side, 1, 1, 0, es), Vert(Wp(b, hh, z0), side, 0, 1, 0, es));
                // top and front: strips that follow the curve, with edges only on the real outline
                for (int i = 0; i < n; i++)
                {
                    float x0 = Mathf.Lerp(ca, cb, i / (float)n), x1 = Mathf.Lerp(ca, cb, (i + 1) / (float)n);
                    float t0 = (x0 - a) / o.W, t1 = (x1 - a) / o.W;
                    Quad(Vert(Wp(x0, hh, z0), top, t0, 0, 0, es), Vert(Wp(x1, hh, z0), top, t1, 0, 0, es),
                         Vert(Wp(x1, hh, z1), top, t1, 1, 0, es), Vert(Wp(x0, hh, z1), top, t0, 1, 0, es));
                    Quad(Vert(Wp(x0, 0, z0), near, t0, 0, 0, es), Vert(Wp(x1, 0, z0), near, t1, 0, 0, es),
                         Vert(Wp(x1, hh, z0), near, t1, 1, 0, es), Vert(Wp(x0, hh, z0), near, t0, 1, 0, es));
                }
            }
        }

        void Pyr(Sim s, Obstacle o, float du, float ds, float f)
        {
            if (Mathf.Abs(du) > lim) return;
            float w2 = o.W / 2, z0 = ds - o.D / 2, z1 = ds + o.D / 2;
            ObjColors(s, o, ds, f, out _, out var near, out var side, out float es);
            var apex = Wp(du, o.H, ds);
            void Face(Vector3 p0, Vector3 p1, Color c) => Tri(Vert(p0, c, 1, 0, 1, es), Vert(p1, c, 0, 1, 1, es), Vert(apex, c, 0, 0, 1, es));
            Face(Wp(du - w2, 0, z1), Wp(du - w2, 0, z0), side);
            Face(Wp(du + w2, 0, z0), Wp(du + w2, 0, z1), side);
            Face(Wp(du - w2, 0, z0), Wp(du + w2, 0, z0), near);
        }

        static readonly Vector3[] ShipPts = { new Vector3(0, 0.13f, 0.62f), new Vector3(-0.3f, 0.07f, -0.22f), new Vector3(0.3f, 0.07f, -0.22f), new Vector3(0, 0.28f, -0.18f) };
        readonly Vector3[] ship = new Vector3[4];
        float bank;

        void Ship(Sim s)
        {
            bank = -Mathf.Clamp(s.Omega * Sim.R / s.MaxR, -1, 1) * 0.5f;
            float cb = Mathf.Cos(bank), sb = Mathf.Sin(bank);
            for (int i = 0; i < 4; i++)
            {
                var p = ShipPts[i];
                ship[i] = new Vector3(p.x * cb - (p.y - 0.12f) * sb, 0.12f + p.x * sb + (p.y - 0.12f) * cb, p.z);
            }
            if (s.Mode == Mode.Dead || (s.Inv > 0 && Mathf.Sin(s.T * 40) < 0)) return;
            float ph = s.Hue + 90;
            void Face(int a, int b, int c, Color cc) => Tri(Vert(ship[a], cc, 1, 0, 1, 0.9f), Vert(ship[b], cc, 0, 1, 1, 0.9f), Vert(ship[c], cc, 0, 0, 1, 0.9f));
            Face(1, 0, 2, Painter.Hsl(ph, 70, 30, 1, false));
            Face(1, 3, 2, Painter.Hsl(ph, 100, 80, 1, false));
            Face(1, 0, 3, Painter.Hsl(ph, 100, 60, 1, false));
            Face(3, 0, 2, Painter.Hsl(ph, 100, 48, 1, false));
        }

        // ---------- effects ----------
        public void Burst(Vector2 p, int n, float hue, float spd, float life, float size)
        {
            for (int i = 0; i < n; i++)
            {
                float a = Rnd(0, Sim.TAU), vel = Rnd(0.2f, 1) * spd * dp;
                sparks.Add(new Spark { P = p, V = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * vel, Max = Rnd(0.4f, 1) * life, Size = Rnd(0.5f, 1) * size * dp, H = hue, Rot = Rnd(0, Sim.TAU), Vr = Rnd(-12, 12) });
            }
        }
        void Ring(Vector2 p, float vr, float max, float hue, float w) => waves.Add(new Wave { P = p, R = 6 * dp, Vr = vr * dp, Max = max, H = hue, W = w });

        void Explode(Sim s, Obstacle o)
        {
            float du = Sim.WrapU(s.ObsU(o) - s.U), ds = o.S - s.Dist;
            if (Mathf.Abs(du) > lim + 0.5f) return;
            if (Proj(Wp(Mathf.Clamp(du, -lim, lim), o.H * 0.5f, ds), out var p, out _)) Burst(p, 14, s.Hue + 180 + o.Hv, 900, 0.7f, 3.5f);
        }

        public void OnGraze(Sim s) => Burst(ShipPos, 22, s.Hue, 700, 0.5f, 3);
        public void OnShield() => Ring(ShipPos, 900, 0.4f, 190, 4);
        public void OnShieldBreak(Sim s, Obstacle o)
        {
            Explode(s, o);
            Burst(ShipPos, 40, 190, 1200, 0.8f, 4);
            Ring(ShipPos, 1400, 0.5f, 190, 5);
        }
        public void OnBomb(Sim s, List<Obstacle> blasted)
        {
            for (int i = 0; i < blasted.Count && i < 24; i++) Explode(s, blasted[i]);
        }
        public void OnDeath(Sim s)
        {
            Burst(ShipPos, 160, s.Hue + 180, 2400, 2.2f, 5);
            Burst(ShipPos, 70, s.Hue, 1500, 2.6f, 7);
            for (int i = 0; i < 3; i++) waves.Add(new Wave { P = ShipPos, R = 4 * dp, Vr = (1600 + i * 900) * dp, Max = 1.4f + i * 0.3f, H = i == 1 ? s.Hue + 180 : s.Hue, W = 6 - i * 1.5f });
        }

        public void Tick(Sim s, float dtR)
        {
            float dt = dtR * s.Ts;
            if (s.Mode != Mode.Dead)
                for (int i = 0; i < 2; i++) trail.Add(new Dot { U = s.U + Rnd(-0.07f, 0.07f), Hh = 0.13f + Rnd(-0.03f, 0.03f), S = s.Dist - 0.3f - Rnd(0, 0.1f), Max = Rnd(0.2f, 0.35f) });
            for (int i = trail.Count - 1; i >= 0; i--) { var p = trail[i]; p.Life += dtR; if (p.Life > p.Max) trail.RemoveAt(i); else trail[i] = p; }
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
            k = s.K; bx = s.Bx; by = s.By;
            float h = s.Hue, dk = s.DarkV, bp = Mathf.Pow(1 - Mathf.Repeat(s.BeatPh / 4f, 1f), 4);
            float F0 = Mathf.Min(W * 0.62f, H * 0.95f);
            F = F0 * (1 + 0.05f * s.Punch + 0.09f * s.BoostA + 0.12f * s.Warp * s.Warp + (s.Mode == Mode.Play ? 0.01f * bp : 0));
            ppY = 0.08f * H;
            float roll = -0.1f * Mathf.Clamp(s.Omega * Sim.R / s.MaxR, -1, 1);
            float sk = s.Shake * s.Shake * 0.12f;
            camPos = new Vector3(Rnd(-sk, sk), CH + Rnd(-sk, sk), -CB);
            rot = Quaternion.Euler(PITCH * Mathf.Rad2Deg, 0, roll * Mathf.Rad2Deg);
            inv = Quaternion.Inverse(rot);
            lim = k < -1e-3f ? Mathf.Min(C / 2, (Mathf.PI / 2 - 0.08f) / -k) : C / 2;
            float bgl = 4 * (1 - 0.7f * dk);
            bg = Painter.Hsl(h, 50, bgl, 1, false);

            // ---- solid geometry ----
            v.Clear(); col.Clear(); uv.Clear(); e.Clear(); idx.Clear();
            Floor(s, h, dk, bp);
            foreach (var o in s.Obs)
            {
                if (o.Dead) continue;
                float ds = o.S - s.Dist;
                if (ds > VIEW || ds < -CB - 2) continue;
                if (o.K == Kind.Bomb)
                {
                    float du = Sim.WrapU(o.U - s.U);
                    if (Mathf.Abs(du) < lim) Patch(du - LW / 2 + 0.08f, du + LW / 2 - 0.08f, ds - 1.3f, ds + 1.3f, Painter.Hsl(0, 100, 50 + 12 * Mathf.Sin(s.T * 10), 1, false), 0);
                }
                else if (o.K == Kind.Box || o.K == Kind.Pyr)
                {
                    float du = Sim.WrapU(s.ObsU(o) - s.U), f = Fog(ds);
                    if (o.Rise && ds > -1 && Mathf.Abs(du) < lim)
                        Patch(du - 0.32f, du + 0.32f, Mathf.Max(ds - 34, -CB + 0.3f), ds - o.D / 2 - 0.2f, Painter.Hsl(25, 100, 42 + 8 * Mathf.Sin(s.T * 14), 1, false), 2);
                    if (o.K == Kind.Pyr) Pyr(s, o, du, ds, f);
                    else Box(s, o, du, ds, Sim.RiseF(o, ds), f);
                }
            }
            Ship(s);
            Solid.Clear();
            Solid.SetVertices(v); Solid.SetColors(col); Solid.SetUVs(0, uv); Solid.SetUVs(1, e);
            Solid.SetTriangles(idx, 0, false);
            Solid.bounds = new Bounds(Vector3.zero, Vector3.one * 1000);

            // ---- overlay ----
            p.Clear();
            Proj(new Vector3(0, 0.15f, 0), out var sp, out _);
            ShipPos = sp;

            // light at the end of the track
            if (Proj(Wp(0, Mathf.Max(k, 0) * Sim.R * Sim.R * 0.9f + 0.5f, VIEW), out var far, out _))
                p.Glow(far, F * 0.5f, Painter.Hsl(h, 100, 65, 0.4f * (1 - dk)));

            // speed streaks
            float streak = s.V * 0.03f * (1 + 3 * s.BoostA + 6 * s.Warp);
            for (int i = 0; i < dust.Length; i++)
            {
                ref var d = ref dust[i];
                float ds = d.S - s.Dist;
                if (ds < -CB + 0.5f) { d.S = s.Dist + Rnd(VIEW * 0.4f, VIEW); d.U = Rnd(0, C); d.Hh = Rnd(0.4f, k > 0.05f ? Sim.R * 0.95f : 3); ds = d.S - s.Dist; }
                float du = Sim.WrapU(d.U - s.U), f = Fog(ds);
                if (Mathf.Abs(du) > lim || f > 0.98f) continue;
                if (Proj(Wp(du, d.Hh, ds), out var a, out _) && Proj(Wp(du, d.Hh, ds + streak), out var b, out _))
                    p.Line(a, b, 1.2f * dp, Painter.White((1 - f) * 0.45f));
            }

            // shield pickups
            foreach (var o in s.Obs)
            {
                if (o.Dead || o.K != Kind.Shield) continue;
                float ds = o.S - s.Dist, du = Sim.WrapU(o.U - s.U);
                if (ds > VIEW || Mathf.Abs(du) > lim || !Proj(Wp(du, 0.65f + 0.12f * Mathf.Sin(s.T * 4 + o.S), ds), out var c, out float z)) continue;
                float r = 0.3f * F / z, a = 1 - Fog(ds), spn = Mathf.Abs(Mathf.Cos(s.T * 3)) * 0.7f + 0.3f;
                p.Glow(c, r * 2, Painter.Hsl(190, 100, 60, a * 0.35f));
                poly[0] = c + new Vector2(0, r * 1.3f); poly[1] = c + new Vector2(r * spn, 0); poly[2] = c - new Vector2(0, r * 1.3f); poly[3] = c - new Vector2(r * spn, 0);
                p.Polygon(poly, 4, Painter.Hsl(190, 100, 66, a, false));
                p.PolyLine(poly, 4, true, 2 * dp, Painter.White(a));
            }

            // exhaust trail
            foreach (var t in trail)
            {
                if (!Proj(Wp(Sim.WrapU(t.U - s.U), t.Hh, t.S - s.Dist), out var c, out float z)) continue;
                float kk = 1 - t.Life / t.Max;
                p.Disc(c, 0.012f * F / z * kk + dp, Painter.Hsl(h + 90, 100, 70, kk * 0.6f));
            }

            // engine glow and shield bubble
            if (s.Mode != Mode.Dead && Proj(new Vector3(0, 0.17f, -0.22f), out var eng, out float ez))
            {
                float fl = 0.7f + Rnd(0, 0.3f), r = (0.05f + 0.05f * s.BoostA) * F / ez * fl;
                p.Glow(eng, r * 2.4f, Painter.Hsl(h + 90, 100, 70, 0.6f));
                p.Disc(eng, r * 0.6f, Painter.White(0.9f));
                if (s.Shield && Proj(new Vector3(0, 0.18f, 0.1f), out var sc, out float sz))
                    p.Ring(sc, 0.62f * F / sz, 2.5f * dp, Painter.Hsl(190, 100, 65, 0.55f + 0.25f * Mathf.Sin(s.T * 6)));
            }

            foreach (var w in waves)
            {
                float kk = 1 - w.Life / w.Max;
                p.Ring(w.P, w.R, w.W * dp * kk + dp, Painter.Hsl(w.H, 100, 70, kk));
            }
            foreach (var q in sparks)
            {
                float kk = 1 - q.Life / q.Max, sz = q.Size * (0.4f + kk);
                Vector2 f = new Vector2(Mathf.Cos(q.Rot), Mathf.Sin(q.Rot)), n = new Vector2(-f.y, f.x);
                p.Triangle(q.P + f * sz * 2.2f, q.P - f * sz + n * sz * 0.8f, q.P - f * sz - n * sz * 0.8f, Painter.Hsl(q.H, 100, 60 + kk * 30, kk));
            }

            // vignette, blackout and flash (normal blending)
            float diag = Mathf.Sqrt(W * W + H * H) * 0.6f;
            p.RadialBand(Vector2.zero, Mathf.Min(W, H) * 0.35f, diag, Painter.Black(0), Painter.Black(0.6f));
            if (dk > 0.01f)
            {
                var ctr = ShipPos + new Vector2(0, H * 0.12f);
                p.RadialBand(ctr, H * 0.1f, diag * 1.2f, Painter.Black(0), Painter.Black(0.75f * dk));
            }
            if (s.Flash > 0) p.Rect(-W, -H, W, H, Painter.White(s.Flash * 0.75f, false));
        }
    }
}
