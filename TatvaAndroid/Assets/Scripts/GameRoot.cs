using UnityEngine;

namespace Tatva
{
    /// <summary>
    /// The only component in the scene. Builds the camera, mesh, audio and UI in code,
    /// runs the simulation and routes its events to sound, effects, haptics and the HUD.
    /// </summary>
    public sealed class GameRoot : MonoBehaviour
    {
        const string BestKey = "tatva.best", ControlKey = "tatva.control", GainKey = "tatva.gain2"; // v1.4 recalibrated the scale; old saved values are ignored
        static readonly float[] Gains = { 0.85f, 1f, 1.25f, 1.6f, 2f };

        Camera cam;
        Painter painter;
        TunnelRenderer view;
        SynthHost audioHost;
        Sim sim;
        Steering steering;
        Hud hud;
        bool paused, music, overShown;
        int best, hudScore = -1, hudMult = -1;
        float lastCut = -1;

        void Awake()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            QualitySettings.antiAliasing = 2;
            ScaleRendering();
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            AllowRotation();
            Input.multiTouchEnabled = false;

            best = PlayerPrefs.GetInt(BestKey, 0);
            steering = new Steering
            {
                Mode = (Control)Mathf.Clamp(PlayerPrefs.GetInt(ControlKey, Steering.HasGyro ? 0 : 2), 0, 2),
                Gain = PlayerPrefs.GetFloat(GainKey, 1.25f),
            };
            if (!Steering.HasGyro) steering.Mode = Control.Touch;
            steering.Enable();

            // camera: orthographic, 1 unit = 1 pixel, origin at the screen centre
            var camGo = new GameObject("Camera", typeof(Camera), typeof(AudioListener));
            camGo.transform.SetParent(transform, false);
            camGo.transform.position = new Vector3(0, 0, -10);
            cam = camGo.GetComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 100f;
            cam.allowHDR = false; cam.allowMSAA = true;
            camGo.AddComponent<BloomEffect>();
            audioHost = camGo.AddComponent<SynthHost>();

            painter = new Painter();
            var meshGo = new GameObject("Tunnel", typeof(MeshFilter), typeof(MeshRenderer));
            meshGo.transform.SetParent(transform, false);
            meshGo.GetComponent<MeshFilter>().sharedMesh = painter.Mesh;
            var mr = meshGo.GetComponent<MeshRenderer>();
            mr.sharedMaterial = new Material(Resources.Load<Shader>("Shaders/TatvaPrim"));
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            view = new TunnelRenderer();
            sim = new Sim(System.Environment.TickCount);
            sim.Reset(Mode.Title);
            Wire();

            hud = new Hud(transform, Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            hud.Start += StartGame;
            hud.Again += () => { if (overShown) StartGame(); };
            hud.Menu += ToTitle;
            hud.Resume += () => SetPaused(false);
            hud.Pause += () => SetPaused(true);
            hud.ControlCycle += CycleControl;
            hud.SensCycle += CycleGain;
            hud.ShowTitle(best, steering.Mode, steering.Gain, Steering.HasGyro);
        }

        // Full-res MSAA + the bloom chain ran at ~41 fps on a Galaxy A71 (2400x1080); render below native and let the
        // compositor upscale. resScale keeps line widths (sized from the physical dpi) the same on screen.
        const float RenderScale = 0.75f;
        float resScale = 1f;

        void ScaleRendering()
        {
            if (!Application.isMobilePlatform) return;
            int nw = Display.main.systemWidth, nh = Display.main.systemHeight;
            if (nw <= 0 || nh <= 0) return;
            bool land = Screen.width > Screen.height;
            int longSide = Mathf.Max(nw, nh), shortSide = Mathf.Min(nw, nh);
            int w = Mathf.RoundToInt((land ? longSide : shortSide) * RenderScale);
            int h = Mathf.RoundToInt((land ? shortSide : longSide) * RenderScale);
            Screen.SetResolution(w, h, FullScreenMode.FullScreenWindow);
            resScale = RenderScale;
        }

        void Wire()
        {
            sim.Step += step =>
            {
                if (music) Music.Step(audioHost.Synth, step, sim.Zone, sim.Bpm, audioHost.At);
            };
            sim.Grazed += pts =>
            {
                view.OnGraze(sim);
                Music.Graze(audioHost.Synth, audioHost.At);
                hud.Float($"Hajszál! +{Hud.Fmt(pts)}", view.ShipPos * 0.82f, Painter.Hsl(sim.Hue + 40, 100, 75, 1, false), 64);
                Haptics.Pulse(18, 120);
            };
            sim.OrbTaken += (pts, streak) =>
            {
                view.OnOrb(sim);
                Music.Orb(audioHost.Synth, audioHost.At, streak - 1);
                hud.Float($"+{Hud.Fmt(pts)}", view.ShipPos * 0.82f, Painter.Hsl(sim.Hue + 150, 100, 75, 1, false), 48);
                Haptics.Pulse(8, 70);
            };
            sim.ZoneChanged += z =>
            {
                Music.Zone(audioHost.Synth, audioHost.At);
                audioHost.Synth.SetBpm(sim.BpmT);
                hud.Banner(z, Painter.Hsl(Sim.PAL[z % 8], 100, 72, 1, false));
                hud.SetZone(z);
                Haptics.Pulse(40, 160);
            };
            sim.Died += () =>
            {
                music = false;
                view.OnDeath(sim);
                Music.Death(audioHost.Synth, audioHost.At);
                Haptics.Pulse(380, 255);
            };
        }

