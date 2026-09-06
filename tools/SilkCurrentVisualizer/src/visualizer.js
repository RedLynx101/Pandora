// Silk Current: a local audio instrument. Renderer and UI rebuilt around projected light sculptures.
// Capture ownership remains generation-scoped: stale permission results never retain a stream.
const $ = selector => document.querySelector(selector);
const canvas = $("#visualizer");
const ctx = canvas.getContext("2d", { alpha: false });
const stage = $("#stage");
const statusEl = $("#status");
const listenButton = $("#listenButton");
const freezeButton = $("#freezeButton");
const fullscreenButton = $("#fullscreenButton");
const sensitivityInput = $("#sensitivity");
const demoButton = $("#demoButton");
const modeButtons = [...document.querySelectorAll(".mode-button")];
const meters = { bass: $("#bassMeter"), mid: $("#midMeter"), treble: $("#trebleMeter") };
const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
let demoMode = new URLSearchParams(window.location.search).has("demo");
const state = {
  width: 1, height: 1, dpr: 1, time: 0, mode: "silk", scene: 0, sceneTarget: 0,
  frozen: reducedMotion, live: false, sensitivity: Number(sensitivityInput.value),
  audioContext: null, analyser: null, stream: null, capturing: false, captureGeneration: 0,
  captureResources: null, lastFrame: null, frequency: null, waveform: null,
  bands: { level: 0, bass: 0, mid: 0, treble: 0 },
  targets: { level: 0, bass: 0, mid: 0, treble: 0 },
  pointer: { x: 0.5, y: 0.5 }, particles: []
};
const TAU = Math.PI * 2;
const clamp = (value, low = 0, high = 1) => Math.min(high, Math.max(low, value));
function setStatus(text) { statusEl.textContent = text; }
function setLive(live) {
  state.live = live;
  stage.classList.toggle("is-live", live);
  listenButton.querySelector("span:last-child").textContent = live ? "Disconnect" : "Connect audio";
  demoButton.disabled = live || state.capturing;
}
function resize(force = false) {
  if (state.frozen && force !== true) return;
  state.width = Math.max(1, window.innerWidth);
  state.height = Math.max(1, window.innerHeight);
  state.dpr = Math.min(window.devicePixelRatio || 1, 1.75);
  canvas.width = Math.floor(state.width * state.dpr);
  canvas.height = Math.floor(state.height * state.dpr);
  canvas.style.width = "100%"; canvas.style.height = "100%";
  ctx.setTransform(state.dpr, 0, 0, state.dpr, 0, 0);
  // Stable field, not frame-dependent random noise.
  state.particles = Array.from({ length: 150 }, (_, i) => ({
    x: ((i * 0.61803398875) % 1), y: ((i * 0.41421356237) % 1), size: i % 4 === 0 ? 1 : 0.5
  }));
  render();
}
async function startCapture() {
  if (state.capturing || state.live) return;
  if (!navigator.mediaDevices?.getDisplayMedia) {
    setStatus("System audio capture needs Edge or Chrome.");
    return;
  }

  const generation = ++state.captureGeneration;
  state.capturing = true;
  demoButton.disabled = true;
  listenButton.querySelector("span:last-child").textContent = "Cancel";
  let stream = null;
  let audioContext = null;
  const resources = { stream: null, context: null, disposed: false };
  try {
    setStatus("Waiting for capture permission");
    stream = await navigator.mediaDevices.getDisplayMedia({
      video: {
        frameRate: 1,
        width: { ideal: 16 },
        height: { ideal: 16 }
      },
      audio: {
        echoCancellation: false,
        noiseSuppression: false,
        autoGainControl: false,
        channelCount: 2
      }
    });

    if (generation !== state.captureGeneration) return;
    resources.stream = stream;
    state.captureResources = resources;
    const audioTracks = stream.getAudioTracks();
    if (audioTracks.length === 0) {
      setStatus("No shared audio track. Enable Share audio and try again.");
      return;
    }

    audioContext = new AudioContext();
    resources.context = audioContext;
    await audioContext.resume();
    if (generation !== state.captureGeneration || audioTracks.every(track => track.readyState === "ended")) return;
    const analyser = audioContext.createAnalyser();
    analyser.fftSize = 2048;
    analyser.smoothingTimeConstant = 0.86;

    const source = audioContext.createMediaStreamSource(stream);
    source.connect(analyser);
    state.stream = stream;
    state.audioContext = audioContext;
    state.analyser = analyser;
    state.frequency = new Uint8Array(analyser.frequencyBinCount);
    state.waveform = new Uint8Array(analyser.fftSize);
    state.captureResources = null;
    stream.getTracks().forEach(track => {
      track.addEventListener("ended", () => {
        if (generation === state.captureGeneration) stopCapture();
      }, { once: true });
    });

    setLive(true);
    setStatus("LIVE · shared audio");
  } catch (error) {
    if (generation !== state.captureGeneration) return;
    if (error?.name === "NotAllowedError") {
      setStatus("Capture canceled.");
    } else {
      setStatus("Could not start audio capture.");
    }
  } finally {
    // A canceled/stale permission result never acquires ownership of capture resources.
    if (stream !== state.stream) {
      // A permission promise can resolve after cancellation, before provisional registration.
      resources.stream ??= stream;
      disposeCaptureResources(resources);
    }
    if (generation === state.captureGeneration) {
      state.captureResources = null;
      state.capturing = false;
      setLive(state.live);
    }
  }
}

