"""Pull TÁTVA steering logs from the attached phone and analyse them.

  python Tools/steer_report.py            # adb pull, then report the newest run
  python Tools/steer_report.py --all      # report every pulled run, plus a total
  python Tools/steer_report.py FILE.csv   # report one file (no adb)

Logs land in ../logs/steerlogs (repo-root logs/, never committed). Stdlib only.
"""
import glob
import math
import os
import subprocess
import sys

PKG = "com.kkodelab.tatva"
DEVICE_DIR = f"/sdcard/Android/data/{PKG}/files/steerlogs"
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "..", "..", "logs", "steerlogs"))
DEG = 180 / math.pi
WINDOW = 1.2  # seconds before a death that the diagnosis looks at


def adb():
    p = os.path.join(os.environ.get("LOCALAPPDATA", ""), "Android", "Sdk", "platform-tools", "adb.exe")
    return p if os.path.exists(p) else "adb"


def pull():
    os.makedirs(OUT, exist_ok=True)
    r = subprocess.run([adb(), "shell", "ls", DEVICE_DIR], capture_output=True, text=True)
    names = [n.strip() for n in r.stdout.split() if n.strip().endswith(".csv")]
    if not names:
        print("no logs on the device (" + (r.stderr.strip() or "empty dir") + ")")
    for n in names:
        subprocess.run([adb(), "pull", f"{DEVICE_DIR}/{n}", os.path.join(OUT, n)], capture_output=True)
    print(f"pulled {len(names)} run(s) into {OUT}")


def parse(path):
    header, cols, frames, events, death, walls = "", None, [], [], None, []
    with open(path, encoding="utf-8") as f:
        for line in f:
            line = line.rstrip("\n")
            if not line:
                continue
            if line.startswith("# "):
                header = line[2:]
            elif line.startswith("#E,"):
                _, t, name, detail = (line.split(",", 3) + [""])[:4]
                events.append((float(t), name, detail))
            elif line.startswith("#D,"):
                parts = line.split(",")
                death = {"t": float(parts[1])}
                for kv in parts[2:]:
                    k, v = kv.split("=")
                    death[k] = float(v)
            elif line.startswith("#W,"):
                dz, a, w, e = map(float, line.split(",")[1:5])
                walls.append({"dz": dz, "a": a, "w": w, "e": e})
            elif cols is None:
                cols = line.split(",")
            else:
                frames.append(dict(zip(cols, map(float, line.split(",")))))
    return {"path": path, "header": header, "frames": frames, "events": events, "death": death, "walls": walls}


