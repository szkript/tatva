using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Tatva
{
    /// <summary>
    /// Immediate-mode 2D mesh builder: everything on screen is rebuilt into one mesh each frame
    /// and drawn in one call, in the order it was added (painter's order).
    /// Coordinates are screen pixels, origin at the screen centre, y up.
    /// Colours are premultiplied; alpha 0 means additive, alpha > 0 means normal blending
    /// (the material uses Blend One OneMinusSrcAlpha).
    /// UV encodes an edge feather: alpha *= saturate((1 - |uv.y|) * uv.x).
    /// </summary>
    public sealed class Painter
    {
        const float Solid = 1e4f;
        public readonly Mesh Mesh;
        readonly List<Vector3> v = new List<Vector3>(1 << 16);
        readonly List<Color> c = new List<Color>(1 << 16);
        readonly List<Vector2> uv = new List<Vector2>(1 << 16);
        readonly List<int> idx = new List<int>(1 << 17);

        public int VertexCount => v.Count;

        public Painter()
        {
            Mesh = new Mesh { name = "TatvaFrame", indexFormat = IndexFormat.UInt32 };
            Mesh.MarkDynamic();
        }

        public void Clear() { v.Clear(); c.Clear(); uv.Clear(); idx.Clear(); }

        public void Upload()
        {
            Mesh.Clear();
            Mesh.SetVertices(v);
            Mesh.SetColors(c);
            Mesh.SetUVs(0, uv);
            Mesh.SetTriangles(idx, 0, false);
            Mesh.bounds = new Bounds(Vector3.zero, new Vector3(1e6f, 1e6f, 10f));
        }

        // ---------- colour ----------
        public static Color Hsl(float h, float s, float l, float a, bool additive = true)
        {
            h = Mathf.Repeat(h, 360f) / 360f; s = Mathf.Clamp01(s / 100f); l = Mathf.Clamp01(l / 100f);
            float q = l < 0.5f ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
            float r = Hue(p, q, h + 1f / 3f), g = Hue(p, q, h), b = Hue(p, q, h - 1f / 3f);
            a = Mathf.Clamp01(a);
            return new Color(r * a, g * a, b * a, additive ? 0 : a);
        }
        static float Hue(float p, float q, float t)
        {
            if (t < 0) t += 1; if (t > 1) t -= 1;
            if (t < 1f / 6f) return p + (q - p) * 6 * t;
            if (t < 0.5f) return q;
            if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6;
            return p;
        }
        public static Color White(float a, bool additive = true) { a = Mathf.Clamp01(a); return new Color(a, a, a, additive ? 0 : a); }
        public static Color Black(float a) => new Color(0, 0, 0, Mathf.Clamp01(a));

        int Vert(Vector2 p, Color col, float k, float d)
        {
            v.Add(new Vector3(p.x, p.y, 0)); c.Add(col); uv.Add(new Vector2(k, d));
            return v.Count - 1;
        }
        void Tri(int a, int b, int d) { idx.Add(a); idx.Add(b); idx.Add(d); }

        // ---------- primitives ----------
        public void Line(Vector2 a, Vector2 b, float w, Color col)
        {
            Vector2 dir = b - a;
            float len = dir.magnitude;
            if (len < 1e-3f || col.maxColorComponent <= 0.001f && col.a <= 0.001f) return;
            float half = w * 0.5f + 1f;
            Vector2 n = new Vector2(-dir.y, dir.x) / len * half;
            int i0 = Vert(a + n, col, half, 1), i1 = Vert(a - n, col, half, -1);
            int i2 = Vert(b + n, col, half, 1), i3 = Vert(b - n, col, half, -1);
            Tri(i0, i2, i1); Tri(i1, i2, i3);
        }

        static int Segs(float r, float span) => Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(span) * Mathf.Max(r, 1f) / 10f), 3, 160);

        public void Arc(Vector2 ctr, float r, float a0, float a1, float w, Color col)
        {
            if (r <= 0.5f || col.maxColorComponent <= 0.001f && col.a <= 0.001f) return;
            float half = w * 0.5f + 1f, ro = r + half, ri = Mathf.Max(0, r - half);
            int n = Segs(r, a1 - a0);
            int start = v.Count;
            for (int i = 0; i <= n; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / (float)n), cs = Mathf.Cos(a), sn = Mathf.Sin(a);
                Vert(ctr + new Vector2(cs * ro, sn * ro), col, half, 1);
                Vert(ctr + new Vector2(cs * ri, sn * ri), col, half, -1);
            }
            for (int i = 0; i < n; i++) { int k = start + i * 2; Tri(k, k + 2, k + 1); Tri(k + 1, k + 2, k + 3); }
        }

        public void Ring(Vector2 ctr, float r, float w, Color col) => Arc(ctr, r, 0, Sim.TAU, w, col);

        /// <summary>Filled annular sector between radii r0 &lt; r1.</summary>
        public void Sector(Vector2 ctr, float r0, float r1, float a0, float a1, Color col)
        {
            if (r1 <= 0.5f || col.maxColorComponent <= 0.001f && col.a <= 0.001f) return;
            int n = Segs(r1, a1 - a0);
            int start = v.Count;
            for (int i = 0; i <= n; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / (float)n), cs = Mathf.Cos(a), sn = Mathf.Sin(a);
                Vert(ctr + new Vector2(cs * r1, sn * r1), col, Solid, 0);
                Vert(ctr + new Vector2(cs * r0, sn * r0), col, Solid, 0);
            }
            for (int i = 0; i < n; i++) { int k = start + i * 2; Tri(k, k + 2, k + 1); Tri(k + 1, k + 2, k + 3); }
        }

        /// <summary>Solid disc with a 1px soft rim.</summary>
        public void Disc(Vector2 ctr, float r, Color col) => Fan(ctr, r, col, col, Mathf.Max(r, 1f));
        /// <summary>Soft glow: alpha falls linearly from the centre to the rim.</summary>
        public void Glow(Vector2 ctr, float r, Color col) => Fan(ctr, r, col, col, 1f);

        void Fan(Vector2 ctr, float r, Color inner, Color outer, float k)
        {
            if (r <= 0.3f) return;
            int n = Mathf.Clamp(Mathf.CeilToInt(r * 0.6f), 10, 64);
            int center = Vert(ctr, inner, k, 0);
            for (int i = 0; i <= n; i++)
            {
                float a = i * Sim.TAU / n;
                Vert(ctr + new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r), outer, k, 1);
            }
            for (int i = 0; i < n; i++) Tri(center, center + 1 + i, center + 2 + i);
        }

        public void Triangle(Vector2 a, Vector2 b, Vector2 d, Color col)
        {
            int i0 = Vert(a, col, Solid, 0), i1 = Vert(b, col, Solid, 0), i2 = Vert(d, col, Solid, 0);
            Tri(i0, i1, i2);
        }

        public void Polygon(Vector2[] pts, int count, Color col)
        {
            int s = v.Count;
            for (int i = 0; i < count; i++) Vert(pts[i], col, Solid, 0);
            for (int i = 1; i < count - 1; i++) Tri(s, s + i, s + i + 1);
        }

        public void PolyLine(Vector2[] pts, int count, bool closed, float w, Color col)
        {
            for (int i = 0; i < count - 1; i++) Line(pts[i], pts[i + 1], w, col);
            if (closed) Line(pts[count - 1], pts[0], w, col);
        }

        public void Rect(float x0, float y0, float x1, float y1, Color col)
        {
            int a = Vert(new Vector2(x0, y0), col, Solid, 0), b = Vert(new Vector2(x1, y0), col, Solid, 0);
            int d = Vert(new Vector2(x1, y1), col, Solid, 0), e = Vert(new Vector2(x0, y1), col, Solid, 0);
            Tri(a, b, d); Tri(a, d, e);
        }

        /// <summary>Radial gradient band from r0 (colour a) to r1 (colour b).</summary>
        public void RadialBand(Vector2 ctr, float r0, float r1, Color a, Color b)
        {
            int n = 64, start = v.Count;
            for (int i = 0; i <= n; i++)
            {
                float t = i * Sim.TAU / n, cs = Mathf.Cos(t), sn = Mathf.Sin(t);
                Vert(ctr + new Vector2(cs * r1, sn * r1), b, Solid, 0);
                Vert(ctr + new Vector2(cs * r0, sn * r0), a, Solid, 0);
            }
            for (int i = 0; i < n; i++) { int k = start + i * 2; Tri(k, k + 2, k + 1); Tri(k + 1, k + 2, k + 3); }
        }
    }
}
