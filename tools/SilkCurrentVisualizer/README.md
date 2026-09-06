# Silk Current Visualizer

Silk Current is a lightweight Pandora companion visualizer. It is a static
web canvas app served from localhost so Edge or Chrome can grant system/tab
audio capture through `getDisplayMedia`.

## Run

```powershell
.\tools\SilkCurrentVisualizer\start-visualizer.ps1
```

The launcher binds to `127.0.0.1` only, starts a tiny PowerShell HTTP server,
and opens the visualizer in the default browser. If the default port is already
in use, it tries nearby ports.

## Capture

Click `Connect audio`, choose a screen or browser tab, and enable audio sharing in the
browser picker. Chrome and Edge expose this differently depending on whether
the source is an entire screen, window, or browser tab.

## Preview

For visual QA without starting audio capture:

```text
http://127.0.0.1:8787/?demo=1
```

## Notes

- Orbital, Tidal, and Prism are different projected filament sculptures, with smooth
  geometric transitions. Bass changes their volume, midrange folds the weave, and
  high frequencies change brightness; the shared waveform drives a travelling thread.
- Without capture, the sculpture drifts but its signal meters stay at zero.
  `Settings > Demo signal` (or `?demo=1`) explicitly enables generated audio-like motion.
- Space pauses motion, F toggles full screen, and D toggles demo. These shortcuts do
  not intercept focused controls. Reduced-motion preference starts with a still image.
- The renderer caps pixel density and filament counts, uses elapsed-time smoothing,
  and suspends painting for paused/hidden pages. No WebGL, CDN, or external assets.
- The radio dock has a direct **Visualizer** button; playback stays in Pandora.
- Verification: `node --test tools/SilkCurrentVisualizer/tests/visualizer.test.cjs`
  and `tools/SilkCurrentVisualizer/tests/server.test.ps1`. Capture tests use fixture APIs,
  not live permissions or a real audio device. Real capture requires the browser picker.

- No npm packages or build step are required.
- The page never sends audio anywhere; analysis stays in the browser tab.
- The screen/video track is kept alive only because browser system-audio capture
  is attached to display capture permissions.
