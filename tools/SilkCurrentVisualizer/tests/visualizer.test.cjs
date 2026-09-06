// No browser permissions, network or real audio devices. Exercise the shipped script with fixture APIs.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { test } = require('node:test');
const source = fs.readFileSync(path.join(__dirname, '../src/visualizer.js'), 'utf8');

function fixture(options = {}) {
  const nodes = new Map();
  const node = id => {
    if (!nodes.has(id)) nodes.set(id, { value: '1.08', style: {}, dataset: {}, listeners: {},
      classList: { toggle() {} }, setAttribute() {},
      addEventListener(name, callback) { this.listeners[name] = callback; },
      querySelector: () => node(id + '/label') });
    return nodes.get(id);
  };
  let paints = 0, captureCalls = 0, closed = 0, samples = 0;
  const drawing = new Proxy({}, { get: (_, name) => name === 'createRadialGradient'
    ? () => ({ addColorStop() {} }) : () => { paints++; }, set: () => true });
  node('#visualizer').getContext = () => drawing;
  const pending = [];
  const browserEvents = {};
  class AudioContext {
    constructor() { if (options.contextFails) throw new Error('Context unavailable'); this.sampleRate = 48000; }
    async resume() { options.onResume?.(); if (options.resumeFails) throw new Error('Resume unavailable'); if (options.resume) await options.resume; }
    async close() { closed++; }
    createAnalyser() { return { frequencyBinCount: 1024, getByteFrequencyData() { samples++; }, getByteTimeDomainData() {} }; }
    createMediaStreamSource() { if (options.sourceFails) throw new Error('Source unavailable'); return { connect() {} }; }
  }
  const context = vm.createContext({
    document: { querySelector: node, querySelectorAll: () => [] },
    window: { innerWidth: 800, innerHeight: 500, devicePixelRatio: 1, location: { search: options.idle ? '' : '?demo' },
      matchMedia: () => ({ matches: !!options.reducedMotion }), addEventListener: (name, callback) => { browserEvents[name] = callback; } },
    navigator: { mediaDevices: { getDisplayMedia: () => { captureCalls++; return new Promise((resolve, reject) => pending.push({ resolve, reject })); } } },
    AudioContext, URLSearchParams, Uint8Array, requestAnimationFrame() {}
  });
  vm.runInContext(source, context);
  return { context, node, pending, browserEvents, read: code => vm.runInContext(code, context),
    start: () => vm.runInContext('startCapture()', context), stop: () => vm.runInContext('stopCapture()', context),
    get paints() { return paints; }, get captures() { return captureCalls; }, get closed() { return closed; }, get samples() { return samples; } };
}
function stream(withAudio = true) {
  const tracks = [{ readyState: 'live', stopped: false, stop() { this.stopped = true; this.readyState = 'ended'; },
    addEventListener(name, callback) { this[name] = callback; } }];
  return { tracks, getTracks: () => tracks, getAudioTracks: () => withAudio ? tracks : [] };
}

