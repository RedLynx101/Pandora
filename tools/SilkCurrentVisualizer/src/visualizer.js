const canvas = document.querySelector("#visualizer");
const ctx = canvas.getContext("2d", { alpha: false });
const stage = document.querySelector("#stage");
const statusEl = document.querySelector("#status");
const listenButton = document.querySelector("#listenButton");
const freezeButton = document.querySelector("#freezeButton");
const fullscreenButton = document.querySelector("#fullscreenButton");
const sensitivityInput = document.querySelector("#sensitivity");
const modeButtons = [...document.querySelectorAll(".mode-button")];
const meters = {
  bass: document.querySelector("#bassMeter"),
  mid: document.querySelector("#midMeter"),
  treble: document.querySelector("#trebleMeter")
};

const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
const demoMode = new URLSearchParams(window.location.search).has("demo");
const maxDpr = reducedMotion ? 1.25 : 1.75;
const state = {
  width: 1,
  height: 1,
  dpr: 1,
  time: 0,
  mode: "silk",
  frozen: false,
  live: false,
  sensitivity: Number(sensitivityInput.value),
  audioContext: null,
  analyser: null,
  stream: null,
  capturing: false,
  captureGeneration: 0,
  captureResources: null,
  lastFrame: null,
  frequency: null,
  waveform: null,
  bands: {
    level: 0,
    bass: 0,
    mid: 0,
    treble: 0
  },
  targets: {
    level: 0,
    bass: 0,
    mid: 0,
    treble: 0
  },
  pointer: {
    x: 0.5,
    y: 0.5,
    intensity: 0
  },
  particles: []
};

const palettes = {
  silk: {
    hueA: 164,
    hueB: 42,
    hueC: 344,
    shadow: "rgba(3, 4, 4, 0.155)",
    veil: "rgba(245, 243, 235, 0.07)"
  },
  ember: {
    hueA: 36,
    hueB: 344,
    hueC: 166,
    shadow: "rgba(7, 4, 3, 0.15)",
    veil: "rgba(242, 206, 126, 0.065)"
  },
  rain: {
    hueA: 252,
    hueB: 166,
    hueC: 42,
    shadow: "rgba(3, 4, 5, 0.16)",
    veil: "rgba(187, 169, 255, 0.06)"
  }
};

function resize() {
  // Leave the frozen bitmap untouched; CSS can scale it until animation resumes.
  if (state.frozen) return;
  const { innerWidth, innerHeight, devicePixelRatio } = window;
  state.dpr = Math.min(devicePixelRatio || 1, maxDpr);
  state.width = Math.max(1, innerWidth);
  state.height = Math.max(1, innerHeight);
  canvas.width = Math.floor(state.width * state.dpr);
  canvas.height = Math.floor(state.height * state.dpr);
  canvas.style.width = `${state.width}px`;
  canvas.style.height = `${state.height}px`;
  ctx.setTransform(state.dpr, 0, 0, state.dpr, 0, 0);
  seedParticles();
  clearCanvas();
}

function clearCanvas() {
  ctx.fillStyle = "#050605";
  ctx.fillRect(0, 0, state.width, state.height);
}

function seedParticles() {
  const area = state.width * state.height;
  const target = reducedMotion ? 220 : Math.min(860, Math.max(320, Math.round(area / 2850)));
  state.particles = Array.from({ length: target }, (_, index) => createParticle(index / target));
}

function createParticle(offset = Math.random()) {
  const angle = offset * Math.PI * 2 + Math.random() * 0.25;
  const maxRadius = Math.hypot(state.width, state.height) * 0.54;
  return {
    angle,
    radius: Math.random() * maxRadius,
    size: 0.55 + Math.random() * 1.8,
    speed: 0.0015 + Math.random() * 0.0048,
    drift: 0.8 + Math.random() * 1.8,
    seed: Math.random() * 1000,
    hueShift: Math.random()
  };
}

function setStatus(text) {
  statusEl.textContent = text;
}

function setLive(isLive) {
  state.live = isLive;
  stage.classList.toggle("is-live", isLive);
  listenButton.querySelector("span:last-child").textContent = isLive ? "Stop" : "Listen";
}

