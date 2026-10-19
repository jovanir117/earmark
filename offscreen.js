import { planSinks, listOutputs } from './resolve.js';

// tabId -> { stream, outs: [{ctx, n}], wants, plan, fx, title, chain }
const routes = new Map();
let muteOnLost = false;
const FX = { volume: 1, pan: 0, mono: false };

const list = () => [...routes].map(([tabId, r]) => ({
  tabId, title: r.title, fx: r.fx,
  outputs: r.plan.map(p => ({ deviceId: p.want.deviceId, label: p.want.label, fallback: p.fallback, muted: p.muted })),
}));

// Broadcast to background (badge) and any open popup (live refresh).
const notify = () => chrome.runtime.sendMessage({ target: 'bg', type: 'routes', routes: list() }).catch(() => {});

// One AudioContext per output, playing straight into its device (AudioContext.setSinkId).
// No <audio> element in between: that hop added a 100-300 ms jitter buffer, i.e. audio behind the video.
function newOut(r) {
  const ctx = new AudioContext({ latencyHint: 'interactive' });
  // tab -> mono -> balance -> gain (up to 200%) -> limiter (so the boost never clips) -> mute -> device
  const mono = ctx.createGain(), pan = ctx.createStereoPanner(), gain = ctx.createGain(), mute = ctx.createGain();
  const limiter = new DynamicsCompressorNode(ctx, { threshold: -2, knee: 0, ratio: 20, attack: 0.003, release: 0.1 });
  ctx.createMediaStreamSource(r.stream).connect(mono).connect(pan).connect(gain).connect(limiter).connect(mute).connect(ctx.destination);
  const o = { ctx, n: { mono, pan, gain, mute } };
  setNodes(o, r.fx);
  // Sink errors (BT drop mid-play): re-resolve.
  ctx.onerror = () => routes.get(r.tabId) === r && reapply(r);
  return o;
}

async function apply(r) {
  r.plan = planSinks(await navigator.mediaDevices.enumerateDevices(), r.wants, muteOnLost);
  while (r.outs.length < r.plan.length) r.outs.push(newOut(r));
  r.outs.splice(r.plan.length).forEach(o => o.ctx.close().catch(() => {}));
  await Promise.all(r.plan.map(async (p, i) => {
    const { ctx, n } = r.outs[i];
    try {
      if (ctx.sinkId !== p.deviceId) await ctx.setSinkId(p.deviceId);
    } catch {
      // Vanished between enumerate and setSinkId: treat as lost.
      await ctx.setSinkId('').catch(() => {});
      p.fallback = true;
      p.muted = muteOnLost || r.plan.length > 1;
    }
    n.mute.gain.value = p.muted ? 0 : 1;
    ctx.resume().catch(() => {});
  }));
}

// devicechange fires in bursts on BT connect; serialize so setSinkId calls never interleave.
function reapply(r) {
  r.chain = r.chain.then(() => routes.has(r.tabId) && apply(r)).catch(() => {}).then(notify);
  return r.chain;
}

function setNodes({ n }, { volume, pan, mono }) {
  n.gain.gain.value = Math.min(Math.max(volume, 0), 2);
  n.pan.pan.value = Math.min(Math.max(pan, -1), 1);
  // Forcing 1 channel makes Web Audio downmix to mono; the panner spreads it back to stereo.
  n.mono.channelCount = mono ? 1 : 2;
  n.mono.channelCountMode = mono ? 'explicit' : 'max';
}

function setFx(r, fx = {}) {
  r.fx = { ...r.fx, ...fx };
  r.outs.forEach(o => setNodes(o, r.fx));
}

async function capture(tabId, streamId) {
  const stream = await navigator.mediaDevices.getUserMedia({
    audio: { mandatory: { chromeMediaSource: 'tab', chromeMediaSourceId: streamId } },
  });
  const r = { tabId, stream, outs: [], plan: [], wants: [], fx: FX, chain: Promise.resolve() };
  // Tab closed or capture killed: drop the route so nothing leaks.
  stream.getAudioTracks()[0].addEventListener('ended', () => unroute({ tabId }));
  return r;
}

async function route({ tabId, title, streamId, wants }) {
  if (!wants?.length) return unroute({ tabId });
  let r = routes.get(tabId);
  if (!r) {
    if (!streamId) throw new Error('La pestaña ya no está capturada, elige el dispositivo otra vez');
    r = await capture(tabId, streamId);
    routes.set(tabId, r);
  }
  r.wants = wants;
  if (title) r.title = title;
  await reapply(r);
}

function unroute({ tabId }) {
  const r = routes.get(tabId);
  if (!r) return;
  routes.delete(tabId);
  r.stream.getTracks().forEach(t => t.stop()); // stopping capture un-mutes the original tab
  r.outs.forEach(o => o.ctx.close().catch(() => {}));
  notify();
}

function fx({ tabId, ...values }) {
  const r = routes.get(tabId);
  if (r) setFx(r, values);
}

function policy({ mute }) {
  muteOnLost = !!mute;
  routes.forEach(reapply);
}

const devices = async () =>
  listOutputs(await navigator.mediaDevices.enumerateDevices()).map(({ deviceId, label }) => ({ deviceId, label }));

// Headphones plugged/unplugged, BT connect/disconnect: re-resolve every route.
// Fallback routes snap back to their wanted device as soon as it reappears.
navigator.mediaDevices.addEventListener('devicechange', () => routes.forEach(reapply));

const handlers = { route, unroute, fx, policy, list, devices };

chrome.runtime.onMessage.addListener((msg, _sender, reply) => {
  if (msg.target !== 'offscreen' || !handlers[msg.type]) return;
  Promise.resolve()
    .then(() => handlers[msg.type](msg))
    .then(v => reply({ ok: true, v }), e => reply({ ok: false, error: String(e?.message || e) }));
  return true;
});