        void StartGame()
        {
            sim.Reset(Mode.Play);
            view.ResetDust(0);
            LockRotation();
            steering.Recenter();
            paused = false; overShown = false; music = true;
            audioHost.Synth.SetMuted(false);
            hudScore = hudMult = -1;
            hud.ShowPlay(best);
            hud.Banner(0, Painter.Hsl(Sim.PAL[0], 100, 72, 1, false));
            sim.Flash = 0.4f;
            audioHost.Synth.SetBpm(sim.BpmT);
            Music.Zone(audioHost.Synth, audioHost.At);
        }

        void ToTitle()
        {
            paused = false; music = false; overShown = false;
            AllowRotation();
            audioHost.Synth.SetMuted(false);
            sim.Reset(Mode.Title);
            view.ResetDust(0);
            hud.ShowTitle(best, steering.Mode, steering.Gain, Steering.HasGyro);
        }

        // Landscape only. Menus follow the phone between the two landscape sides; a run locks the
        // side it started on, so steering by turning the phone cannot flip the screen mid-game.
        static void AllowRotation()
        {
            Screen.autorotateToPortrait = false;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.orientation = ScreenOrientation.AutoRotation;
        }

        static void LockRotation()
        {
            var o = Screen.orientation;
            if (o == ScreenOrientation.AutoRotation)
            {
                var d = Input.deviceOrientation;
                o = d == DeviceOrientation.LandscapeRight ? ScreenOrientation.LandscapeRight : ScreenOrientation.LandscapeLeft;
            }
            Screen.orientation = o;
        }

        void SetPaused(bool p)
        {
            if (sim.Mode != Mode.Play || paused == p) return;
            paused = p;
            hud.ShowPause(p);
            audioHost.Synth.SetMuted(p);
        }

        void CycleControl()
        {
            if (!Steering.HasGyro) { steering.Mode = Control.Touch; }
            else steering.Mode = (Control)(((int)steering.Mode + 1) % 3);
            PlayerPrefs.SetInt(ControlKey, (int)steering.Mode);
            steering.Recenter();
            hud.SetControl(steering.Mode, steering.Gain, Steering.HasGyro);
            Music.Click(audioHost.Synth, audioHost.At);
        }

        void CycleGain()
        {
            int i = System.Array.FindIndex(Gains, g => Mathf.Abs(g - steering.Gain) < 0.01f);
            steering.Gain = Gains[(i + 1) % Gains.Length];
            PlayerPrefs.SetFloat(GainKey, steering.Gain);
            hud.SetControl(steering.Mode, steering.Gain, Steering.HasGyro);
            Music.Click(audioHost.Synth, audioHost.At);
        }

        void OnApplicationPause(bool p) { if (p) SetPaused(true); }
        void OnApplicationFocus(bool f) { if (!f) SetPaused(true); }

        void Update()
        {
            float dtR = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            float w = Screen.width, h = Screen.height;
            cam.orthographicSize = h / 2f;
            float dp = Mathf.Clamp(Screen.dpi > 0 ? Screen.dpi / 160f * resScale : h / 800f, 1f, 4f);
            view.Resize(w, h, dp);

            // Android back button
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (sim.Mode == Mode.Play) SetPaused(!paused);
                else if (sim.Mode == Mode.Dead) ToTitle();
                else Application.Quit();
            }
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
            {
                if (hud.TitleVisible || overShown) StartGame(); else if (paused) SetPaused(false);
            }

            var cmd = steering.Read(dtR, new Vector2(w / 2f, h / 2f), Mathf.Min(w, h) * 0.05f, sim.Theta);
            if (!paused)
            {
                sim.Update(dtR, cmd);
                view.Tick(sim, dtR);
            }

            if (sim.Mode == Mode.Dead && !overShown && sim.DeadT > 1.1f)
            {
                overShown = true;
                bool record = sim.Score > best;
                if (record) { best = sim.Score; PlayerPrefs.SetInt(BestKey, best); PlayerPrefs.Save(); }
                hud.ShowOver(sim, best, record);
            }

            if (sim.Mode == Mode.Play)
            {
                if (sim.Score != hudScore) { hudScore = sim.Score; hud.SetScore(sim.Score); }
                if (sim.Mult != hudMult) { hud.SetMult(sim.Mult, sim.Mult > hudMult && hudMult > 0); hudMult = sim.Mult; }
            }

            float cut = sim.Mode == Mode.Dead ? 420 : sim.Ts < 0.75f ? 900 : 20000;
            if (cut != lastCut) { lastCut = cut; audioHost.Synth.SetCutoff(cut); }

            // frame
            cam.backgroundColor = Painter.Hsl(sim.Hue, 45, 3.5f, 1, false);
            float sk = sim.Shake * sim.Shake * 30 * dp;
            cam.transform.position = new Vector3(Random.Range(-sk, sk), Random.Range(-sk, sk), -10);
            view.Draw(sim, painter);
            painter.Upload();
            hud.Tick(dtR, sim.Hue);
        }
    }
}
