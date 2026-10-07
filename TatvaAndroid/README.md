# TÁTVA for Android

Unity 6 (6000.5.4f1) port of `../index.html`. Built-in render pipeline, legacy Input (for `Input.gyro`), uGUI. Everything (scene, UI, meshes, sound) is generated from code, so the project is built and tested without opening the Editor.

## Orientation

Landscape only (manifest `userLandscape`). The menus flip between the two landscape sides; a run locks the side it started on, so turning the phone to steer cannot rotate the screen. v1.1 allowed portrait and rotated at runtime, but on the owner's phone it stayed in portrait (without rotation lock); declaring landscape in the manifest (v1.2) fixed it.

## Controls

- **Gyroscope (default): hybrid steering wheel** (`Steering.TiltMapper`), from the owner's request "small rotation = small, reactive move; more rotation = heavier move":
  - up to 10° of tilt from level is **position control**: the ship moves 2×sensitivity degrees per phone degree, immediately (98% within 0.15 s in the self-test), holds while the phone holds, and comes back when the phone is levelled;
  - past 10° the ship **keeps circling**, faster with more tilt (`TiltToRate`, full 12 rad/s at 10 + 25/sensitivity degrees). At the boundary the direct offset is at its maximum and the rate is zero, so the two blend.
- **Sensor fusion:** the gyro rate around the screen normal is integrated and pulled gently (2.5/s) toward gravity's direction in the screen plane, so it also works with the phone lying flat. Gravity is rotated into screen axes per orientation, the gyro axis sign is learned from gravity, and the first reading of a run snaps by a quarter turn if a device reports its axes differently.
- **Rejected:** v1.0 "marble" absolute steering (the ship went to real-world down, so you had to turn the phone all the way round). v1.3/v1.4 pure rate control (tilt = speed) was called too slow to react, *not* too sensitive; softening it was the wrong direction.
- The title screen toggles the control mode (gyroscope / gyroscope inverted / touch) and the sensitivity (0.85×–2×, default 1.25×, saved under `tatva.gain2`).
- Touch: tap anywhere on the ring and the ship turns there. In the Editor the ←/→ keys also steer.
- The Android back button pauses; on the title screen it quits.

Not yet confirmed on a device: how v1.5's hybrid steering feels in the hand.

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
- `Scripts/Render/`: `Painter` (one dynamic mesh per frame, feathered neon primitives), `TunnelRenderer` (port of the canvas drawing code), `BloomEffect` + `Resources/Shaders/`
- `Scripts/Audio/Synth.cs`: sample-level synth on the audio thread (`OnAudioFilterRead`); `Music` = song + SFX
- `Scripts/Input/Steering.cs`: gyro fusion, hybrid tilt mapping (`TiltMapper`), touch, haptics
- `Scripts/UI/Hud.cs`: all screens built in code
- `Editor/BuildTools.cs`: SetupScene, ConfigureAndroid (+ generated icon), SelfTest, Snapshot, BuildAndroid
