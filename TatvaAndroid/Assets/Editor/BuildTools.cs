using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tatva;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Batchmode entry points (the project is built without opening the Editor):
///   BuildTools.SetupScene, BuildTools.ConfigureAndroid, BuildTools.SelfTest, BuildTools.BuildAndroid
/// </summary>
public static class BuildTools
{
    const string ScenePath = "Assets/Scenes/Main.unity";
    const string IconPath = "Assets/Icon/icon.png";
    const string PackageName = "com.kkodelab.tatva";
    const string ApkPath = "Builds/Tatva.apk";

    [MenuItem("Tatva/Setup Scene")]
    public static void SetupScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("GameRoot", typeof(GameRoot));
        Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        Debug.Log("[BuildTools] Scene written: " + ScenePath);
    }

    [MenuItem("Tatva/Configure Android")]
    public static void ConfigureAndroid()
    {
        PlayerSettings.companyName = "kkodelab";
        PlayerSettings.productName = "Tátva";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, PackageName);
        PlayerSettings.bundleVersion = "2.0";
        PlayerSettings.Android.bundleVersionCode = 9;

        // landscape only (either side); GameRoot locks the side while a run is on
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;

        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.colorSpace = ColorSpace.Gamma;
        PlayerSettings.runInBackground = false;
        PlayerSettings.SplashScreen.show = false;
        PlayerSettings.SplashScreen.showUnityLogo = false;
        EditorUserBuildSettings.buildAppBundle = false; // installable APK

        MakeIcon();
        var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
        var kinds = new[] { IconKind.Application, IconKind.Any };
        foreach (var kind in kinds)
        {
            var sizes = PlayerSettings.GetIconSizes(NamedBuildTarget.Android, kind);
            if (sizes.Length > 0) PlayerSettings.SetIcons(NamedBuildTarget.Android, Enumerable.Repeat(icon, sizes.Length).ToArray(), kind);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[BuildTools] Android player settings applied.");
    }

    /// <summary>Neon tunnel icon drawn into a texture: rings, one wall sector, the ship.</summary>
    static void MakeIcon()
    {
        const int S = 512;
        var px = new Color[S * S];
        var c = new Vector2(S / 2f, S / 2f);
        Color Hsv(float h, float s, float v) => Color.HSVToRGB(Mathf.Repeat(h, 1f), s, v);
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f) - c;
                float r = p.magnitude / (S / 2f), a = Mathf.Atan2(p.y, p.x);
                Color col = new Color(0.04f, 0.015f, 0.06f) + Hsv(0.9f, 0.9f, 1f) * Mathf.Pow(Mathf.Max(0, 1 - r * 2.2f), 2) * 0.9f;
                // perspective rings
                for (int k = 0; k < 7; k++)
                {
                    float rr = 0.92f / (1 + k * 0.55f), d = Mathf.Abs(r - rr);
                    col += Hsv(0.92f - k * 0.03f, 0.8f, 1f) * Mathf.Clamp01(1 - d * 160f / (1 + k)) * (1f - k * 0.11f);
                }
                // a wall sector at the front with a gap at the bottom
                float wa = Sim.Mod(a - (-Mathf.PI / 2 + 0.9f));
                if (r > 0.62f && r < 0.86f && wa < Sim.TAU - 1.8f) col += Hsv(0.92f, 0.85f, 0.55f);
                Color o = col; o.a = 1; px[y * S + x] = o;
            }
        // ship chevron at the bottom gap
        var ship = new Vector2(c.x, c.y - S * 0.36f);
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                var p = new Vector2(x, y) - ship;
                float u = p.y / (S * 0.1f), w = Mathf.Abs(p.x) / (S * 0.1f);
                if (u > -0.5f && u < 1.3f && w < (1.3f - u) * 0.75f && !(u < 0.1f && w < (0.1f - u) * 0.9f))
                    px[y * S + x] = Color.Lerp(Hsv(0.42f, 0.7f, 1f), Color.white, 0.35f);
            }
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.SetPixels(px); tex.Apply();
        Directory.CreateDirectory(Path.GetDirectoryName(IconPath));
        File.WriteAllBytes(IconPath, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(IconPath, ImportAssetOptions.ForceUpdate);
    }

    // ---------- self-test ----------
    [MenuItem("Tatva/Self Test")]
    public static void SelfTest()
    {
        var errors = new List<string>();
        try { TestLevels(errors); } catch (Exception e) { errors.Add("levels threw: " + e); }
        try { TestPlay(errors); } catch (Exception e) { errors.Add("play threw: " + e); }
        try { TestSynth(errors); } catch (Exception e) { errors.Add("synth threw: " + e); }
        try { TestRender(errors); } catch (Exception e) { errors.Add("render threw: " + e); }
        try { TestHud(errors); } catch (Exception e) { errors.Add("hud threw: " + e); }
        try { TestTilt(errors); } catch (Exception e) { errors.Add("tilt threw: " + e); }
        if (errors.Count > 0)
        {
            foreach (var e in errors) Debug.LogError("[BuildTools] " + e);
            Fail("Self-test failed with " + errors.Count + " error(s).");
            return;
        }
        Debug.Log("[SelfTest] All runtime checks passed.");
    }

    static bool Solid(Obstacle o) => o.K == Kind.Box || o.K == Kind.Pyr;
    static SteerCmd Pilot(Sim s) => new SteerCmd { HasTunnelTarget = true, Target = s.AiTarget() };

    /// <summary>
    /// The generator must never close the track: 7 minutes of play through every zone (invulnerable, demo pilot
    /// flying), and at every half unit of track some lateral position clears all static obstacles there.
    /// </summary>
    static void TestLevels(List<string> errors)
    {
        var sim = new Sim(1234);
        sim.Reset(Mode.Play);
        sim.God = true;
        float checkedTo = 0;
        int slices = 0, closed = 0, solids = 0, clipped = 0, maxZone = 0;
        var seen = new HashSet<Obstacle>();
        var near = new List<Obstacle>();
        for (int f = 0; f < 60 * 60 * 7; f++)
        {
            sim.Update(1f / 60f, Pilot(sim));
            maxZone = Mathf.Max(maxZone, sim.Zone);
            foreach (var o in sim.Obs)
                if (Solid(o) && o.Passed && seen.Add(o)) { solids++; if (o.MinL < -0.05f) clipped++; }

            // check the stretch that is fully generated and still in the list
            float upTo = sim.NextS - 30;
            if (upTo - checkedTo < 20) continue;
            checkedTo = Mathf.Max(checkedTo, sim.Dist + 1);
            for (float s = checkedTo; s < upTo; s += 0.5f)
            {
                near.Clear();
                foreach (var o in sim.Obs)
                    if (Solid(o) && o.Amp == 0 && Mathf.Abs(o.S - s) <= o.D / 2) near.Add(o);
                slices++;
                bool open = false;
                for (int i = 0; i < 192 && !open; i++)
                {
                    float u = i * Sim.C / 192f;
                    open = near.All(o => Mathf.Abs(Sim.WrapU(o.U - u)) >= (o.K == Kind.Pyr ? o.W * 0.36f : o.W / 2) + Sim.PW / 2 + 0.15f);
                }
                if (!open && ++closed <= 5) errors.Add($"track closed at s={s:0.0} (zone {Mathf.FloorToInt(s / Sim.ZL) + 1}, {near.Count} obstacles)");
            }
            checkedTo = upTo;
        }
        if (maxZone < 8) errors.Add("the run did not reach zone 9 (got " + (maxZone + 1) + ")");
        Debug.Log($"[SelfTest] levels: {slices} track slices checked, {closed} closed; zone reached {maxZone + 1}, top speed {sim.MaxV * Sim.KMH:0} km/h; demo pilot clipped {clipped}/{solids} obstacles.");
    }

    static void TestPlay(List<string> errors)
    {
        var sim = new Sim(99);
        sim.Reset(Mode.Play);
        int steps = 0, grazes = 0; bool died = false;
        sim.Step += _ => steps++;
        sim.Grazed += _ => grazes++;
        sim.Died += () => died = true;
        int f = 0;
        for (; f < 60 * 60 * 5 && !died; f++) sim.Update(1f / 60f, Pilot(sim));
        if (steps < 100) errors.Add("music steps did not fire (" + steps + ")");
        if (sim.Score <= 0) errors.Add("score stayed at zero");
        Debug.Log($"[SelfTest] play: demo-piloted run lasted {f / 60f:0.0}s, zone {sim.Zone + 1}, {sim.Dist:0} m, score {sim.Score}, grazes {grazes}, died={died}.");

        // a player who never steers must crash
        var idle = new Sim(5);
        bool idleDied = false;
        idle.Died += () => idleDied = true;
        idle.Reset(Mode.Play);
        for (int i = 0; i < 60 * 60 && !idleDied; i++) idle.Update(1f / 60f, default);
        if (!idleDied) errors.Add("an idle player never crashed: collisions are not working");

        // pickups: a shield absorbs one hit, a bomb tile clears what is ahead
        var p = new Sim(6);
        p.Reset(Mode.Play);
        bool shieldTaken = false, broke = false, pDied = false; int blasted = 0;
        p.ShieldTaken += () => shieldTaken = true;
        p.ShieldBroke += _ => broke = true;
        p.Bombed += (_, list) => blasted = list.Count;
        p.Died += () => pDied = true;
        p.Obs.Add(new Obstacle { K = Kind.Shield, S = 8, U = p.U });
        p.Obs.Add(new Obstacle { K = Kind.Box, S = 20, U = p.U, W = 1, D = 1.4f, H = 1 });
        p.Obs.Add(new Obstacle { K = Kind.Bomb, S = 40, U = p.U });
        for (int i = 0; i < 6; i++) p.Obs.Add(new Obstacle { K = Kind.Box, S = 50 + i * 4, U = p.U, W = 1, D = 1.4f, H = 1 });
        for (int i = 0; i < 60 * 3 && !pDied; i++) p.Update(1f / 60f, default);
        if (!shieldTaken) errors.Add("shield pickup was not collected");
        if (!broke || pDied) errors.Add($"shield did not absorb the hit (broke={broke}, died={pDied})");
        if (blasted < 6) errors.Add($"bomb tile cleared {blasted} obstacles, expected at least 6");
    }

    static void TestSynth(List<string> errors)
    {
        var s = new Synth(48000);
        for (int step = 0; step < 96; step++) Music.Step(s, step, 5, 140, (long)(step * 60f / 140f / 4f * 48000));
        Music.Graze(s, 24000); Music.Orb(s, 30000, 3); Music.Zone(s, 60000); Music.Bomb(s, 90000); Music.ShieldBreak(s, 120000); Music.Death(s, 200000);
        var buf = new float[1024 * 2];
        float peak = 0; double energy = 0; int n = 0;
        for (int b = 0; b < 48000 * 7 / 1024; b++)
        {
            s.Render(buf, 2);
            foreach (var x in buf)
            {
                if (float.IsNaN(x) || float.IsInfinity(x)) { errors.Add("synth produced NaN/Inf"); return; }
                peak = Mathf.Max(peak, Mathf.Abs(x)); energy += x * x; n++;
            }
        }
        float rms = Mathf.Sqrt((float)(energy / n));
        if (peak < 0.05f) errors.Add("synth is silent (peak " + peak + ")");
        if (peak > 1.0001f) errors.Add("synth clips past 1.0 (peak " + peak + ")");
        Debug.Log($"[SelfTest] synth: 7s rendered, peak {peak:0.000}, rms {rms:0.000}.");
    }

    /// <summary>Every track shape renders without NaNs and within a sane vertex budget.</summary>
    static void TestRender(List<string> errors)
    {
        var painter = new Painter();
        var view = new TrackRenderer();
        view.Resize(2400, 1080, 2.6f);
        int maxVerts = 0, maxFx = 0;
        try
        {
            for (int z = 0; z < 8; z++)
            {
                var sim = new Sim(42 + z);
                sim.Reset(Mode.Play);
                sim.God = true;
                sim.SkipTo(z);
                for (int f = 0; f < 60 * 15; f++)
                {
                    sim.Update(1f / 60f, Pilot(sim));
                    view.Tick(sim, 1f / 60f);
                    if (f % 20 != 0) continue;
                    view.Draw(sim, painter);
                    painter.Upload();
                    maxVerts = Mathf.Max(maxVerts, view.VertexCount);
                    maxFx = Mathf.Max(maxFx, painter.VertexCount);
                    if (view.Solid.vertices.Any(v => float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z))) { errors.Add($"track mesh has NaN vertices (zone {z + 1})"); return; }
                    if (painter.Mesh.vertices.Any(v => float.IsNaN(v.x) || float.IsNaN(v.y))) { errors.Add($"overlay has NaN vertices (zone {z + 1})"); return; }
                }
                view.OnDeath(sim);
                view.Tick(sim, 0.1f);
                view.Draw(sim, painter);
            }
            if (maxVerts > 120000) errors.Add("track mesh too heavy: " + maxVerts + " vertices");
            Debug.Log($"[SelfTest] render: all 8 zones (tube, plain, outside), max {maxVerts} track and {maxFx} overlay vertices per frame.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(painter.Mesh);
            UnityEngine.Object.DestroyImmediate(view.Solid);
        }
    }

    static void TestHud(List<string> errors)
    {
        var root = new GameObject("HudTest");
        try
        {
            var hud = new Hud(root.transform, Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            hud.ShowTitle(12345, Control.Gyro, 1.6f, true);
            hud.SetControl(Control.Touch, 1.6f, false);
            hud.ShowPlay(12345);
            hud.SetScore(98765); hud.SetMult(4, true); hud.SetZone(7, -1); hud.SetSpeed(612); hud.SetChips(true, true);
            hud.Banner(3, Color.cyan);
            hud.Banner("V E S Z É L Y", "ÁRAMSZÜNET", Color.red);
            hud.Float("Hajszál! +200", new Vector2(100, -300), Color.white, 64);
            for (int i = 0; i < 120; i++) hud.Tick(1f / 60f, 200);
            var sim = new Sim(1); sim.Reset(Mode.Play);
            hud.ShowOver(sim, 12345, true);
            hud.ShowPause(true);
            var texts = root.GetComponentsInChildren<UnityEngine.UI.Text>(true).Select(t => t.text).ToList();
            if (!texts.Contains("TÁTVA")) errors.Add("title text missing");
            if (!texts.Any(t => t.Contains("98"))) errors.Add("score label did not update");
            if (!texts.Any(t => t.Contains("km/h"))) errors.Add("speed label missing");
            Debug.Log($"[SelfTest] hud: {texts.Count} labels built.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            var es = UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>();
            if (es != null) UnityEngine.Object.DestroyImmediate(es.gameObject);
        }
    }

    /// <summary>
    /// Renders frames (track, overlay, UI, bloom) to Builds/snap_*.png in landscape for a visual check:
    /// the title, each track shape, and the game-over screen. Needs a graphics device (no -nographics).
    /// </summary>
    public static void Snapshot()
    {
        Directory.CreateDirectory("Builds");
        Shot(2400, 1080, 0, 60 * 8, "title", "title");
        Shot(2400, 1080, 0, 60 * 9, "play", "tube");
        Shot(2400, 1080, 2, 60 * 9, "play", "plain");
        Shot(2400, 1080, 4, 60 * 9, "play", "outside");
        Shot(2400, 1080, 3, 60 * 4, "over", "over");
        Debug.Log("[BuildTools] Snapshots written to Builds/.");
    }

    static void Shot(int W, int H, int zone, int frames, string screen, string name)
    {
        var sim = new Sim(3);
        sim.Reset(screen == "title" ? Mode.Title : Mode.Play);
        sim.God = true;
        sim.SkipTo(zone);
        var painter = new Painter();
        var view = new TrackRenderer();
        view.Resize(W, H, 2.6f);
        var root = new GameObject("SnapRoot");
        var camGo = new GameObject("SnapCam", typeof(Camera));
        camGo.transform.SetParent(root.transform, false);
        var uiCamGo = new GameObject("SnapUiCam", typeof(Camera));
        uiCamGo.transform.SetParent(root.transform, false);
        var trackGo = new GameObject("SnapTrack", typeof(MeshFilter), typeof(MeshRenderer));
        trackGo.transform.SetParent(root.transform, false);
        var fxGo = new GameObject("SnapOverlay", typeof(MeshFilter), typeof(MeshRenderer));
        fxGo.transform.SetParent(camGo.transform, false);
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
        var bloomMat = new Material(Resources.Load<Shader>("Shaders/TatvaBloom"));
        const int UiLayer = 5;
        try
        {
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.cullingMask = ~(1 << UiLayer);
            trackGo.GetComponent<MeshFilter>().sharedMesh = view.Solid;
            trackGo.GetComponent<MeshRenderer>().sharedMaterial = new Material(Resources.Load<Shader>("Shaders/TatvaSolid"));
            fxGo.GetComponent<MeshFilter>().sharedMesh = painter.Mesh;
            fxGo.GetComponent<MeshRenderer>().sharedMaterial = new Material(Resources.Load<Shader>("Shaders/TatvaPrim")) { renderQueue = 3100 };

            for (int f = 0; f < frames; f++) { sim.Update(1f / 60f, screen == "title" ? default : Pilot(sim)); view.Tick(sim, 1f / 60f); }
            view.Draw(sim, painter);
            painter.Upload();
            view.ApplyCamera(cam, fxGo.transform);

            var raw = RenderTexture.GetTemporary(W, H, 24);
            cam.targetTexture = raw;
            cam.Render();
            cam.targetTexture = null;

            // UI through its own orthographic camera (the game camera has an off-centre custom projection)
            var uiCam = uiCamGo.GetComponent<Camera>();
            uiCam.orthographic = true; uiCam.clearFlags = CameraClearFlags.Nothing; uiCam.cullingMask = 1 << UiLayer;
            uiCam.transform.position = new Vector3(0, 0, -100); uiCam.nearClipPlane = 0.1f; uiCam.farClipPlane = 200;
            var hud = new Hud(root.transform, Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")) { AutoOrient = false };
            hud.SetLandscape(true);
            hud.Canvas.renderMode = RenderMode.ScreenSpaceCamera;
            hud.Canvas.worldCamera = uiCam;
            hud.Canvas.planeDistance = 50;
            foreach (var t in hud.Canvas.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = UiLayer;
            if (screen == "title") hud.ShowTitle(48210, Control.Gyro, 1.25f, true);
            else if (screen == "play")
            {
                hud.ShowPlay(48210); hud.SetScore(sim.Score); hud.SetMult(3, false); hud.SetZone(sim.Zone, sim.Shape);
                hud.SetSpeed(Mathf.RoundToInt(sim.V * Sim.KMH)); hud.SetChips(true, false);
            }
            else
            {
                var dead = new Sim(1); dead.Reset(Mode.Play); dead.Score = 31480; dead.Zone = 4; dead.Grazes = 37; dead.Dist = 5321; dead.MaxV = 61;
                hud.ShowOver(dead, 48210, false);
            }
            Canvas.ForceUpdateCanvases();
            uiCam.targetTexture = raw;
            uiCam.Render();
            uiCam.targetTexture = null;

            var half = RenderTexture.GetTemporary(W / 2, H / 2); var q1 = RenderTexture.GetTemporary(W / 4, H / 4); var q2 = RenderTexture.GetTemporary(W / 4, H / 4);
            var e1 = RenderTexture.GetTemporary(W / 8, H / 8); var e2 = RenderTexture.GetTemporary(W / 8, H / 8);
            Graphics.Blit(raw, half, bloomMat, 0); Graphics.Blit(half, q1, bloomMat, 0);
            bloomMat.SetFloat("_Spread", 1f); Graphics.Blit(q1, q2, bloomMat, 1); Graphics.Blit(q2, q1, bloomMat, 2);
            Graphics.Blit(q1, e1, bloomMat, 0);
            bloomMat.SetFloat("_Spread", 1.6f); Graphics.Blit(e1, e2, bloomMat, 1); Graphics.Blit(e2, e1, bloomMat, 2);
            bloomMat.SetTexture("_Bloom1", q1); bloomMat.SetTexture("_Bloom2", e1);
            bloomMat.SetFloat("_Int1", 0.55f); bloomMat.SetFloat("_Int2", 0.45f);
            Graphics.Blit(raw, rt, bloomMat, 3);
            foreach (var t in new[] { raw, half, q1, q2, e1, e2 }) RenderTexture.ReleaseTemporary(t);

            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes($"Builds/snap_{name}.png", tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            var es = UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>();
            if (es != null) UnityEngine.Object.DestroyImmediate(es.gameObject);
            UnityEngine.Object.DestroyImmediate(painter.Mesh);
            UnityEngine.Object.DestroyImmediate(view.Solid);
            rt.Release();
        }
    }

    /// <summary>
    /// Rotation-speed gyro steering, driven closed-loop through RotationMapper and Sim:
    /// the ship stops when the phone stops; the same phone turn moves the ship further when it is
    /// swifter; a slow turn back undoes only a little of a swift dodge; noise does nothing.
    /// </summary>
    static void TestTilt(List<string> errors)
    {
        const float G = 1.25f, dt = 1f / 60f;
        if (Steering.RateToShip(Steering.NoiseRate * 0.9f, G) != 0) errors.Add("gyro noise moves the ship");
        float prev = 0;
        for (int i = 1; i <= 100; i++)
        {
            float r = Steering.RateToShip(i * 0.05f, G);
            if (r < prev) errors.Add($"ship speed not monotonic at {i * 0.05f:0.00} rad/s");
            if (Mathf.Abs(r + Steering.RateToShip(-i * 0.05f, G)) > 1e-5f) errors.Add("rotation mapping not symmetric");
            prev = r;
        }

        Sim sim = null;
        var map = new Steering.RotationMapper();
        float travelled = 0;
        float Run(float rate, float seconds)
        {
            float start = travelled;
            for (int i = 0; i < Mathf.RoundToInt(seconds / dt); i++)
            {
                float before = sim.Theta;
                sim.Update(dt, new SteerCmd { HasTunnelTarget = true, Target = map.Step(rate, G, dt, sim.Theta) });
                travelled += Sim.AngDiff(sim.Theta, before);
            }
            return travelled - start;
        }
        void Fresh(int seed) { sim = new Sim(seed); sim.Reset(Mode.Play); sim.God = true; map.Reset(); travelled = 0; Run(0, 0.1f); }

        // the same 30 degree phone turn at three speeds, then let the ship settle
        float Turn(float rate) { Run(rate, 30f * Mathf.Deg2Rad / rate); return Run(0, 0.6f); }
        Fresh(8); Turn(0.3f); float slow = travelled;
        float still = Run(0, 0.5f);
        Fresh(9); Turn(1f); float medium = travelled;
        Fresh(10); Turn(3f); float fast = travelled;
        if (Mathf.Abs(still) > 0.005f) errors.Add($"ship keeps moving after the phone stopped ({still * Mathf.Rad2Deg:0.0} deg in 0.5s)");
        if (!(medium > slow * 2f && fast > medium * 1.5f && fast > 2.5f))
            errors.Add($"swifter turn does not move further: 30deg at 0.3/1/3 rad/s -> {slow * Mathf.Rad2Deg:0}/{medium * Mathf.Rad2Deg:0}/{fast * Mathf.Rad2Deg:0} deg");

        // swift dodge, then ease the phone back slowly: the ship should keep most of the dodge
        Fresh(11); Turn(3f); float dodge = travelled;
        Run(-0.25f, 30f * Mathf.Deg2Rad / 0.25f); Run(0, 0.6f);
        float kept = travelled / dodge;
        if (kept < 0.75f) errors.Add($"slow return undid too much of a dodge: kept {kept * 100:0}%");

        Fresh(12); Run(0.04f, 2f); Run(-0.04f, 2f);
        if (Mathf.Abs(travelled) > 0.001f) errors.Add($"steady slow drift moved the ship {travelled * Mathf.Rad2Deg:0.0} deg");

        Debug.Log($"[SelfTest] rotation steering (1.25x): 30deg phone turn at 0.3/1/3 rad/s -> {slow * Mathf.Rad2Deg:0}/{medium * Mathf.Rad2Deg:0}/{fast * Mathf.Rad2Deg:0} deg ship; " +
                  $"stops when the phone stops; slow return keeps {kept * 100:0}% of a dodge.");
    }

    // ---------- build ----------
    [MenuItem("Tatva/Build Android APK")]
    public static void BuildAndroid()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android &&
            !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
        { Fail("Could not switch to the Android build target."); return; }

        SetupScene();
        ConfigureAndroid();
        SelfTest();

        Directory.CreateDirectory(Path.GetDirectoryName(ApkPath));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = ApkPath,
            target = BuildTarget.Android,
            options = BuildOptions.None,
        });
        if (report.summary.result != BuildResult.Succeeded) { Fail("Build failed: " + report.summary.result); return; }
        Debug.Log($"[BuildTools] APK written: {ApkPath} ({report.summary.totalSize / (1024 * 1024)} MB)");
    }

    static void Fail(string msg)
    {
        Debug.LogError("[BuildTools] " + msg);
        if (Application.isBatchMode) EditorApplication.Exit(1);
    }
}
