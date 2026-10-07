using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Tatva
{
    /// <summary>All screens (title, HUD, game over, pause) built in code with uGUI. No prefabs.</summary>
    public sealed class Hud
    {
        public event Action Start, Again, Menu, Resume, Pause, ControlCycle, SensCycle;

        static readonly CultureInfo Hu = CultureInfo.GetCultureInfo("hu-HU");
        public static string Fmt(int n) => n.ToString("N0", Hu);

        static readonly Color Ink = new Color(0.957f, 0.941f, 1f), Mist = new Color(0.604f, 0.573f, 0.722f);
        static readonly Color Neon = new Color(1f, 0.239f, 0.604f);
        static readonly Color Gold = new Color(1f, 0.824f, 0.369f), Ice = new Color(0.247f, 0.941f, 1f), Void = new Color(0.024f, 0.016f, 0.047f);

        readonly Canvas canvas;
        readonly CanvasScaler scaler;
        // portrait layout is the one built below; landscape positions are registered with Dual()
        readonly List<(RectTransform rt, Vector2 aP, Vector2 pP, Vector2 sP, Vector2 aL, Vector2 pL, Vector2 sL)> placements =
            new List<(RectTransform, Vector2, Vector2, Vector2, Vector2, Vector2, Vector2)>();
        bool landscape;
        /// <summary>Follow the screen aspect in Tick (off for offscreen snapshots).</summary>
        public bool AutoOrient = true;
        public Canvas Canvas => canvas;
        readonly RectTransform safe;
        readonly Font font;
        readonly Sprite pill;
        readonly GameObject hudRoot, titleRoot, overRoot, pauseRoot;
        readonly Text score, mult, zoneNo, zoneName, best, title, tagline, controlLabel, sensLabel, hint, titleBest;
        readonly Text overZone, overTitle, overScore, overStats, bannerZone, bannerName;
        readonly Button controlBtn, sensBtn;
        readonly List<(Text t, float life, float max, Vector2 p)> floats = new List<(Text, float, float, Vector2)>();
        readonly Stack<Text> floatPool = new Stack<Text>();
        float bannerT = 99, multPop;
        Rect lastSafe;

        public Hud(Transform parent, Font font)
        {
            this.font = font;
            pill = MakePill();

            var go = new GameObject("UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(parent, false);
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;

            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                es.transform.SetParent(parent, false);
            }

            safe = Rt(new GameObject("Safe", typeof(RectTransform)), go.transform);
            Stretch(safe);
            ApplySafeArea();

            // ---- in-game HUD ----
            hudRoot = Group("Hud");
            zoneNo = Label(hudRoot, "Zóna 1", 28, Mist, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(40, -36), new Vector2(400, 40));
            zoneName = Label(hudRoot, "Ébredés", 40, Ink, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(40, -74), new Vector2(460, 56), FontStyle.Bold);
            score = Label(hudRoot, "0", 104, Ink, TextAnchor.UpperCenter, new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(700, 130), FontStyle.Bold);
            Glow(score, new Color(1f, 0.24f, 0.6f, 0.55f));
            mult = Label(hudRoot, "×1", 46, Gold, TextAnchor.UpperCenter, new Vector2(0.5f, 1), new Vector2(0, -150), new Vector2(300, 64), FontStyle.Bold);
            Label(hudRoot, "Rekord", 28, Mist, TextAnchor.UpperRight, new Vector2(1, 1), new Vector2(-40, -36), new Vector2(400, 40));
            best = Label(hudRoot, "0", 40, Ink, TextAnchor.UpperRight, new Vector2(1, 1), new Vector2(-40, -74), new Vector2(400, 56), FontStyle.Bold);
            var pauseBtn = MakeButton(hudRoot, "II", new Vector2(1, 1), new Vector2(-40 - 60, -170), new Vector2(120, 100), 40, false);
            pauseBtn.onClick.AddListener(() => Pause?.Invoke());

            // ---- zone banner ----
            var bannerGo = Group("Banner");
            bannerZone = Label(bannerGo, "", 34, Ink, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.72f), Vector2.zero, new Vector2(1000, 50));
            bannerName = Label(bannerGo, "", 110, Ink, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.72f), new Vector2(0, -90), new Vector2(1060, 140), FontStyle.Bold);
            Glow(bannerName, new Color(1, 1, 1, 0.35f));

            // ---- title ----
            titleRoot = Group("Title");
            Dim(titleRoot, 0.55f);
            title = Label(titleRoot, "TÁTVA", 250, Ink, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.72f), Vector2.zero, new Vector2(1080, 300), FontStyle.Bold);
            Glow(title, new Color(1f, 0.24f, 0.6f, 0.6f));
            Dual(title, new Vector2(0.3f, 0.62f), Vector2.zero);
            tagline = Label(titleRoot, "A L A G Ú T F U T A M   A   R I T M U S R A", 26, Ink, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.72f), new Vector2(0, -170), new Vector2(1000, 40));
            Dual(tagline, new Vector2(0.3f, 0.62f), new Vector2(0, -170), new Vector2(900, 40));
            var startBtn = MakeButton(titleRoot, "INDÍTÁS", new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(560, 150), 56, true);
            startBtn.onClick.AddListener(() => Start?.Invoke());
            Dual(startBtn, new Vector2(0.74f, 0.5f), new Vector2(0, 220));
            controlBtn = MakeButton(titleRoot, "", new Vector2(0.5f, 0.5f), new Vector2(0, -130), new Vector2(760, 110), 36, false);
            controlLabel = controlBtn.GetComponentInChildren<Text>();
            controlBtn.onClick.AddListener(() => ControlCycle?.Invoke());
            Dual(controlBtn, new Vector2(0.74f, 0.5f), new Vector2(0, 60));
            sensBtn = MakeButton(titleRoot, "", new Vector2(0.5f, 0.5f), new Vector2(0, -260), new Vector2(760, 110), 36, false);
            sensLabel = sensBtn.GetComponentInChildren<Text>();
            sensBtn.onClick.AddListener(() => SensCycle?.Invoke());
            Dual(sensBtn, new Vector2(0.74f, 0.5f), new Vector2(0, -60));
            hint = Label(titleRoot, "", 32, Mist, TextAnchor.UpperCenter, new Vector2(0.5f, 0.5f), new Vector2(0, -360), new Vector2(940, 260));
            Dual(hint, new Vector2(0.74f, 0.5f), new Vector2(0, -135), new Vector2(800, 300));
            titleBest = Label(titleRoot, "", 32, Gold, TextAnchor.MiddleCenter, new Vector2(0.5f, 0), new Vector2(0, 90), new Vector2(900, 50));
            Dual(titleBest, new Vector2(0.3f, 0.62f), new Vector2(0, -250));

            // ---- game over ----
            overRoot = Group("Over");
            Dim(overRoot, 0.6f);
            overZone = Label(overRoot, "", 30, Ice, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.68f), new Vector2(0, 80), new Vector2(1000, 50));
            Dual(overZone, new Vector2(0.32f, 0.5f), new Vector2(0, 250));
            overTitle = Label(overRoot, "", 120, Ink, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.68f), new Vector2(0, -40), new Vector2(1060, 160), FontStyle.Bold);
            Dual(overTitle, new Vector2(0.32f, 0.5f), new Vector2(0, 140));
            overScore = Label(overRoot, "", 130, Ink, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.68f), new Vector2(0, -210), new Vector2(1000, 170), FontStyle.Bold);
            Glow(overScore, new Color(1f, 0.24f, 0.6f, 0.55f));
            Dual(overScore, new Vector2(0.32f, 0.5f), new Vector2(0, -30));
            overStats = Label(overRoot, "", 34, Mist, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.68f), new Vector2(0, -350), new Vector2(1000, 110));
            Dual(overStats, new Vector2(0.32f, 0.5f), new Vector2(0, -190));
            var againBtn = MakeButton(overRoot, "ÚJRA", new Vector2(0.5f, 0.3f), new Vector2(0, 60), new Vector2(560, 150), 56, true);
            againBtn.onClick.AddListener(() => Again?.Invoke());
            Dual(againBtn, new Vector2(0.76f, 0.5f), new Vector2(0, 70));
            var menuBtn = MakeButton(overRoot, "MENÜ", new Vector2(0.5f, 0.3f), new Vector2(0, -110), new Vector2(400, 110), 36, false);
            menuBtn.onClick.AddListener(() => Menu?.Invoke());
            Dual(menuBtn, new Vector2(0.76f, 0.5f), new Vector2(0, -100));

            // ---- pause ----
            pauseRoot = Group("Pause");
            Dim(pauseRoot, 0.7f);
            Label(pauseRoot, "Szünet", 130, Ink, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.62f), Vector2.zero, new Vector2(1000, 170), FontStyle.Bold);
            var resumeBtn = MakeButton(pauseRoot, "TOVÁBB", new Vector2(0.5f, 0.45f), new Vector2(0, 0), new Vector2(560, 150), 56, true);
            resumeBtn.onClick.AddListener(() => Resume?.Invoke());
            var pMenuBtn = MakeButton(pauseRoot, "MENÜ", new Vector2(0.5f, 0.45f), new Vector2(0, -170), new Vector2(400, 110), 36, false);
            pMenuBtn.onClick.AddListener(() => Menu?.Invoke());

            ShowOnly(titleRoot);
        }

        // ---------- orientation ----------
        void Dual(Component c, Vector2 anchorL, Vector2 posL, Vector2? sizeL = null)
        {
            var rt = (RectTransform)c.transform;
            placements.Add((rt, rt.anchorMin, rt.anchoredPosition, rt.sizeDelta, anchorL, posL, sizeL ?? rt.sizeDelta));
        }

        public bool Landscape => landscape;

        public void SetLandscape(bool l)
        {
            landscape = l;
            scaler.referenceResolution = l ? new Vector2(1920, 1080) : new Vector2(1080, 1920);
            foreach (var p in placements)
            {
                p.rt.anchorMin = p.rt.anchorMax = l ? p.aL : p.aP;
                p.rt.anchoredPosition = l ? p.pL : p.pP;
                p.rt.sizeDelta = l ? p.sL : p.sP;
            }
        }

        // ---------- screens ----------
        void ShowOnly(GameObject g)
        {
            hudRoot.SetActive(g == hudRoot);
            titleRoot.SetActive(g == titleRoot);
            overRoot.SetActive(g == overRoot);
            pauseRoot.SetActive(g == pauseRoot);
        }

        public void ShowTitle(int bestScore, Control control, float gain, bool hasGyro)
        {
            ShowOnly(titleRoot);
            SetControl(control, gain, hasGyro);
            titleBest.text = bestScore > 0 ? $"Rekord: {Fmt(bestScore)}" : "";
            EventSystem.current?.SetSelectedGameObject(null);
        }

        public void SetControl(Control control, float gain, bool hasGyro)
        {
            controlLabel.text = control switch
            {
                Control.Gyro => "Irányítás: giroszkóp",
                Control.GyroInverted => "Irányítás: giroszkóp (fordított)",
                _ => "Irányítás: érintés",
            };
            sensLabel.text = $"Érzékenység: {gain.ToString("0.0#", Hu)}×";
            sensBtn.gameObject.SetActive(control != Control.Touch);
            hint.text = control == Control.Touch
                ? "Érintsd meg a kör bármely pontját, a hajó odafordul.\nSúrold a falakat a hajszál-bónuszért, gyűjtsd a fénygömböket."
                : hasGyro
                    ? "Döntsd a telefont, mint egy kormányt. Kis döntés:\nkis, pontos mozdulat. Nagyobb döntés: a hajó körbe\nfordul, annál gyorsabban, minél jobban döntöd."
                    : "Ezen az eszközön nincs giroszkóp, az érintéses irányítás működik.";
        }

        public void ShowPlay(int bestScore)
        {
            ShowOnly(hudRoot);
            best.text = Fmt(bestScore);
            SetScore(0); SetMult(1, false); SetZone(0);
        }

        public void ShowPause(bool on) => ShowOnly(on ? pauseRoot : hudRoot);

        public void ShowOver(Sim s, int bestScore, bool record)
        {
            ShowOnly(overRoot);
            overZone.text = $"ZÓNA {s.Zone + 1} · {Sim.ZNAMES[s.Zone % 8].ToUpper(Hu)}";
            overTitle.text = record ? "Új rekord!" : "Becsapódás";
            overTitle.color = record ? Gold : Ink;
            overScore.text = Fmt(s.Score);
            overStats.text = $"Hajszál  {Fmt(s.Grazes)}     Gömb  {Fmt(s.Orbs)}     Ütem  {Fmt(s.Beats)}\nRekord  {Fmt(bestScore)}";
        }

        public bool TitleVisible => titleRoot.activeSelf;

        // ---------- HUD values ----------
        public void SetScore(int v) => score.text = Fmt(v);
        public void SetMult(int v, bool pop) { mult.text = "×" + v; if (pop) multPop = 1; }
        public void SetZone(int z) { zoneNo.text = $"Zóna {z + 1}"; zoneName.text = Sim.ZNAMES[z % 8]; }

        public void Banner(int z, Color col)
        {
            bannerZone.text = $"Z Ó N A   {z + 1}";
            bannerName.text = Sim.ZNAMES[z % 8].ToUpper(Hu);
            bannerName.color = col;
            bannerT = 0;
        }

        /// <param name="screenPx">pixels from the screen centre, y up</param>
        public void Float(string txt, Vector2 screenPx, Color col, int size)
        {
            Text t = floatPool.Count > 0 ? floatPool.Pop() : Label(hudRoot, "", size, col, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(700, 90), FontStyle.Bold);
            t.gameObject.SetActive(true);
            t.text = txt; t.fontSize = size; t.color = col;
            // the HUD lives inside the safe-area rect; convert from screen-centre pixels to its local units
            var p = screenPx / canvas.scaleFactor;
            floats.Add((t, 0, 0.9f, p));
        }

        public void Tick(float dt, float hue)
        {
            if (Screen.safeArea != lastSafe) ApplySafeArea();
            if (AutoOrient && Screen.width > 0 && (Screen.width > Screen.height) != landscape) SetLandscape(Screen.width > Screen.height);

            // title colour cycles through the zone palette
            if (titleRoot.activeSelf)
            {
                title.color = Color.HSVToRGB(Mathf.Repeat(hue / 360f + Time.time * 0.05f, 1f), 0.55f, 1f);
                float k = 1 + 0.025f * Mathf.Sin(Time.time * 2.2f);
                title.rectTransform.localScale = new Vector3(k, k, 1);
            }

            // zone banner: punch in, hold, fade
            bannerT += dt;
            float bt = bannerT / 2.3f, ba = bt < 0.14f ? bt / 0.14f : bt < 0.72f ? 1 : Mathf.Max(0, 1 - (bt - 0.72f) / 0.28f);
            float bs = bt < 0.14f ? Mathf.Lerp(1.7f, 1f, bt / 0.14f) : Mathf.Lerp(1f, 0.94f, Mathf.Clamp01((bt - 0.72f) / 0.28f));
            SetAlpha(bannerZone, ba); SetAlpha(bannerName, ba);
            bannerName.rectTransform.localScale = new Vector3(bs, bs, 1);
            bannerZone.rectTransform.localScale = new Vector3(bs, bs, 1);

            multPop = Mathf.Max(0, multPop - dt * 3f);
            float ms = 1 + multPop * 0.9f;
            mult.rectTransform.localScale = new Vector3(ms, ms, 1);

            for (int i = floats.Count - 1; i >= 0; i--)
            {
                var f = floats[i];
                f.life += dt;
                if (f.life > f.max) { f.t.gameObject.SetActive(false); floatPool.Push(f.t); floats.RemoveAt(i); continue; }
                float k = 1 - f.life / f.max, sc = 1 + Mathf.Max(0, 0.25f - f.life) * 2;
                var rt = f.t.rectTransform;
                rt.anchoredPosition = f.p + new Vector2(0, f.life * 60f);
                rt.localScale = new Vector3(sc, sc, 1);
                SetAlpha(f.t, k);
                floats[i] = f;
            }
        }

        // ---------- builders ----------
        static RectTransform Rt(GameObject g, Transform parent)
        {
            var rt = g.GetComponent<RectTransform>() ?? g.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            return rt;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        void ApplySafeArea()
        {
            lastSafe = Screen.safeArea;
            if (Screen.width <= 0 || Screen.height <= 0) return;
            safe.anchorMin = new Vector2(lastSafe.xMin / Screen.width, lastSafe.yMin / Screen.height);
            safe.anchorMax = new Vector2(lastSafe.xMax / Screen.width, lastSafe.yMax / Screen.height);
        }

        GameObject Group(string name)
        {
            var g = new GameObject(name, typeof(RectTransform));
            Stretch(Rt(g, safe));
            return g;
        }

        void Dim(GameObject parent, float a)
        {
            var g = new GameObject("Dim", typeof(RectTransform), typeof(Image));
            var rt = Rt(g, parent.transform);
            // reach past the safe area to the real screen edges
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(-400, -400); rt.offsetMax = new Vector2(400, 400);
            var img = g.GetComponent<Image>();
            img.color = new Color(Void.r, Void.g, Void.b, a);
            img.raycastTarget = false;
        }

        Text Label(GameObject parent, string text, int size, Color col, TextAnchor align, Vector2 anchor, Vector2 pos, Vector2 box, FontStyle style = FontStyle.Normal)
        {
            var g = new GameObject("Text", typeof(RectTransform), typeof(Text));
            var rt = Rt(g, parent.transform);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(align == TextAnchor.UpperLeft ? 0 : align == TextAnchor.UpperRight ? 1 : 0.5f, align == TextAnchor.UpperLeft || align == TextAnchor.UpperRight || align == TextAnchor.UpperCenter ? 1 : 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = box;
            var t = g.GetComponent<Text>();
            t.font = font; t.text = text; t.fontSize = size; t.color = col; t.alignment = align; t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        static void Glow(Text t, Color c)
        {
            var o = t.gameObject.AddComponent<Outline>();
            o.effectColor = c; o.effectDistance = new Vector2(3, -3);
        }

        static void SetAlpha(Text t, float a) { var c = t.color; c.a = a; t.color = c; }

        Button MakeButton(GameObject parent, string label, Vector2 anchor, Vector2 pos, Vector2 size, int fontSize, bool primary)
        {
            var g = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = Rt(g, parent.transform);
            rt.anchorMin = rt.anchorMax = anchor; rt.anchoredPosition = pos; rt.sizeDelta = size;
            var img = g.GetComponent<Image>();
            img.sprite = pill; img.type = Image.Type.Sliced;
            img.color = primary ? Neon : new Color(1, 1, 1, 0.1f);
            var btn = g.GetComponent<Button>();
            var cb = btn.colors;
            cb.highlightedColor = Color.white; cb.pressedColor = new Color(0.75f, 0.75f, 0.8f); cb.selectedColor = Color.white; cb.fadeDuration = 0.05f;
            btn.colors = cb;
            var t = Label(g, label, fontSize, Ink, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, size, FontStyle.Bold);
            if (primary) { var o = g.AddComponent<Shadow>(); o.effectColor = new Color(Ice.r, Ice.g, Ice.b, 0.6f); o.effectDistance = new Vector2(0, -6); }
            t.raycastTarget = false;
            return btn;
        }

        static Sprite MakePill()
        {
            const int S = 64;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[S * S];
            float r = S / 2f;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                    byte a = (byte)(Mathf.Clamp01(r - d) * 255);
                    px[y * S + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(31, 31, 31, 31));
        }
    }
}