async function startCapture() {
  if (state.capturing || state.live) return;
  if (!navigator.mediaDevices?.getDisplayMedia) {
    setStatus("System audio capture needs Edge or Chrome.");
    return;
  }

  const generation = ++state.captureGeneration;
  state.capturing = true;
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
    setStatus("Live system audio");
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
  setStatus(demoMode ? "Preview drift" : "Waiting for system audio");
}

function rangeAverage(data, sampleRate, minHz, maxHz) {
  if (!data || !sampleRate) {
    return 0;
  }

  const nyquist = sampleRate / 2;
  const start = Math.max(0, Math.floor((minHz / nyquist) * data.length));
  const end = Math.min(data.length - 1, Math.ceil((maxHz / nyquist) * data.length));
  let total = 0;
  let count = 0;
  for (let index = start; index <= end; index += 1) {
    total += data[index] / 255;
    count += 1;
  }

  return count === 0 ? 0 : total / count;
}

function sampleAudio(seconds) {
  if (state.analyser && state.frequency && state.waveform && state.audioContext) {
    state.analyser.getByteFrequencyData(state.frequency);
    state.analyser.getByteTimeDomainData(state.waveform);
    const sampleRate = state.audioContext.sampleRate;
    state.targets.bass = rangeAverage(state.frequency, sampleRate, 28, 160);
    state.targets.mid = rangeAverage(state.frequency, sampleRate, 180, 2200);
    state.targets.treble = rangeAverage(state.frequency, sampleRate, 2400, 12000);
    state.targets.level = Math.min(
      1,
      state.targets.bass * 0.48 + state.targets.mid * 0.34 + state.targets.treble * 0.22
    );
  } else {
    const pulse = Math.sin(seconds * 0.78) * 0.5 + 0.5;
    const flicker = Math.sin(seconds * 2.3 + Math.cos(seconds * 0.3)) * 0.5 + 0.5;
    const shimmer = Math.sin(seconds * 4.6 + 2.1) * 0.5 + 0.5;
    state.targets.bass = 0.12 + pulse * 0.24;
    state.targets.mid = 0.10 + flicker * 0.2;
    state.targets.treble = 0.08 + shimmer * 0.18;
    state.targets.level = 0.16 + pulse * 0.2 + shimmer * 0.05;
  }

  const easing = state.live ? 0.11 : 0.045;
  for (const key of Object.keys(state.bands)) {
    const boosted = Math.min(1, state.targets[key] * state.sensitivity);
    state.bands[key] += (boosted - state.bands[key]) * easing;
  }

  state.pointer.intensity *= 0.94;
  meters.bass.style.transform = `scaleY(${0.12 + state.bands.bass * 0.94})`;
  meters.mid.style.transform = `scaleY(${0.12 + state.bands.mid * 0.94})`;
  meters.treble.style.transform = `scaleY(${0.12 + state.bands.treble * 0.94})`;
}

function drawBackground(palette) {
  ctx.globalCompositeOperation = "source-over";
  ctx.fillStyle = palette.shadow;
  ctx.fillRect(0, 0, state.width, state.height);

  const cx = state.width * (0.5 + (state.pointer.x - 0.5) * 0.05);
  const cy = state.height * (0.5 + (state.pointer.y - 0.5) * 0.05);
  const radius = Math.max(state.width, state.height) * (0.42 + state.bands.bass * 0.16);
  const gradient = ctx.createRadialGradient(cx, cy, 20, cx, cy, radius);
  gradient.addColorStop(0, `hsla(${palette.hueA}, 70%, 72%, ${0.06 + state.bands.level * 0.08})`);
  gradient.addColorStop(0.42, `hsla(${palette.hueB}, 80%, 68%, ${0.035 + state.bands.mid * 0.06})`);
  gradient.addColorStop(1, "rgba(0, 0, 0, 0)");
  ctx.fillStyle = gradient;
  ctx.fillRect(0, 0, state.width, state.height);
}

