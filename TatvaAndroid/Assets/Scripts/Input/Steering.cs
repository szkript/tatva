using UnityEngine;

namespace Tatva
{
    public enum Control { Gyro, GyroInverted, Touch }

    /// <summary>
    /// Turns the phone into a steering wheel driven by rotation speed: the ship moves only while
    /// the phone is turning, and a swifter turn moves it disproportionately further.
    ///
    /// Sensor fusion (complementary filter): the gyroscope's rotation rate around the screen
    /// normal is integrated every frame (fast, smooth, works with the phone lying flat), and the
    /// estimate is pulled toward the gravity direction in the screen plane whenever that
    /// direction is reliable (removes drift). The sign of the gyro axis is learned at runtime by
    /// comparing it with gravity, so device-specific axis conventions cannot invert the controls.
    /// </summary>
    public sealed class Steering
    {
        public Control Mode = Control.Gyro;
        public float Gain = 1.25f;

        float est = Sim.Bottom, lastMeas, gyroSign = 1, signVotes, snap;
        bool init, haveLast, hasTouch;
        float touchAngle;

        public static bool HasGyro => SystemInfo.supportsGyroscope;

        public void Enable()
        {
            if (!HasGyro) return;
            Input.gyro.enabled = true;
            Input.gyro.updateInterval = 1f / 60f;
        }

        public void Recenter() { init = false; hasTouch = false; snap = 0; mapper.Reset(); }

        /// <summary>
        /// The sensors report in the device's natural (portrait) axes; the game steers in screen axes.
        /// Rotation about the screen normal is the same in every orientation, only gravity's angle shifts.
        /// </summary>
        static float ScreenOffset()
        {
            var o = Screen.orientation;
            if (o == ScreenOrientation.AutoRotation)
            {
                var d = Input.deviceOrientation;
                o = d == DeviceOrientation.LandscapeLeft ? ScreenOrientation.LandscapeLeft
                  : d == DeviceOrientation.LandscapeRight ? ScreenOrientation.LandscapeRight
                  : Screen.width > Screen.height ? ScreenOrientation.LandscapeLeft : ScreenOrientation.Portrait;
            }
            switch (o)
            {
                case ScreenOrientation.LandscapeLeft: return Mathf.PI / 2f;
                case ScreenOrientation.LandscapeRight: return -Mathf.PI / 2f;
                case ScreenOrientation.PortraitUpsideDown: return Mathf.PI;
                default: return 0f;
            }
        }

        /// <summary>Current steering angle for UI feedback (title-screen indicator).</summary>
        public float Estimate => est;