test('pending capture is serialized; canceled late result releases all tracks', async () => {
  const f = fixture(), capture = f.start();
  await f.start();
  assert.equal(f.captures, 1);
  f.stop();
  const result = stream(); f.pending[0].resolve(result); await capture;
  assert(result.tracks.every(t => t.stopped));
  assert.equal(f.read('state.live'), false);
  assert.equal(f.read('state.stream'), null);
});
test('audio setup failures and no-audio results release capture resources', async () => {
  for (const option of ['contextFails', 'resumeFails', 'sourceFails', 'noAudio']) {
    const f = fixture({ [option]: true }), capture = f.start(), result = stream(option !== 'noAudio');
    f.pending[0].resolve(result); await capture;
    assert(result.tracks.every(t => t.stopped), option);
    assert.equal(f.read('state.capturing || state.live'), false, option);
    assert.equal(f.closed, ['resumeFails', 'sourceFails'].includes(option) ? 1 : 0);
  }
});
test('live capture ends safely on track end or pagehide; stale callbacks do not stop a newer capture', async () => {
  const f = fixture(), a = stream(), first = f.start(); f.pending[0].resolve(a); await first;
  assert.equal(f.read('state.live'), true);
  a.tracks[0].ended(); assert.equal(f.closed, 1);
  const b = stream(), second = f.start(); f.pending[1].resolve(b); await second;
  a.tracks[0].ended(); assert.equal(f.read('state.live'), true);
  f.browserEvents.pagehide(); assert(b.tracks[0].stopped); assert.equal(f.closed, 2);
});
test('permission rejection is caught and capture can be retried', async () => {
  const f = fixture(), first = f.start(); f.pending[0].reject({ name: 'NotAllowedError' }); await first;
  assert.match(f.node('#status').textContent, /canceled/);
  const second = f.start(); f.pending[1].resolve(stream()); await second;
  assert.equal(f.read('state.live'), true); f.stop();
});
test('Cancel releases provisional capture immediately even if context resume is unresolved', async () => {
  let resume;
  let entered;
  const resuming = new Promise(resolve => { entered = resolve; });
  const f = fixture({ resume: new Promise(resolve => { resume = resolve; }), onResume: entered });
  const result = stream(), capture = f.start(); f.pending[0].resolve(result);
  await resuming;
  f.stop();
  assert(result.tracks[0].stopped); assert.equal(f.closed, 1);
  resume(); await capture;
  assert.equal(f.read('state.live'), false); assert.equal(f.closed, 1);
});
test('Freeze stops canvas drawing and particle mutation, then resumes without a time jump', () => {
  const f = fixture(); f.read('draw(1000); draw(1016)');
  const particles = f.read('JSON.stringify(state.particles)'), time = f.read('state.time'), paints = f.paints;
  f.node('#freezeButton').listeners.click(); f.read('draw(1800000)');
  assert.equal(f.read('JSON.stringify(state.particles)'), particles);
  assert.equal(f.paints, paints); assert.equal(f.read('state.time'), time);
  f.context.window.innerWidth = 1200; f.browserEvents.resize();
  assert.equal(f.paints, paints, 'Resize must preserve the frozen bitmap');
  f.node('#freezeButton').listeners.click(); f.read('draw(1800016)');
  assert(f.paints > paints); assert(f.read('state.time') - time < 0.02);
});

test('idle never invents an audio signal; demo is explicit and reversible', () => {
  const f = fixture({ idle: true });
  f.read('sampleAudio(12, 1)');
  assert.equal(f.read('state.bands.level'), 0);
  f.node('#demoButton').listeners.click();
  f.read('sampleAudio(12, 1)');
  assert(f.read('state.bands.level') > 0);
  assert.match(f.node('#status').textContent, /DEMO/);
  f.node('#demoButton').listeners.click();
  f.read('sampleAudio(13, 10)');
  assert(f.read('state.bands.level') < 0.0001);
  assert.match(f.node('#status').textContent, /IDLE/);
});

test('reduced motion starts with a still composition and resumes only by choice', () => {
  const f = fixture({ reducedMotion: true });
  const paints = f.paints;
  f.read('draw(1000); draw(1200)');
  assert.equal(f.paints, paints);
  assert.equal(f.read('state.frozen'), true);
  f.node('#freezeButton').listeners.click(); f.read('draw(1216)');
  assert(f.paints > paints);
});

test('all sculpture modes and interpolation produce finite projected geometry', () => {
  const f = fixture();
  assert.equal(f.read(`(() => {
    for (const width of [320, 1440, 2560]) {
      state.width = width; state.height = width < 500 ? 844 : 900;
      for (let mode = 0; mode <= 2; mode++) {
        state.bands.bass = 1; state.bands.mid = 1;
        for (let i = 0; i < 100; i++) {
          const p = project(point(mode, i / 100 * TAU, i * 0.1, 99), 99);
          if (!p.every(Number.isFinite)) return false;
        }
        state.scene = mode; render();
      }
    }
    return true;
  })()`), true);
});

test('gain clamps invalid input and hidden pages do not animate', () => {
  const f = fixture();
  f.node('#sensitivity').value = 'NaN'; f.node('#sensitivity').listeners.input();
  assert.equal(f.read('state.sensitivity'), 1);
  f.node('#sensitivity').value = '100'; f.node('#sensitivity').listeners.input();
  assert.equal(f.read('state.sensitivity'), 2.5);
  f.context.document.hidden = true;
  const paints = f.paints; f.read('draw(1000); draw(1016)');
  assert.equal(f.paints, paints);
});