function drawStrands(palette, seconds) {
  const count = reducedMotion ? 9 : 18;
  const centerY = state.height * 0.54;
  const spread = state.height * 0.42;
  const step = Math.max(18, Math.floor(state.width / 96));
  const amp = 18 + state.bands.bass * 112 + state.pointer.intensity * 26;

  ctx.save();
  ctx.globalCompositeOperation = "lighter";
  ctx.lineCap = "round";
  ctx.lineJoin = "round";

  for (let line = 0; line < count; line += 1) {
    const lane = count === 1 ? 0 : line / (count - 1);
    const yBase = centerY + (lane - 0.5) * spread;
    const alpha = 0.035 + (1 - Math.abs(lane - 0.5) * 1.4) * 0.075 + state.bands.mid * 0.04;
    const hue = line % 3 === 0 ? palette.hueA : line % 3 === 1 ? palette.hueB : palette.hueC;

    ctx.beginPath();
    for (let x = -80; x <= state.width + 80; x += step) {
      const pointerPull = Math.exp(
        -Math.abs(x / state.width - state.pointer.x) * 4.8 -
        Math.abs(yBase / state.height - state.pointer.y) * 4.2
      ) * state.pointer.intensity * 44;
      const flow =
        Math.sin(x * 0.006 + seconds * (0.42 + line * 0.008) + line * 0.75) * amp +
        Math.sin(x * 0.014 - seconds * 0.68 + line * 1.25) * amp * 0.34 +
        Math.sin(x * 0.027 + seconds * 0.17 + line) * amp * 0.12;
      const y = yBase + flow + pointerPull;
      if (x === -80) {
        ctx.moveTo(x, y);
      } else {
        ctx.lineTo(x, y);
      }
    }

    ctx.strokeStyle = `hsla(${hue}, 82%, ${62 + state.bands.treble * 16}%, ${Math.max(0.018, alpha)})`;
    ctx.lineWidth = 0.7 + state.bands.level * 2.2 + (line % 5 === 0 ? 0.8 : 0);
    ctx.stroke();
  }

  ctx.restore();
}

function waveformValue(index, seconds) {
  if (state.waveform) {
    return (state.waveform[index % state.waveform.length] - 128) / 128;
  }

  return Math.sin(index * 0.33 + seconds * 1.6) * 0.42 + Math.sin(index * 0.07 - seconds * 0.42) * 0.28;
}

function drawWaveHalo(palette, seconds) {
  const cx = state.width * 0.5;
  const cy = state.height * 0.5;
  const points = reducedMotion ? 160 : 260;
  const baseRadius = Math.min(state.width, state.height) * (0.17 + state.bands.bass * 0.08);
  const energy = 24 + state.bands.mid * 74 + state.bands.treble * 22;

  ctx.save();
  ctx.globalCompositeOperation = "lighter";
  ctx.beginPath();
  for (let point = 0; point <= points; point += 1) {
    const turn = point / points;
    const angle = turn * Math.PI * 2;
    const sample = waveformValue(point * 7, seconds);
    const ripple = Math.sin(angle * 5 - seconds * 0.7) * state.bands.bass * 20;
    const radius = baseRadius + sample * energy + ripple;
    const x = cx + Math.cos(angle) * radius * (1.35 + state.bands.treble * 0.18);
    const y = cy + Math.sin(angle) * radius * (0.74 + state.bands.bass * 0.16);
    if (point === 0) {
      ctx.moveTo(x, y);
    } else {
      ctx.lineTo(x, y);
    }
  }

  ctx.closePath();
  ctx.strokeStyle = `hsla(${palette.hueB}, 88%, 72%, ${0.18 + state.bands.level * 0.26})`;
  ctx.lineWidth = 1.1 + state.bands.level * 3.2;
  ctx.shadowBlur = 22 + state.bands.treble * 34;
  ctx.shadowColor = `hsla(${palette.hueA}, 80%, 68%, 0.62)`;
  ctx.stroke();

  ctx.globalAlpha = 0.065 + state.bands.bass * 0.08;
  ctx.fillStyle = `hsla(${palette.hueC}, 76%, 70%, 1)`;
  ctx.fill();
  ctx.restore();
}

