# TÁTVA

A neon tunnel skill game played to the beat. Steer through the gaps in the walls, graze them for slow motion and a bigger multiplier, collect the light orbs. 8 zones, rising tempo, a bending and rolling tunnel, synthesized music.

- **Android:** download `Tatva.apk` from [Releases](https://github.com/szkript/tatva/releases) (Android 8.0+, sideload). Landscape. Steering uses the **gyroscope**: tilt the phone like a steering wheel. A light tilt gives a small, precise move; a bigger tilt makes the ship circle faster. Touch mode is selectable on the title screen.
- **Browser:** open `index.html` (a single file, no build). SpeedX 3D-style first-person run over a track that switches between tube, flat plain and outside-of-tube. Steer with ←/→ or just by moving the mouse (the ship goes where the cursor points: the screen width spans one full lap of the track; on touch, hold the left/right side), boost with ↑/W/Shift or a held mouse button (two fingers on touch). Testing: `?z=N` starts in zone N+1, `?auto=1` lets the demo pilot fly.

## Source

- `index.html`: the web version (Canvas 2D + WebAudio)
- `TatvaAndroid/`: Unity 6 (6000.5.4f1) Android version, built entirely from code; build steps in [`TatvaAndroid/README.md`](TatvaAndroid/README.md)
