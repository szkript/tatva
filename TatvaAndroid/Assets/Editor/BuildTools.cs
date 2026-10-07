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
        PlayerSettings.bundleVersion = "1.0";
        PlayerSettings.Android.bundleVersionCode = 1;

        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        PlayerSettings.allowedAutorotateToPortrait = true;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.allowedAutorotateToLandscapeLeft = false;
        PlayerSettings.allowedAutorotateToLandscapeRight = false;

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
        if (errors.Count > 0)
        {
            foreach (var e in errors) Debug.LogError("[BuildTools] " + e);
            Fail("Self-test failed with " + errors.Count + " error(s).");
            return;
        }
        Debug.Log("[SelfTest] All runtime checks passed.");
    }

    /// <summary>Every wall group the generator emits must leave a passable gap; AI flies 25 minutes.</summary>
    static void TestLevels(List<string> errors)
    {
        var sim = new Sim(1234);
        sim.Reset(Mode.Title);
        var seen = new HashSet<Obstacle>();
        int walls = 0, aiHits = 0, groups = 0;
        float maxZone = 0;
        for (int f = 0; f < 60 * 60 * 25; f++)
        {
            sim.Update(1f / 60f, default);
            for (int i = 1; i < sim.Obs.Count; i++)
                if (sim.Obs[i].S < sim.Obs[i - 1].S - 1e-4f) { errors.Add($"obstacles out of order at frame {f}"); return; }
            foreach (var o in sim.Obs)
            {
                if (!o.Wall || seen.Contains(o)) continue;
                var grp = sim.Obs.Where(q => q.Wall && Mathf.Abs(q.S - o.S) < 1e-4f).ToList();
                foreach (var q in grp) seen.Add(q);
                groups++;
                bool open = false;
                for (int k = 0; k < 360 && !open; k++)
                {
                    float a = k * Sim.TAU / 360f;
                    if (grp.All(q => Sim.ArcInfo(a, q.A, q.W) >= 0.12f)) open = true;
                }
                if (!open) errors.Add($"closed wall group at s={o.S:0.0} (zone {Mathf.FloorToInt(o.S / Sim.D / 32)})");
            }
            foreach (var o in sim.Obs)
                if (o.Wall && o.Done && o.MinE < 8 && !float.IsNaN(o.MinE)) { walls++; if (o.MinE < -Sim.HB) aiHits++; o.MinE = 9; }
            maxZone = Mathf.Max(maxZone, sim.Zone);
        }
        if (maxZone < 8) errors.Add("demo run did not reach zone 9 (got " + (maxZone + 1) + ")");
        Debug.Log($"[SelfTest] levels: {groups} wall groups, all passable; zone reached {maxZone + 1}; AI pilot clipped {aiHits}/{walls} walls.");
    }

    static void TestPlay(List<string> errors)
    {
        var sim = new Sim(99);
        sim.Reset(Mode.Play);
        int steps = 0, grazes = 0, orbs = 0; bool died = false;
        sim.Step += _ => steps++;
        sim.Grazed += _ => grazes++;
        sim.OrbTaken += (_, __) => orbs++;
        sim.Died += () => died = true;
        int f = 0;
        for (; f < 60 * 60 * 5 && !died; f++)
            sim.Update(1f / 60f, new SteerCmd { HasTarget = true, Target = sim.AiTarget() + sim.Roll });
        if (steps < 100) errors.Add("music steps did not fire (" + steps + ")");
        if (sim.Score <= 0) errors.Add("score stayed at zero");
        Debug.Log($"[SelfTest] play: AI-steered run lasted {f / 60f:0.0}s, zone {sim.Zone + 1}, score {sim.Score}, grazes {grazes}, orbs {orbs}, died={died}.");

        // a player who never steers must crash into the first gate
        var idle = new Sim(5);
        bool idleDied = false;
        idle.Died += () => idleDied = true;
        idle.Reset(Mode.Play);
        for (int i = 0; i < 60 * 30 && !idleDied; i++) idle.Update(1f / 60f, default);
        if (!idleDied) errors.Add("an idle player never crashed: collisions are not working");
    }

    static void TestSynth(List<string> errors)
    {
        var s = new Synth(48000);
        for (int step = 0; step < 96; step++) Music.Step(s, step, 5, 140, (long)(step * 60f / 140f / 4f * 48000));
        Music.Graze(s, 24000); Music.Orb(s, 30000, 3); Music.Zone(s, 60000); Music.Death(s, 200000);
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

    static void TestRender(List<string> errors)
    {
        var sim = new Sim(42);
        sim.Reset(Mode.Title);
        var painter = new Painter();
        var view = new TunnelRenderer();
        view.Resize(1080, 2400, 2.6f);
        int maxVerts = 0;
        for (int f = 0; f < 60 * 120; f++)
        {
            sim.Update(1f / 60f, default);
            view.Tick(sim, 1f / 60f);
            if (f % 30 != 0) continue;
            view.Draw(sim, painter);
            painter.Upload();
            maxVerts = Mathf.Max(maxVerts, painter.VertexCount);
            var verts = painter.Mesh.vertices;
            if (verts.Any(v => float.IsNaN(v.x) || float.IsNaN(v.y))) { errors.Add("renderer produced NaN vertices at frame " + f); break; }
        }
        view.OnDeath(sim);
        view.Tick(sim, 0.1f);
        view.Draw(sim, painter);
        if (maxVerts > 400000) errors.Add("frame mesh too heavy: " + maxVerts + " vertices");
        UnityEngine.Object.DestroyImmediate(painter.Mesh);
        Debug.Log($"[SelfTest] render: 2 minutes of frames, max {maxVerts} vertices per frame.");
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
            hud.SetScore(98765); hud.SetMult(4, true); hud.SetZone(7);
            hud.Banner(3, Color.cyan);
            hud.Float("Hajszál! +200", new Vector2(100, -300), Color.white, 64);
            for (int i = 0; i < 120; i++) hud.Tick(1f / 60f, 200);
            var sim = new Sim(1); sim.Reset(Mode.Play);
            hud.ShowOver(sim, 12345, true);
            hud.ShowPause(true);
            var texts = root.GetComponentsInChildren<UnityEngine.UI.Text>(true).Select(t => t.text).ToList();
            if (!texts.Contains("TÁTVA")) errors.Add("title text missing");
            if (!texts.Any(t => t.Contains("98"))) errors.Add("score label did not update");
            Debug.Log($"[SelfTest] hud: {texts.Count} labels built.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            var es = UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>();
            if (es != null) UnityEngine.Object.DestroyImmediate(es.gameObject);
        }
    }

    /// <summary>Renders demo frames (with bloom) to Builds/snap_*.png for a visual check. Needs a graphics device.</summary>
    public static void Snapshot()
    {
        const int W = 1080, H = 2400;
        var sim = new Sim(3);
        sim.Reset(Mode.Title);
        var painter = new Painter();
        var view = new TunnelRenderer();
        view.Resize(W, H, 2.6f);
        var camGo = new GameObject("SnapCam", typeof(Camera));
        var meshGo = new GameObject("SnapMesh", typeof(MeshFilter), typeof(MeshRenderer));
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
        var bloomMat = new Material(Resources.Load<Shader>("Shaders/TatvaBloom"));
        try
        {
            var cam = camGo.GetComponent<Camera>();
            cam.orthographic = true; cam.orthographicSize = H / 2f; cam.clearFlags = CameraClearFlags.SolidColor;
            cam.transform.position = new Vector3(0, 0, -10); cam.nearClipPlane = 0.1f; cam.farClipPlane = 100;
            meshGo.GetComponent<MeshFilter>().sharedMesh = painter.Mesh;
            meshGo.GetComponent<MeshRenderer>().sharedMaterial = new Material(Resources.Load<Shader>("Shaders/TatvaPrim"));
            Directory.CreateDirectory("Builds");
            int[] shotsAt = { 60 * 8, 60 * 50, 60 * 75 };
            int shot = 0;
            for (int f = 0; f <= shotsAt[shotsAt.Length - 1]; f++)
            {
                sim.Update(1f / 60f, default);
                view.Tick(sim, 1f / 60f);
                if (f != shotsAt[shot]) continue;
                view.Draw(sim, painter);
                painter.Upload();
                cam.backgroundColor = Painter.Hsl(sim.Hue, 45, 3.5f, 1, false);
                var raw = RenderTexture.GetTemporary(W, H, 24);
                cam.targetTexture = raw;
                cam.Render();
                cam.targetTexture = null;
                // same chain as BloomEffect
                var half = RenderTexture.GetTemporary(W / 2, H / 2); var q1 = RenderTexture.GetTemporary(W / 4, H / 4); var q2 = RenderTexture.GetTemporary(W / 4, H / 4);
                var e1 = RenderTexture.GetTemporary(W / 8, H / 8); var e2 = RenderTexture.GetTemporary(W / 8, H / 8);
                Graphics.Blit(raw, half, bloomMat, 0); Graphics.Blit(half, q1, bloomMat, 0);
                bloomMat.SetFloat("_Spread", 1f); Graphics.Blit(q1, q2, bloomMat, 1); Graphics.Blit(q2, q1, bloomMat, 2);
                Graphics.Blit(q1, e1, bloomMat, 0);
                bloomMat.SetFloat("_Spread", 1.6f); Graphics.Blit(e1, e2, bloomMat, 1); Graphics.Blit(e2, e1, bloomMat, 2);
                bloomMat.SetTexture("_Bloom1", q1); bloomMat.SetTexture("_Bloom2", e1);
                bloomMat.SetFloat("_Int1", 0.9f); bloomMat.SetFloat("_Int2", 0.7f);
                Graphics.Blit(raw, rt, bloomMat, 3);
                foreach (var t in new[] { raw, half, q1, q2, e1, e2 }) RenderTexture.ReleaseTemporary(t);

                RenderTexture.active = rt;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes($"Builds/snap_{shot}_zone{sim.Zone + 1}.png", tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                shot++;
                if (shot >= shotsAt.Length) break;
            }
            Debug.Log("[BuildTools] Snapshots written to Builds/.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(camGo);
            UnityEngine.Object.DestroyImmediate(meshGo);
            UnityEngine.Object.DestroyImmediate(painter.Mesh);
            rt.Release();
        }
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