function drawParticles(palette, seconds) {
  const cx = state.width * 0.5;
  const cy = state.height * 0.5;
  const pullX = (state.pointer.x - 0.5) * state.width * 0.08 * state.pointer.intensity;
  const pullY = (state.pointer.y - 0.5) * state.height * 0.08 * state.pointer.intensity;
  const maxRadius = Math.hypot(state.width, state.height) * 0.58;

  ctx.save();
  ctx.globalCompositeOperation = "lighter";

  for (const particle of state.particles) {
    particle.angle += particle.speed * (0.55 + state.bands.mid * 5.8);
    particle.radius += Math.sin(seconds * particle.drift + particle.seed) * 0.025 + (state.bands.bass - 0.18) * 0.12;
    if (particle.radius > maxRadius || particle.radius < 10) {
      Object.assign(particle, createParticle());
      particle.radius = Math.random() * maxRadius * 0.28;
    }

    const wobble = Math.sin(seconds * 0.8 + particle.seed) * (12 + state.bands.treble * 28);
    const x = cx + Math.cos(particle.angle) * (particle.radius + wobble) + pullX;
    const y = cy + Math.sin(particle.angle * 0.92) * (particle.radius * 0.56 + wobble * 0.4) + pullY;
    const hue = particle.hueShift < 0.5 ? palette.hueA : particle.hueShift < 0.82 ? palette.hueB : palette.hueC;
    const size = particle.size * (0.82 + state.bands.treble * 3.8 + state.bands.level * 1.3);
    ctx.beginPath();
    ctx.fillStyle = `hsla(${hue}, 86%, ${66 + state.bands.treble * 12}%, ${0.1 + state.bands.treble * 0.22})`;
    ctx.arc(x, y, size, 0, Math.PI * 2);
    ctx.fill();
  }

  ctx.restore();
}

function drawVignette(palette) {
  const gradient = ctx.createRadialGradient(
    state.width * 0.5,
    state.height * 0.5,
    Math.min(state.width, state.height) * 0.18,
    state.width * 0.5,
    state.height * 0.5,
    Math.max(state.width, state.height) * 0.76
  );
  gradient.addColorStop(0, "rgba(0, 0, 0, 0)");
  gradient.addColorStop(0.72, palette.veil);
  gradient.addColorStop(1, "rgba(0, 0, 0, 0.72)");
  ctx.globalCompositeOperation = "source-over";
  ctx.fillStyle = gradient;
  ctx.fillRect(0, 0, state.width, state.height);
}

function draw(now) {
  requestAnimationFrame(draw);
  const elapsed = state.lastFrame === null ? 0 : Math.min(0.1, Math.max(0, (now - state.lastFrame) / 1000));
  state.lastFrame = now;
  if (state.frozen) return;
  state.time += elapsed;
  sampleAudio(state.time);

  const palette = palettes[state.mode];
  drawBackground(palette);
  drawStrands(palette, state.time);
  drawWaveHalo(palette, state.time);
  drawParticles(palette, state.time);
  drawVignette(palette);
}

listenButton.addEventListener("click", () => {
  if (state.live || state.capturing) {
    stopCapture();
  } else {
    startCapture();
  }
});

freezeButton.addEventListener("click", () => {
  state.frozen = !state.frozen;
  if (!state.frozen && (state.width !== window.innerWidth || state.height !== window.innerHeight)) resize();
  freezeButton.classList.toggle("is-active", state.frozen);
  freezeButton.setAttribute("aria-pressed", String(state.frozen));
});

fullscreenButton.addEventListener("click", () => {
  if (document.fullscreenElement) {
    document.exitFullscreen().catch(() => {});
  } else {
    document.documentElement.requestFullscreen().catch(() => {});
  }
});

sensitivityInput.addEventListener("input", () => {
  state.sensitivity = Number(sensitivityInput.value);
});

modeButtons.forEach(button => {
  button.addEventListener("click", () => {
    state.mode = button.dataset.mode;
    stage.dataset.mode = state.mode;
    for (const modeButton of modeButtons) {
      modeButton.classList.toggle("is-active", modeButton === button);
      modeButton.setAttribute("aria-pressed", String(modeButton === button));
    }
  });
});

window.addEventListener("pointermove", event => {
  state.pointer.x = event.clientX / Math.max(1, state.width);
  state.pointer.y = event.clientY / Math.max(1, state.height);
  state.pointer.intensity = Math.min(1, state.pointer.intensity + 0.08);
});

window.addEventListener("pointerdown", event => {
  state.pointer.x = event.clientX / Math.max(1, state.width);
  state.pointer.y = event.clientY / Math.max(1, state.height);
  state.pointer.intensity = 1;
});

window.addEventListener("resize", resize);
window.addEventListener("pagehide", stopCapture);

resize();
clearCanvas();
if (demoMode) {
  setStatus("Preview drift");
}
requestAnimationFrame(draw);
