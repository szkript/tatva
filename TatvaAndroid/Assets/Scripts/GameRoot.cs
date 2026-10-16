using UnityEngine;

namespace Tatva
{
    /// <summary>
    /// The only component in the scene. Builds the camera, meshes, audio and UI in code,
    /// runs the simulation and routes its events to sound, effects, haptics and the HUD.
    /// </summary>
    public sealed class GameRoot : MonoBehaviour
    {
        // v2.0 (SpeedX-style rework) scores differently, so it keeps its own record
        const string BestKey = "tatva.best2", ControlKey = "tatva.control", GainKey = "tatva.gain2", FineKey = "tatva.fine", ReturnKey = "tatva.ret";
        const float StepSize = 0.1f;

        Camera cam;
        Transform overlay;
        Painter painter;
        TrackRenderer view;
        SynthHost audioHost;
        Sim sim;
        Steering steering;
        Hud hud;
        readonly SteerLog log = new SteerLog();
        bool paused, music, overShown;
        int best, hudScore = -1, hudMult = -1, hudSpeed = -1, hudChips = -1;
        float lastCut = -1;

        void Awake()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            QualitySettings.antiAliasing = 2;
            ScaleRendering();
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            AllowRotation();
            Input.multiTouchEnabled = true; // touch mode boosts with two fingers

            best = PlayerPrefs.GetInt(BestKey, 0);
            steering = new Steering
            {
                Mode = (Control)Mathf.Clamp(PlayerPrefs.GetInt(ControlKey, Steering.HasGyro ? 0 : 2), 0, 2),
                Gain = PlayerPrefs.GetFloat(GainKey, 1.25f),
                Fine = PlayerPrefs.GetFloat(FineKey, Steering.DefaultFine),
                FreeReturn = PlayerPrefs.GetFloat(ReturnKey, Steering.DefaultFreeReturn),
            };
            if (!Steering.HasGyro) steering.Mode = Control.Touch;
            steering.Enable();

            // camera: perspective, placed and projected by TrackRenderer every frame
            var camGo = new GameObject("Camera", typeof(Camera), typeof(AudioListener));
            camGo.transform.SetParent(transform, false);
            cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.allowHDR = false; cam.allowMSAA = true;
            var bloom = camGo.AddComponent<BloomEffect>();
            bloom.Intensity1 = 0.55f; bloom.Intensity2 = 0.45f; // the lit floor would wash out at the old strength
            audioHost = camGo.AddComponent<SynthHost>();

            view = new TrackRenderer();
            var trackGo = new GameObject("Track", typeof(MeshFilter), typeof(MeshRenderer));
            trackGo.transform.SetParent(transform, false);
            trackGo.GetComponent<MeshFilter>().sharedMesh = view.Solid;
            var tr = trackGo.GetComponent<MeshRenderer>();
            tr.sharedMaterial = new Material(Resources.Load<Shader>("Shaders/TatvaSolid"));
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.receiveShadows = false;

            // 2D effects: a pixel-scaled plane glued in front of the camera
            painter = new Painter();
            var fxGo = new GameObject("Overlay", typeof(MeshFilter), typeof(MeshRenderer));
            fxGo.transform.SetParent(camGo.transform, false);
            overlay = fxGo.transform;
            fxGo.GetComponent<MeshFilter>().sharedMesh = painter.Mesh;
            var mr = fxGo.GetComponent<MeshRenderer>();
            mr.sharedMaterial = new Material(Resources.Load<Shader>("Shaders/TatvaPrim")) { renderQueue = 3100 };
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

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
            hud.SpeedStep += d => Tune(ref steering.Gain, d, 0.1f, 4f, GainKey);
            hud.FineStep += d => Tune(ref steering.Fine, d, 0f, 4f, FineKey);
            hud.ReturnStep += d => Tune(ref steering.FreeReturn, d, 0f, 2f, ReturnKey);
            hud.ShowTitle(best, steering.Mode, steering.Gain, Steering.HasGyro, steering.Fine, steering.FreeReturn);
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

