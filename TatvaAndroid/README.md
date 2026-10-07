# TÁTVA for Android

Unity 6 (6000.5.4f1) port of `../index.html`. Built-in render pipeline, legacy Input (for `Input.gyro`), uGUI. Everything (scene, UI, meshes, sound) is generated from code, so the project is built and tested without opening the Editor.

## Controls

- **Gyroscope (default):** hold the phone and turn it like a steering wheel. The ship rolls toward real-world "down", like a marble. The gyro rotation rate is integrated and corrected by gravity (a complementary filter), so it also works with the phone lying flat. The sign of the gyro axis is learned at runtime from gravity.
- The title screen toggles the control mode (gyroscope / gyroscope inverted / touch) and the sensitivity (1×–2.5×). Both are saved in PlayerPrefs.
- Touch: tap anywhere on the ring and the ship turns there. In the Editor the ←/→ keys also steer.
- The Android back button pauses; on the title screen it quits.

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
- `Scripts/Input/Steering.cs`: gyro fusion, touch, haptics
- `Scripts/UI/Hud.cs`: all screens built in code
- `Editor/BuildTools.cs`: SetupScene, ConfigureAndroid (+ generated icon), SelfTest, Snapshot, BuildAndroid