def median(xs):
    xs = sorted(xs)
    return xs[len(xs) // 2] if xs else float("nan")


def segments(frames, thr=0.15, min_len=0.08):
    """Contiguous stretches where the phone turns one way faster than thr rad/s."""
    segs, cur = [], None
    for i, f in enumerate(frames):
        r = f["rate"]
        s = 1 if r > thr else -1 if r < -thr else 0
        if cur and (s != cur["sign"]):
            cur["end"] = i
            if frames[i - 1]["t"] - frames[cur["start"]]["t"] >= min_len:
                segs.append(cur)
            cur = None
        if s and not cur:
            cur = {"sign": s, "start": i}
    return segs


def analyse(run, quiet=False):
    fr = run["frames"]
    out = {"name": os.path.basename(run["path"]), "deaths": [], "len": fr[-1]["t"] if fr else 0}
    if len(fr) < 10:
        if not quiet:
            print(f"\n== {out['name']}: too short ({len(fr)} frames)")
        return out
    p = (lambda *a: None) if quiet else print
    p(f"\n== {out['name']}  [{run['header']}]")
    dts = [f["dt"] for f in fr]
    p(f"length {fr[-1]['t']:.1f}s, {len(fr)} frames, median frame {median(dts) * 1000:.1f} ms, "
      f"frames >25ms: {sum(d > 0.025 for d in dts)}")

    # does the player's motion go into the axis we read? (z = rotation about the screen normal)
    turning = [f for f in fr if (f["rx"] ** 2 + f["ry"] ** 2 + f["rz"] ** 2) > 0.25]
    if turning:
        ez = sum(f["rz"] ** 2 for f in turning)
        ea = sum(f["rx"] ** 2 + f["ry"] ** 2 + f["rz"] ** 2 for f in turning)
        out["zshare"] = ez / ea
        p(f"axis: {100 * ez / ea:.0f}% of rotation energy is about the screen normal (steering axis); "
          f"the rest is pitch/yaw the game ignores")
    bias = median([f["rz"] - f["uz"] for f in fr])
    still = [abs(f["rate"]) for f in fr if abs(f["rate"]) < 0.15]
    p(f"gyro: bias {bias:+.3f} rad/s, still-hand noise median {median(still):.3f} rad/s "
      f"(noise floor {run['header'].split('noise ')[1].split(';')[0] if 'noise ' in run['header'] else '?'}), "
      f"sign flips {sum(1 for e in run['events'] if e[1] == 'signflip')}")

    # phone -> ship ratio by turn speed
    bins = [(0.05, 0.3), (0.3, 1), (1, 2), (2, 99)]
    row = []
    for lo, hi in bins:
        sel = [f for f in fr if lo <= abs(f["rate"]) < hi]
        ph = sum(abs(f["rate"]) * f["dt"] for f in sel)
        sh = sum(abs(f["ship"]) * f["dt"] for f in sel)
        tm = sum(f["dt"] for f in sel)
        row.append(f"{lo:g}-{hi if hi < 99 else '+'}: {tm:4.1f}s, x{sh / ph if ph else 0:.1f}")
    p("turn speed (rad/s): time spent, ship deg per phone deg ->  " + " | ".join(row))
    maxrate = float(run["header"].split("maxrate ")[1].split(";")[0]) if "maxrate " in run["header"] else 12
    sat = sum(f["dt"] for f in fr if abs(f["omega"]) > 0.95 * maxrate)
    clamp = sum(f["dt"] for f in fr if f["clamp"] > 0)
    p(f"limits: ship at max speed {sat:.1f}s, lead clamp (input thrown away) {clamp:.1f}s")

    # stops: after a turn ends, how long until the ship stops, and how far it keeps going
    segs = segments(fr)
    delays, drifts = [], []
    for s in segs:
        i = s["end"]
        t0, th0 = fr[i]["t"], fr[i]["theta"]
        for j in range(i, min(len(fr), i + 90)):
            if abs(fr[j]["rate"]) > 0.15:
                break
            if abs(fr[j]["omega"]) < 0.3:
                delays.append(fr[j]["t"] - t0)
                drifts.append(abs(ang(fr[j]["theta"] - th0)) * DEG)
                break
    if delays:
        p(f"stops: {len(delays)} turns ended; ship stopped after median {median(delays) * 1000:.0f} ms "
          f"(max {max(delays) * 1000:.0f}), coasting median {median(drifts):.0f} deg (max {max(drifts):.0f})")
    rev = sum(1 for a, b in zip(segs, segs[1:]) if a["sign"] != b["sign"]
              and fr[b["start"]]["t"] - fr[a["end"] - 1]["t"] < 0.35)
    p(f"turns: {len(segs)}, quick reversals (<0.35s, i.e. corrections) {rev}")

    d = run["death"]
    if d:
        out["deaths"].append(diagnose(run, d, maxrate, p))
    else:
        p("no death recorded (run left via menu or still running)")
    return out


def ang(x):
    x = (x + math.pi) % (2 * math.pi) - math.pi
    return x


def diagnose(run, d, maxrate, p):
    fr = [f for f in run["frames"] if f["t"] >= d["t"] - WINDOW]
    if not fr:
        return "unknown"
    gap0 = fr[0]["gap"]
    phone = sum(f["rate"] * f["dt"] for f in fr)
    ship = sum(ang(b["theta"] - a["theta"]) for a, b in zip(fr, fr[1:]))
    peak = max(abs(f["rate"]) for f in fr)
    crossed = any(a["gap"] * b["gap"] < 0 and abs(a["gap"]) > 0.05 for a, b in zip(fr, fr[1:]))
    sat = sum(f["dt"] for f in fr if abs(f["omega"]) > 0.95 * maxrate or f["clamp"] > 0)
    first = next((f["t"] for f in fr if abs(f["rate"]) > 0.3), None)
    react = (d["t"] - first) if first is not None else 0

    if peak < 0.3 and abs(phone) < 0.14:
        cause = "NO INPUT: the phone barely turned"
    elif gap0 * ship < 0 and abs(fr[-1]["gap"]) > abs(gap0):
        cause = "WRONG WAY: the ship moved away from the nearest gap (direction mapping or wrong gap chosen)"
    elif crossed:
        cause = "OVERSHOOT: the ship passed through the gap and came out the other side"
    elif sat > 0.15:
        cause = "TOO SLOW (limit): the ship was at max speed or the input was clipped"
    elif react < 0.35:
        cause = f"LATE: the turn started only {react * 1000:.0f} ms before the hit"
    else:
        cause = "TOO SLOW (gain): moved toward the gap but not far enough"
    walls = ", ".join(f"[{w['a'] * DEG:.0f}+{w['w'] * DEG:.0f}deg e={w['e'] * DEG:+.0f}]" for w in run["walls"])
    p(f"DEATH at {d['t']:.1f}s: {cause}")
    p(f"  last {WINDOW}s: gap error {gap0 * DEG:+.0f} -> {fr[-1]['gap'] * DEG:+.0f} deg, phone turned {phone * DEG:+.0f} deg "
      f"(peak {peak:.1f} rad/s), ship moved {ship * DEG:+.0f} deg, at limit {sat:.2f}s; walls {walls}")
    p("  timeline  t-   rate  ship-cmd  omega   lead   gap  wall-in")
    step, nxt = 0.1, fr[0]["t"]
    for f in fr:
        if f["t"] >= nxt:
            nxt += step
            p(f"          {d['t'] - f['t']:4.2f} {f['rate']:+6.2f} {f['ship']:+8.2f} {f['omega']:+6.2f} "
              f"{f['lead'] * DEG:+6.0f} {f['gap'] * DEG:+5.0f} {f['wallT']:6.2f}{'  CLAMP' if f['clamp'] else ''}")
    return cause.split(":")[0]


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if args:
        files = args
    else:
        pull()
        files = sorted(glob.glob(os.path.join(OUT, "run_*.csv")))
        if not files:
            return
        if "--all" not in sys.argv:
            files = files[-1:]
    results = [analyse(parse(f)) for f in files]
    if len(results) > 1:
        causes = {}
        for r in results:
            for c in r["deaths"]:
                causes[c] = causes.get(c, 0) + 1
        print(f"\n== TOTAL: {len(results)} runs, median length {median([r['len'] for r in results]):.1f}s, deaths by cause: "
              + ", ".join(f"{k} {v}" for k, v in sorted(causes.items(), key=lambda kv: -kv[1])))


if __name__ == "__main__":
    main()