function disposeCaptureResources(resources) {
  if (!resources || resources.disposed) return;
  resources.disposed = true;
  resources.stream?.getTracks().forEach(track => track.stop());
  resources.context?.close().catch(() => {});
}

function stopCapture() {
  ++state.captureGeneration;
  state.capturing = false;
  const provisional = state.captureResources;
  state.captureResources = null;
  disposeCaptureResources(provisional);
  const stream = state.stream;
  const audioContext = state.audioContext;
  state.stream = null;
  state.audioContext = null;
  state.analyser = null;
  state.frequency = null;
  state.waveform = null;
  stream?.getTracks().forEach(track => track.stop());
  audioContext?.close().catch(() => {});
  setLive(false);
  setStatus(demoMode ? "DEMO · generated signal" : "IDLE · no audio connected");
}


function rangeAverage(data, sampleRate, minHz, maxHz) {
  const start = Math.max(0, Math.floor(minHz * 2 / sampleRate * data.length));
  const end = Math.min(data.length - 1, Math.ceil(maxHz * 2 / sampleRate * data.length));
  let sum = 0;
  for (let i = start; i <= end; i++) sum += data[i] / 255;
  return end < start ? 0 : sum / (end - start + 1);
}
function sampleAudio(seconds, elapsed = 1 / 60) {
  if (state.analyser) {
    state.analyser.getByteFrequencyData(state.frequency);
    state.analyser.getByteTimeDomainData(state.waveform);
    state.targets.bass = rangeAverage(state.frequency, state.audioContext.sampleRate, 28, 160);
    state.targets.mid = rangeAverage(state.frequency, state.audioContext.sampleRate, 180, 2200);
    state.targets.treble = rangeAverage(state.frequency, state.audioContext.sampleRate, 2400, 12000);
  } else {
    state.targets.bass = demoMode ? Math.pow(Math.max(0, Math.sin(seconds * 2.6)), 6) * 0.8 : 0;
    state.targets.mid = demoMode ? 0.25 + Math.sin(seconds * 1.3) * 0.18 : 0;
    state.targets.treble = demoMode ? 0.12 + Math.pow(Math.sin(seconds * 4.1), 8) * 0.4 : 0;
  }
  state.targets.level = state.targets.bass * 0.48 + state.targets.mid * 0.34 + state.targets.treble * 0.18;
  for (const key of Object.keys(state.bands)) {
    const target = clamp(state.targets[key] * state.sensitivity);
    const smoothing = 1 - Math.exp(-elapsed * (target > state.bands[key] ? 13 : 4));
    state.bands[key] += (target - state.bands[key]) * smoothing;
  }
  for (const key of ["bass", "mid", "treble"]) meters[key].style.transform = `scaleX(${state.bands[key]})`;
}
function point(scene, u, v, time) {
  const bass = state.bands.bass, mid = state.bands.mid;
  if (scene === 1) { // A folded ribbon with a travelling frequency wave.
    const x = (u / TAU - 0.5) * 4.8;
    const fold = Math.sin(u * 1.45 + time * 0.28 + v * 0.32);
    return [x, fold * (0.55 + bass * 0.6) + Math.cos(v) * 0.28,
      Math.sin(v) * 0.65 + Math.cos(u * 1.2 - time * 0.16) * 0.7];
  }
  if (scene === 2) { // A woven, precessing figure-eight.
    const r = 1.25 + Math.cos(v) * (0.24 + bass * 0.3);
    return [Math.sin(u) * r * 1.5, Math.sin(u * 2) * r * 0.62,
      Math.cos(u) * 0.7 + Math.sin(v + u * 3 + time * 0.2) * (0.3 + mid * 0.35)];
  }
  // A breathing toroidal sculpture. Bass expands its core; mid folds its filaments.
  const ripple = Math.sin(u * 5 + time * 0.5 + v) * mid * 0.16;
  const r = 1.18 + (0.38 + bass * 0.22) * Math.cos(v) + ripple;
  return [r * Math.cos(u), r * Math.sin(u), Math.sin(v) * (0.42 + mid * 0.2)];
}
function project(p, time) {
  const yaw = Math.sin(time * 0.09) * 0.2 + (state.pointer.x - 0.5) * 0.25;
  const tilt = 0.95 + Math.sin(time * 0.12) * 0.14 + (state.pointer.y - 0.5) * 0.12;
  const x = p[0] * Math.cos(yaw) + p[2] * Math.sin(yaw);
  const z = -p[0] * Math.sin(yaw) + p[2] * Math.cos(yaw);
  const y = p[1] * Math.cos(tilt) - z * Math.sin(tilt);
  const depth = p[1] * Math.sin(tilt) + z * Math.cos(tilt);
  const scale = Math.min(state.width * 0.16, state.height * 0.245) * 4.8 / (4.8 + depth);
  const angle = -0.23;
  return [state.width * 0.5 + (x * Math.cos(angle) - y * Math.sin(angle)) * scale,
    state.height * 0.49 + (x * Math.sin(angle) + y * Math.cos(angle)) * scale, depth];
}
function render() {
  const { width: w, height: h, time: t } = state;
  ctx.globalCompositeOperation = "source-over";
  ctx.fillStyle = "#06090e"; ctx.fillRect(0, 0, w, h);
  const glow = ctx.createRadialGradient(w * 0.5, h * 0.49, 0, w * 0.5, h * 0.49, Math.min(w, h) * 0.65);
  glow.addColorStop(0, `rgba(64, 41, 22, ${0.18 + state.bands.bass * 0.15})`);
  glow.addColorStop(0.55, "rgba(20, 35, 49, 0.14)"); glow.addColorStop(1, "rgba(6,9,14,0)");
  ctx.fillStyle = glow; ctx.fillRect(0, 0, w, h);
  ctx.fillStyle = "rgba(180,203,219,0.25)";
  for (const star of state.particles) ctx.fillRect(star.x * w, star.y * h, star.size, star.size);
  const low = Math.floor(state.scene), high = Math.min(2, low + 1), mix = state.scene - low;
  const strands = reducedMotion ? 44 : 76, steps = reducedMotion ? 112 : 176;
  ctx.globalCompositeOperation = "lighter";
  ctx.lineWidth = 0.72;
  for (let j = 0; j < strands; j++) {
    const v = j / strands * TAU;
    ctx.beginPath();
    for (let i = 0; i <= steps; i++) {
      const u = i / steps * TAU;
      const a = point(low, u, v, t), b = point(high, u, v, t);
      const p = project(a.map((value, k) => value + (b[k] - value) * mix), t);
      if (i === 0) ctx.moveTo(p[0], p[1]); else ctx.lineTo(p[0], p[1]);
    }
    const light = (Math.sin(v + t * 0.12) + 1) * 0.5;
    const hues = [28, 170, 220];
    const hue = hues[low] + (hues[high] - hues[low]) * mix + light * 14;
    ctx.strokeStyle = `hsla(${hue}, ${74 - state.scene * 8}%, ${48 + light * 29}%, ${0.13 + light * 0.47 + state.bands.treble * 0.12})`;
    ctx.stroke();
  }
  // A bright thread travels through the weave; actual waveform perturbs its orbit.
  ctx.beginPath();
  for (let i = 0; i <= steps; i++) {
    const u = i / steps * TAU;
    const audio = state.waveform ? (state.waveform[Math.floor(i / steps * (state.waveform.length - 1))] - 128) / 128 : 0;
    const v = t * 0.3 + audio * 0.35;
    const a = point(low, u, v, t), b = point(high, u, v, t);
    const p = project(a.map((value, k) => value + (b[k] - value) * mix), t);
    i === 0 ? ctx.moveTo(p[0], p[1]) : ctx.lineTo(p[0], p[1]);
  }
  ctx.strokeStyle = "rgba(247,228,194,0.7)"; ctx.lineWidth = 1.15; ctx.stroke();
  ctx.globalCompositeOperation = "source-over";
}
function draw(now) {
  requestAnimationFrame(draw);
  const elapsed = state.lastFrame === null ? 0 : clamp((now - state.lastFrame) / 1000, 0, 0.05);
  state.lastFrame = now;
  if (state.frozen || document.hidden) return;
  state.time += elapsed;
  state.scene += (state.sceneTarget - state.scene) * (1 - Math.exp(-elapsed * 3));
  if (Math.abs(state.sceneTarget - state.scene) < 0.001) state.scene = state.sceneTarget;
  sampleAudio(state.time, elapsed); render();
}
function toggleFreeze() {
  state.frozen = !state.frozen;
  if (!state.frozen && (state.width !== window.innerWidth || state.height !== window.innerHeight)) resize();
  freezeButton.setAttribute("aria-pressed", String(state.frozen));
  freezeButton.textContent = state.frozen ? "Resume motion" : "Pause motion";
}
function toggleDemo() {
  if (state.live || state.capturing) return;
  demoMode = !demoMode;
  demoButton.setAttribute("aria-pressed", String(demoMode));
  setStatus(demoMode ? "DEMO · generated signal" : "IDLE · no audio connected");
}
function fullscreen() {
  const action = document.fullscreenElement ? document.exitFullscreen() : document.documentElement.requestFullscreen();
  action.catch(() => setStatus("Full screen is unavailable in this browser."));
}
listenButton.addEventListener("click", () => state.live || state.capturing ? stopCapture() : startCapture());
freezeButton.addEventListener("click", toggleFreeze);
fullscreenButton.addEventListener("click", fullscreen);
demoButton.addEventListener("click", toggleDemo);
sensitivityInput.addEventListener("input", () => {
  state.sensitivity = clamp(Number(sensitivityInput.value) || 1, 0.5, 2.5);
  $("#gainValue").textContent = state.sensitivity.toFixed(2) + "×";
});
modeButtons.forEach((button, index) => button.addEventListener("click", () => {
  state.mode = button.dataset.mode; state.sceneTarget = index; stage.dataset.mode = state.mode;
  for (const item of modeButtons) item.setAttribute("aria-pressed", String(item === button));
  $("#sceneNumber").textContent = "0" + (index + 1);
  $("#sceneName").textContent = button.dataset.label;
  if (state.frozen) { state.scene = index; render(); }
}));
window.addEventListener("pointermove", event => {
  state.pointer.x = clamp(event.clientX / state.width); state.pointer.y = clamp(event.clientY / state.height);
});
window.addEventListener("keydown", event => {
  if (["INPUT", "BUTTON", "SUMMARY", "SELECT", "TEXTAREA"].includes(event.target?.tagName)) return;
  if (event.code === "Space") { event.preventDefault(); toggleFreeze(); }
  if (event.key?.toLowerCase() === "f") fullscreen();
  if (event.key?.toLowerCase() === "d") toggleDemo();
});
window.addEventListener("resize", resize);
window.addEventListener("pagehide", stopCapture);
freezeButton.setAttribute("aria-pressed", String(state.frozen));
freezeButton.textContent = state.frozen ? "Resume motion" : "Pause motion";
demoButton.setAttribute("aria-pressed", String(demoMode));
setStatus(demoMode ? "DEMO · generated signal" : "IDLE · no audio connected");
resize(true);
requestAnimationFrame(draw);
