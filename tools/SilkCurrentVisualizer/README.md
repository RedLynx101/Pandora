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

Click `Listen`, choose a screen or browser tab, and enable audio sharing in the
browser picker. Chrome and Edge expose this differently depending on whether
the source is an entire screen, window, or browser tab.

## Preview

For visual QA without starting audio capture:

```text
http://127.0.0.1:8787/?demo=1
```

## Notes

- No npm packages or build step are required.
- The page never sends audio anywhere; analysis stays in the browser tab.
- The screen/video track is kept alive only because browser system-audio capture
  is attached to display capture permissions.
