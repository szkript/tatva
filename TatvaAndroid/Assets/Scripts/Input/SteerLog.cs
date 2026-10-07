using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Tatva
{
    /// <summary>
    /// Per-frame steering telemetry, one CSV per run under persistentDataPath/steerlogs
    /// (/sdcard/Android/data/com.kkodelab.tatva/files/steerlogs). Tools/steer_report.py pulls and analyses it.
    /// Lines starting with '#' are the header and events; everything else is one frame of play.
    /// </summary>
    public sealed class SteerLog
    {
        const int Keep = 40;                 // newest run files kept on the device
        static readonly CultureInfo IC = CultureInfo.InvariantCulture;

        readonly StringBuilder sb = new StringBuilder(1 << 18);
        string path;
        float t;
        int lastFlips;

        public static string Dir => Path.Combine(Application.persistentDataPath, "steerlogs");
        public bool Active => path != null;

        public void Begin(Steering s, Sim sim)
        {
            End();
            try
            {
                Directory.CreateDirectory(Dir);
                var old = Directory.GetFiles(Dir, "run_*.csv");
                Array.Sort(old, StringComparer.Ordinal);
                for (int i = 0; i < old.Length - (Keep - 1); i++) File.Delete(old[i]);
                path = Path.Combine(Dir, "run_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", IC) + ".csv");
            }
            catch (Exception e) { Debug.LogWarning("[SteerLog] " + e.Message); path = null; return; }

            t = 0; lastFlips = s.SignFlips; sb.Clear();
            sb.Append("# tatva steerlog 1; app ").Append(Application.version)
              .Append("; device ").Append(SystemInfo.deviceModel)
              .Append("; control ").Append(s.Mode)
              .Append("; gain ").Append(F(s.Gain))
              .Append("; noise ").Append(F(Steering.NoiseRate))
              .Append("; lead ").Append(F(Steering.LeadLimit))
              .Append("; maxrate ").Append(F(Sim.MaxRate))
              .Append("; gyroInterval ").Append(F(Steering.HasGyro ? Input.gyro.updateInterval : 0))
              .Append("; orientation ").Append(Screen.orientation)
              .Append('\n');
            sb.Append("t,dt,ts,dist,zone,rx,ry,rz,uz,gx,gy,gz,sign,tilt,rate,ship,target,theta,omega,lead,clamp,gap,wallT\n");
        }

        /// <summary>One frame of play, after sim.Update.</summary>
        public void Frame(float dt, Steering s, Sim sim, SteerCmd cmd)
        {
            if (path == null || sim.Mode != Mode.Play) return;
            t += dt;
            if (s.SignFlips != lastFlips) { lastFlips = s.SignFlips; Event("signflip", "sign=" + F(s.GyroSign)); }

            float wallT = -1, gap = 0;
            float sMin = float.PositiveInfinity;
            foreach (var o in sim.Obs) if (o.Wall && !o.Done && o.S - sim.Dist > -o.Th && o.S < sMin) sMin = o.S;
            if (!float.IsInfinity(sMin))
            {
                wallT = (sMin - sim.Dist) / Mathf.Max(0.01f, sim.Velocity * sim.Ts);
                gap = Sim.AngDiff(sim.AiTarget(), sim.Theta);
            }

            sb.Append(F(t)).Append(',').Append(F(dt)).Append(',').Append(F(sim.Ts)).Append(',').Append(F(sim.Dist)).Append(',').Append(sim.Zone).Append(',')
              .Append(F(s.LastRaw.x)).Append(',').Append(F(s.LastRaw.y)).Append(',').Append(F(s.LastRaw.z)).Append(',').Append(F(s.LastUnbiased.z)).Append(',')
              .Append(F(s.LastGravity.x)).Append(',').Append(F(s.LastGravity.y)).Append(',').Append(F(s.LastGravity.z)).Append(',')
              .Append(F(s.GyroSign)).Append(',').Append(F(Sim.AngDiff(s.Estimate, Sim.Bottom))).Append(',')
              .Append(F(s.LastRate)).Append(',').Append(F(s.LastShip)).Append(',')
              .Append(F(cmd.HasTunnelTarget ? cmd.Target : -1)).Append(',').Append(F(sim.Theta)).Append(',').Append(F(sim.Omega)).Append(',')
              .Append(F(cmd.HasTunnelTarget ? Sim.AngDiff(cmd.Target, sim.Theta) : 0)).Append(',').Append(s.LastClamped ? 1 : 0).Append(',')
              .Append(F(gap)).Append(',').Append(F(wallT)).Append('\n');
            if (sb.Length > 200000) Flush();
        }

        public void Event(string name, string detail = "")
        {
            if (path == null) return;
            sb.Append("#E,").Append(F(t)).Append(',').Append(name).Append(',').Append(detail).Append('\n');
        }

        /// <summary>Death: the ship angle and every wall alongside it (dz, arc start, arc width, signed distance; &lt;0 = inside).</summary>
        public void Death(Sim sim)
        {
            if (path == null) return;
            sb.Append("#D,").Append(F(t)).Append(",theta=").Append(F(sim.Theta)).Append(",omega=").Append(F(sim.Omega))
              .Append(",score=").Append(sim.Score).Append(",beats=").Append(sim.Beats).Append('\n');
            foreach (var o in sim.Obs)
            {
                if (!o.Wall || Mathf.Abs(o.S - sim.Dist) > o.Th + 0.5f) continue;
                sb.Append("#W,").Append(F(o.S - sim.Dist)).Append(',').Append(F(o.A)).Append(',').Append(F(o.W)).Append(',')
                  .Append(F(Sim.ArcInfo(sim.Theta, o.A, o.W))).Append('\n');
            }
            End();
        }

        public void End()
        {
            Flush();
            path = null;
        }

        public void Flush()
        {
            if (path == null || sb.Length == 0) return;
            try { File.AppendAllText(path, sb.ToString()); }
            catch (Exception e) { Debug.LogWarning("[SteerLog] " + e.Message); }
            sb.Clear();
        }

        static string F(float v) => v.ToString("0.####", IC);
    }
}
