using UnityEngine;

namespace Tatva
{
    public enum Control { Gyro, GyroInverted, Touch }

    /// <summary>
    /// Turns the phone into a steering wheel. The ship slides toward the real-world "down",
    /// like a marble: rotate the phone clockwise and the ship rolls along the tunnel wall.
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
        public float Gain = 1.6f;

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

        public void Recenter() { init = false; hasTouch = false; snap = 0; }

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

        public SteerCmd Read(float dt, Vector2 screenCenter, float deadRadius)
        {
            float axis = 0;
            if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A)) axis -= 1;
            if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) axis += 1;
            if (axis != 0) return new SteerCmd { Axis = axis };

            if (Mode != Control.Touch && HasGyro)
            {
                float target = ReadGyro(dt);
                return new SteerCmd { HasTarget = true, Target = target };
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
                    if (signVotes < -12) { gyroSign = -gyroSign; signVotes = 0; }
                }
            }
            lastMeas = meas; haveLast = mag > 0.5f;

            // gravity correction, weighted by how much gravity lies in the screen plane
            float w = Mathf.Clamp01((mag - 0.25f) / 0.5f);
            est = Sim.Mod(est + Sim.AngDiff(meas, est) * Mathf.Min(1f, dt * 6f * w));

            float off = Sim.AngDiff(est, Sim.Bottom) * (Mode == Control.GyroInverted ? -1 : 1);
            return Sim.Bottom + Gain * off;
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
