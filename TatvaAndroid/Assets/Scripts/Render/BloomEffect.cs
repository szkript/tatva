using UnityEngine;

namespace Tatva
{
    /// <summary>Two-level neon bloom for the built-in pipeline (quarter and eighth resolution).</summary>
    [RequireComponent(typeof(Camera))]
    public sealed class BloomEffect : MonoBehaviour
    {
        public float Intensity1 = 0.9f, Intensity2 = 0.7f;
        Material mat;

        static readonly int Bloom1 = Shader.PropertyToID("_Bloom1"), Bloom2 = Shader.PropertyToID("_Bloom2");
        static readonly int Int1 = Shader.PropertyToID("_Int1"), Int2 = Shader.PropertyToID("_Int2"), Spread = Shader.PropertyToID("_Spread");

        void Awake()
        {
            var sh = Resources.Load<Shader>("Shaders/TatvaBloom");
            if (sh != null && sh.isSupported) mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            else enabled = false;
        }

        void OnDestroy() { if (mat != null) Destroy(mat); }

        void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (mat == null) { Graphics.Blit(src, dst); return; }
            int w = src.width, h = src.height;
            var half = RenderTexture.GetTemporary(w / 2, h / 2, 0, src.format);
            var q1 = RenderTexture.GetTemporary(w / 4, h / 4, 0, src.format);
            var q2 = RenderTexture.GetTemporary(w / 4, h / 4, 0, src.format);
            var e1 = RenderTexture.GetTemporary(w / 8, h / 8, 0, src.format);
            var e2 = RenderTexture.GetTemporary(w / 8, h / 8, 0, src.format);

            Graphics.Blit(src, half, mat, 0);
            Graphics.Blit(half, q1, mat, 0);
            mat.SetFloat(Spread, 1f);
            Graphics.Blit(q1, q2, mat, 1);
            Graphics.Blit(q2, q1, mat, 2);
            Graphics.Blit(q1, e1, mat, 0);
            mat.SetFloat(Spread, 1.6f);
            Graphics.Blit(e1, e2, mat, 1);
            Graphics.Blit(e2, e1, mat, 2);

            mat.SetTexture(Bloom1, q1);
            mat.SetTexture(Bloom2, e1);
            mat.SetFloat(Int1, Intensity1);
            mat.SetFloat(Int2, Intensity2);
            Graphics.Blit(src, dst, mat, 3);

            RenderTexture.ReleaseTemporary(half);
            RenderTexture.ReleaseTemporary(q1);
            RenderTexture.ReleaseTemporary(q2);
            RenderTexture.ReleaseTemporary(e1);
            RenderTexture.ReleaseTemporary(e2);
        }
    }
}
