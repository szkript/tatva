# TÁTVA for Android

Unity 6 (6000.5.4f1) port of `../index.html`. Built-in render pipeline, legacy Input (for `Input.gyro`), uGUI. Everything (scene, UI, meshes, sound) is generated from code, so the project is built and tested without opening the Editor.

**v2.0: SpeedX-style rework** (port of the web version on the `speedx` branch). A chase camera flies over a checkerboard track whose cross-section morphs per zone: inside of a tube, flat plain, outside of a tube. Obstacles are blocks, scatter, pyramids, snake corridors, sliding blocks and rising columns. Blue diamonds give a shield, red tiles are bombs, and from zone 3 there are blackout stretches. Speed rises from about 230 to 830 km/h. Holding a finger on the screen boosts (x1.45 speed, x1.5 score). The lateral position is still an angle (one lap = 2 pi), so the gyro steering below carries over unchanged. The record is kept under `tatva.best2`. Rendering uses a perspective camera with a depth buffer and the `Tatva/Solid` edge shader (`TrackRenderer`); the 2D effects ride on the Painter overlay glued in front of the camera.

## Orientation

Landscape only (manifest `userLandscape`). The menus flip between the two landscape sides; a run locks the side it started on, so turning the phone to steer cannot rotate the screen. v1.1 allowed portrait and rotated at runtime, but on the owner's phone it stayed in portrait (without rotation lock); declaring landscape in the manifest (v1.2) fixed it.

## Controls

- **Gyroscope (default): rotation-speed steering** (`Steering.RotationMapper`, v1.6), from the owner's request "when I stop rotating the phone the ship should stop; the harder it rotates, the faster it should move; rotating back must be easy":
  - only the phone's rotation **speed** moves the ship, never its angle. Stop turning and the ship stops, wherever the phone is held;
  - the response is superlinear like mouse acceleration, with a linear part for fine turns (v2.1, owner: "a finom mozgásra kellene érzékenyebbnek lennie"): `RateToShip` = sensitivity x (0.8 r + 3.5 r^2) with r = rate - 0.03 rad/s. With the defaults (Gyors 1.25, Finom 1.5, picked by the owner in v2.1 playtests), a 30 degree phone turn moves the ship 73 degrees when slow (0.3 rad/s), 165 when normal (1 rad/s) and 191 on a flick (3 rad/s; capped by `LeadLimit`). A slow turn back keeps 66% of a dodge. (v1.6–2.0 were purely quadratic with a 0.05 floor: 27/117/190, 89%, too dull for fine moves);
  - the target may lead the ship by at most 1.6 rad (`LeadLimit`).
- **Sensor fusion:** the gyro rate around the screen normal is integrated and pulled gently (2.5/s) toward gravity's direction in the screen plane, so it also works with the phone lying flat. Gravity is rotated into screen axes per orientation, the gyro axis sign is learned from gravity, and the first reading of a run snaps by a quarter turn if a device reports its axes differently.
- **Rejected:** v1.5 hybrid (tilt up to 10 degrees = position, beyond = circling): holding a tilt kept the ship moving and rotating back was hard. v1.0 "marble" absolute steering (the ship went to real-world down, so you had to turn the phone all the way round). v1.3/v1.4 pure rate control (tilt = speed) was called too slow to react, *not* too sensitive; softening it was the wrong direction.
- The title screen toggles the control mode (gyroscope / gyroscope inverted / touch). Since v2.1 it has two −/+ steppers (step 0.1) so the owner can find the best feel and report the numbers: **Gyors** = the quadratic coefficient (`Steering.Gain`, default 1.25, `tatva.gain2`) and **Finom** = the linear coefficient for slow fine turns (`Steering.Fine`, default 1.5 since v2.2, `tatva.fine`). Formula: `RateToShip` = Finom·r + 3.5·Gyors·r². Both are written into each steerlog header.
- Gyro mode: a finger held anywhere on the screen (not on a button) = boost.
- Touch mode: hold the left or right side to turn (further from the centre = faster); two fingers = boost. In the Editor ←/→ steer and ↑/W/Shift boost.
- The Android back button pauses; on the title screen it quits.

Not yet confirmed on a device: how v1.6's rotation-speed steering feels in the hand.

**Steering telemetry (v1.7):** every run writes a per-frame CSV (`SteerLog`) to `/sdcard/Android/data/com.kkodelab.tatva/files/steerlogs/` (newest 40 kept). Each frame records raw gyro xyz, gravity, axis sign, rate used, ship command, target, ship angle and speed, lead clamp, the error to the nearest free gap (`gap`, from `Sim.AiTarget`) and the time to the next wall. Deaths record the obstacles alongside the ship (in v2.0 as track angles: centre, width, clearance). With the phone plugged in, run `python Tools/steer_report.py` (`--all` reports every run). It pulls the logs into `../logs/steerlogs` and reports the axis share, noise and bias, the ship/phone ratio by turn speed, stop delay and coasting, corrections, time at the speed limit, and a classified cause for each death (no input / wrong way / overshoot / too slow / late) with a timeline of the last 1.2 s.

**Performance:** the game renders at 75% of native resolution with 2x MSAA (`GameRoot.RenderScale`). At full resolution with 4x MSAA plus the bloom chain, a Galaxy A71 ran at ~41 fps; now it holds a steady 60 (measured with `dumpsys SurfaceFlinger --latency`).

## Releases

Every APK goes out as a GitHub release (`gh release create vX.Y`), and the owner installs from there. Bump `bundleVersion` and `bundleVersionCode` in `BuildTools.ConfigureAndroid` first; a versionCode that doesn't increase won't install over the old one. Check the built manifest with `aapt2 dump xmltree --file AndroidManifest.xml Builds/Tatva.apk` (in the Editor's `AndroidPlayer/SDK/build-tools/`).

## Build (Editor closed)

```sh
UNITY="/c/Program Files/Unity/Hub/Editor/6000.5.4f1/Editor/Unity.exe"
"$UNITY" -projectPath . -batchmode -quit -nographics -executeMethod BuildTools.SelfTest -logFile ../logs/selftest.log
"$UNITY" -projectPath . -batchmode -quit -executeMethod BuildTools.Snapshot -logFile ../logs/snap.log      # Builds/snap_*.png (needs a GPU, no -nographics)
"$UNITY" -projectPath . -batchmode -quit -nographics -buildTarget Android -executeMethod BuildTools.BuildAndroid -logFile ../logs/build.log
```

Don't trust the exit code. Check the log: `\): error ` must be 0, and the log must contain `[SelfTest] All runtime checks passed.` and `Build Finished, Result: Success`. The APK lands in `Builds/Tatva.apk` (debug-signed, for sideloading).

## Layout

- `Scripts/Core/Sim.cs`: rules, level generator, AI pilot (pure C#, self-tested)
- `Scripts/Render/`: `Painter` (one dynamic mesh per frame, feathered neon primitives), `TrackRenderer` (per-frame 3D track mesh + 2D effects), `BloomEffect` + `Resources/Shaders/`
- `Scripts/Audio/Synth.cs`: sample-level synth on the audio thread (`OnAudioFilterRead`); `Music` = song + SFX
- `Scripts/Input/Steering.cs`: gyro fusion, rotation-speed mapping (`RotationMapper`), touch, boost, haptics
- `Scripts/UI/Hud.cs`: all screens built in code
- `Editor/BuildTools.cs`: SetupScene, ConfigureAndroid (+ generated icon), SelfTest, Snapshot, BuildAndroid