        Color Hsl(float h, float l = 75) => Painter.Hsl(h, 100, l, 1, false);
        Vector2 FloatAt => view.ShipPos + new Vector2(0, 90);

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
                hud.Float($"Hajszál! +{Hud.Fmt(pts)}", FloatAt, Hsl(sim.Hue + 40), 64);
                Haptics.Pulse(18, 120);
                log.Event("graze", "pts=" + pts);
            };
            sim.ShieldTaken += () =>
            {
                view.OnShield();
                Music.Orb(audioHost.Synth, audioHost.At, 4);
                hud.Float("Pajzs", FloatAt, Hsl(190), 56);
                Haptics.Pulse(12, 90);
                log.Event("shield");
            };
            sim.ShieldBroke += o =>
            {
                view.OnShieldBreak(sim, o);
                Music.ShieldBreak(audioHost.Synth, audioHost.At);
                Haptics.Pulse(120, 220);
                log.Event("shieldbreak");
            };
            sim.Bombed += (pts, blasted) =>
            {
                view.OnBomb(sim, blasted);
                Music.Bomb(audioHost.Synth, audioHost.At);
                hud.Float($"Bumm! +{Hud.Fmt(pts)}", FloatAt, Hsl(0), 68);
                Haptics.Pulse(200, 255);
                log.Event("bomb", "n=" + blasted.Count);
            };
            sim.ZoneChanged += z =>
            {
                Music.Zone(audioHost.Synth, audioHost.At);
                audioHost.Synth.SetBpm(sim.BpmT);
                hud.Banner(z, Hsl(Sim.PAL[z % 8], 72));
                hud.SetZone(z, Sim.SHAPES[z % 8]);
                Haptics.Pulse(40, 160);
            };
            sim.DarkStarted += () => hud.Banner("V E S Z É L Y", "ÁRAMSZÜNET", Hsl(0, 65));
            sim.Died += () =>
            {
                log.Death(sim);
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
            log.Begin(steering, sim);
            paused = false; overShown = false; music = true;
            audioHost.Synth.SetMuted(false);
            hudScore = hudMult = hudSpeed = hudChips = -1;
            hud.ShowPlay(best);
            hud.Banner(0, Hsl(Sim.PAL[0], 72));
            sim.Flash = 0.4f;
            audioHost.Synth.SetBpm(sim.BpmT);
            Music.Zone(audioHost.Synth, audioHost.At);
        }

        void ToTitle()
        {
            paused = false; music = false; overShown = false;
            log.End();
            AllowRotation();
            audioHost.Synth.SetMuted(false);
            sim.Reset(Mode.Title);
            view.ResetDust(0);
            hud.ShowTitle(best, steering.Mode, steering.Gain, Steering.HasGyro, steering.Fine, steering.FreeReturn);
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
            log.Event(p ? "pause" : "resume");
            hud.ShowPause(p);
            audioHost.Synth.SetMuted(p);
        }

        void CycleControl()
        {
            if (!Steering.HasGyro) { steering.Mode = Control.Touch; }
            else steering.Mode = (Control)(((int)steering.Mode + 1) % 3);
            PlayerPrefs.SetInt(ControlKey, (int)steering.Mode);
            steering.Recenter();
            hud.SetControl(steering.Mode, steering.Gain, Steering.HasGyro, steering.Fine, steering.FreeReturn);
            Music.Click(audioHost.Synth, audioHost.At);
        }

        void Tune(ref float value, int dir, float min, float max, string key)
        {
            value = Mathf.Clamp(Mathf.Round((value + dir * StepSize) * 100f) / 100f, min, max);
            PlayerPrefs.SetFloat(key, value);
            PlayerPrefs.Save();
            hud.SetControl(steering.Mode, steering.Gain, Steering.HasGyro, steering.Fine, steering.FreeReturn);
            Music.Click(audioHost.Synth, audioHost.At);
        }

        void OnApplicationPause(bool p) { if (p) { SetPaused(true); log.Flush(); } }
        void OnApplicationFocus(bool f) { if (!f) SetPaused(true); }

        void Update()
        {
            float dtR = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            float w = Screen.width, h = Screen.height;
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

            var cmd = steering.Read(dtR, w, sim.Theta);
            if (!paused)
            {
                sim.Update(dtR, cmd);
                log.Frame(dtR, steering, sim, cmd);
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
                int spd = Mathf.RoundToInt(sim.V * Sim.KMH);
                if (spd != hudSpeed) { hudSpeed = spd; hud.SetSpeed(spd); }
                int chips = (sim.Shield ? 1 : 0) | (sim.Boosting ? 2 : 0);
                if (chips != hudChips) { hudChips = chips; hud.SetChips(sim.Shield, sim.Boosting); }
            }

            float cut = sim.Mode == Mode.Dead ? 420 : sim.Ts < 0.75f ? 900 : 20000;
            if (cut != lastCut) { lastCut = cut; audioHost.Synth.SetCutoff(cut); }

            // frame
            view.Draw(sim, painter);
            painter.Upload();
            view.ApplyCamera(cam, overlay);
            hud.Tick(dtR, sim.Hue);
        }
    }
}