        public SteerCmd Read(float dt, Vector2 screenCenter, float deadRadius, float theta)
        {
            float axis = 0;
            if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A)) axis -= 1;
            if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) axis += 1;
            if (axis != 0) return new SteerCmd { Axis = axis };

            if (Mode != Control.Touch && HasGyro)
            {
                float rate = ReadGyro(dt);
                return new SteerCmd { HasTunnelTarget = true, Target = mapper.Step(rate, Gain, dt, theta) };
            }

            Vector2? p = null;
            if (Input.touchCount > 0) p = Input.GetTouch(0).position;
            else if (Input.GetMouseButton(0)) p = Input.mousePosition;
            if (p.HasValue)
            {
                Vector2 d = p.Value - screenCenter;
                if (d.sqrMagnitude > deadRadius * deadRadius) { touchAngle = Mathf.Atan2(d.y, d.x); hasTouch = true; }
            }
            return hasTouch ? new SteerCmd { HasTarget = true, Target = touchAngle } : default;
        }

        // ---------- rotation-speed mapping (v1.6) ----------
        // Only the phone's rotation SPEED moves the ship, never its angle: stop rotating and the ship stops,
        // wherever the phone is. The response is superlinear like mouse acceleration: a swift turn throws the
        // ship far (dodging), a slow turn barely moves it, so the phone can be eased back to level without
        // losing the ship's position.

        /// <summary>Rotation speed treated as stillness (gyro noise, hand tremor), rad/s.</summary>
        public const float NoiseRate = 0.05f;
        /// <summary>How far the target may run ahead of the ship before the excess is dropped, radians.</summary>
        public const float LeadLimit = 1.6f;

        /// <summary>Ship angular speed (rad/s) for a phone rotation speed (rad/s, signed): k·gain·r², minus the noise floor.</summary>
        public static float RateToShip(float rate, float gain)
        {
            float a = Mathf.Max(0f, Mathf.Abs(rate) - NoiseRate);
            return Mathf.Sign(rate) * 3.5f * gain * a * a;
        }

        /// <summary>Turns phone rotation speed into a target angle in tunnel space. Plain C# so the self-test can drive it.</summary>
        public sealed class RotationMapper
        {
            float target;
            bool init;
            public void Reset() => init = false;

            public float Step(float rate, float gain, float dt, float theta)
            {
                if (!init) { target = theta; init = true; }
                target = Sim.Mod(target + RateToShip(rate, gain) * dt);
                float lead = Sim.AngDiff(target, theta);
                if (Mathf.Abs(lead) > LeadLimit) target = Sim.Mod(theta + Mathf.Sign(lead) * LeadLimit);
                return target;
            }
        }

        readonly RotationMapper mapper = new RotationMapper();

        /// <summary>
        /// Signed rotation speed of the phone about the screen normal, rad/s. The tilt estimate is kept up to date
        /// too, because learning the gyro axis sign compares the gyro against gravity.
        /// </summary>
        float ReadGyro(float dt)
        {
            Vector3 g = Input.gyro.gravity;
            float mag = new Vector2(g.x, g.y).magnitude;
            float meas = Sim.Mod(Mathf.Atan2(g.y, g.x) + ScreenOffset() + snap);
            float rate = -Input.gyro.rotationRateUnbiased.z * gyroSign;

            if (!init)
            {
                // At the start of a run the phone is held the way the player looks at it, so "down" should
                // be near the bottom of the screen. If it is a clean quarter turn off, this device reports
                // its axes differently: snap by that quarter turn instead of trusting the convention.
                if (mag > 0.6f)
                {
                    float d = Sim.AngDiff(Sim.Bottom, meas), k = Mathf.Round(d / (Mathf.PI / 2f));
                    if (k != 0 && Mathf.Abs(d - k * Mathf.PI / 2f) < 0.45f) { snap = k * Mathf.PI / 2f; meas = Sim.Mod(meas + snap); }
                }
                est = mag > 0.2f ? meas : Sim.Bottom;
                init = true;
            }
            est = Sim.Mod(est + rate * dt);

            // learn the gyro axis sign from gravity while the phone is upright enough
            if (mag > 0.5f && haveLast)
            {
                float dm = Sim.AngDiff(meas, lastMeas), dg = rate * dt;
                if (Mathf.Abs(dm) > 0.003f && Mathf.Abs(dg) > 0.003f)
                {
                    signVotes = Mathf.Clamp(signVotes + Mathf.Sign(dm * dg), -30, 30);
                    if (signVotes < -5) { gyroSign = -gyroSign; signVotes = 0; }
                }
            }
            lastMeas = meas; haveLast = mag > 0.5f;

            // gravity correction, weighted by how much gravity lies in the screen plane
            float w = Mathf.Clamp01((mag - 0.25f) / 0.5f);
            // gentle pull: the gyro carries fast motion, gravity (slower, filtered by the OS) only removes drift
            est = Sim.Mod(est + Sim.AngDiff(meas, est) * Mathf.Min(1f, dt * 2.5f * w));

            return rate * (Mode == Control.GyroInverted ? -1 : 1);
        }
    }

    /// <summary>Short vibrations on Android; no-ops elsewhere.</summary>
    public static class Haptics
    {
        public static bool Enabled = true;
#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject vibrator;
        static bool tried;
        static int sdk;

        static AndroidJavaObject Vib()
        {
            if (tried) return vibrator;
            tried = true;
            try
            {
                using (var version = new AndroidJavaClass("android.os.Build$VERSION")) sdk = version.GetStatic<int>("SDK_INT");
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
            }
            catch { vibrator = null; }
            return vibrator;
        }
#endif

        public static void Pulse(long ms, int amplitude = 255)
        {
            if (!Enabled) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            var v = Vib();
            if (v == null) { if (ms >= 200) Handheld.Vibrate(); return; }
            try
            {
                if (sdk >= 26)
                {
                    using (var fx = new AndroidJavaClass("android.os.VibrationEffect"))
                    using (var effect = fx.CallStatic<AndroidJavaObject>("createOneShot", ms, Mathf.Clamp(amplitude, 1, 255)))
                        v.Call("vibrate", effect);
                }
                else v.Call("vibrate", ms);
            }
            catch { /* vibration is optional */ }
#endif
        }
    }
}
