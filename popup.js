import { listOutputs, deviceKind } from './resolve.js';
import { call } from './rpc.js';

const ICON = { headphones: '🎧', screen: '🖥️', speaker: '🔊' };
const FX = { volume: 1, pan: 0, mono: false };
const $ = s => document.querySelector(s);
const el = (tag, props = {}, ...kids) => {
  const e = Object.assign(document.createElement(tag), props);
  e.append(...kids.filter(k => k !== '' && k != null));
  return e;
};

const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
const host = (() => { try { return new URL(tab.url).host; } catch { return ''; } })();
let devices = [], routes = [], busy = false, lastHere = [];

function status(msg = '', isErr = false) {
  $('#status').textContent = msg;
  $('#status').classList.toggle('err', isErr);
}

function friendly(e) {
  const m = String(e?.message || e);
  if (/active stream/i.test(m)) return 'Otra extensión o app ya está capturando esta pestaña.';
  if (/chrome:\/\/|Chrome pages|webstore|Cannot capture/i.test(m)) return 'Chrome no deja capturar esta página (chrome://, Web Store).';
  if (/not been invoked|activeTab/i.test(m)) return 'Cierra y vuelve a abrir la extensión en esta pestaña.';
  return m;
}

async function beep(deviceId) {
  const ctx = new AudioContext();
  try {
    await ctx.setSinkId(deviceId);
    const osc = ctx.createOscillator(), gain = ctx.createGain();
    osc.frequency.value = 660;
    gain.gain.setValueAtTime(0.0001, ctx.currentTime);
    gain.gain.exponentialRampToValueAtTime(0.3, ctx.currentTime + 0.02);
    gain.gain.exponentialRampToValueAtTime(0.0001, ctx.currentTime + 0.35);
    osc.connect(gain).connect(ctx.destination);
    osc.start();
    osc.stop(ctx.currentTime + 0.4);
    await new Promise(res => setTimeout(res, 450));
  } catch { status('No se pudo sonar en ese dispositivo.', true); }
  finally { ctx.close(); }
}

const same = (a, b) => a.deviceId === b.deviceId || (a.label && a.label === b.label);
const mine = () => routes.find(r => r.tabId === tab.id);

function setBusy(b) {
  busy = b;
  document.querySelectorAll('.pick').forEach(x => { x.disabled = b; });
}

async function setWants(wants, okMsg) {
  if (busy) return;
  setBusy(true);
  status(wants.length ? 'Conectando…' : 'Restaurando…');
  try {
    if (!wants.length) {
      await call('unroute', { tabId: tab.id });
    } else {
      // A tab can only be captured once; already-routed tabs just switch sinks.
      const streamId = mine() ? undefined : await chrome.tabCapture.getMediaStreamId({ targetTabId: tab.id });
      await call('route', { tabId: tab.id, title: tab.title, streamId, wants });
      if (host) chrome.storage.local.set({ ['site:' + host]: wants });
    }
    status(okMsg);
    routes = await call('list');
  } catch (e) {
    status(friendly(e), true);
  } finally {
    setBusy(false);
    render();
  }
}

function pick(d) {
  if (!d.deviceId) return setWants([], 'Suena como siempre.');
  const want = { deviceId: d.deviceId, label: d.label };
  if (!$('#multi').checked) return setWants([want], `Sonando en ${d.label}.`);
  const cur = mine()?.outputs.map(({ deviceId, label }) => ({ deviceId, label })) ?? [];
  const has = cur.some(o => same(o, d));
  const next = has ? cur.filter(o => !same(o, d)) : [...cur, want];
  setWants(next, next.length ? `Sonando en ${next.map(o => o.label).join(' + ')}.` : 'Suena como siempre.');
}

function deviceCard(d, { on, warn, sub }) {
  const icon = d.deviceId ? ICON[deviceKind(d.label)] : '💻';
  const multi = $('#multi').checked && d.deviceId;
  return el('li', { className: 'dev' + (warn ? ' warn' : on ? ' on' : '') },
    el('button', {
      className: 'pick', disabled: busy, onclick: () => pick(d),
      ...(multi ? { role: 'checkbox', ariaChecked: String(on) } : { ariaPressed: String(on) }),
    },
      el('span', { className: 'ico', ariaHidden: 'true', textContent: icon }),
      el('span', { className: 'name' }, d.label, sub ? el('small', { textContent: sub }) : ''),
      on || multi ? el('span', { className: 'check', ariaHidden: 'true', textContent: on ? '✓' : '' }) : ''),
    d.deviceId && !warn
      ? el('button', { className: 'test', textContent: '▶', title: 'Probar sonido', ariaLabel: `Probar ${d.label}`, onclick: () => beep(d.deviceId) })
      : '');
}

const lostText = o => o.muted ? '⚠ Desconectado · silenciado' : '⚠ Desconectado · sonando en predeterminado';
const panText = v => v == 0 ? 'Centro' : v < 0 ? `Izq ${-v}` : `Der ${v}`;

function renderFx(fx) {
  const v = Math.round(fx.volume * 100), p = Math.round(fx.pan * 100);
  const active = document.activeElement;
  if (active !== $('#vol')) $('#vol').value = v;
  if (active !== $('#pan')) $('#pan').value = p;
  $('#mono').checked = fx.mono;
  $('#volval').textContent = `${$('#vol').value}%`;
  $('#volval').classList.toggle('boost', $('#vol').value > 100);
  $('#panval').textContent = panText($('#pan').value);
}

function render() {
  const m = mine();
  $('#devices').classList.toggle('multi', $('#multi').checked);

  const cards = [deviceCard({ deviceId: '', label: 'Predeterminado del sistema' },
    { on: !m, sub: 'Como suena normalmente' })];
  // A wanted device that's gone still gets a card so the user sees what happened.
  const missing = m?.outputs.filter(o => !devices.some(d => same(d, o))) ?? [];
  for (const d of [...devices, ...missing]) {
    const out = m?.outputs.find(o => same(o, d));
    const warn = !!out?.fallback;
    const sub = warn ? lostText(out)
      : !m && lastHere.some(w => w.label === d.label) ? 'Último usado en este sitio' : '';
    cards.push(deviceCard(d, { on: !!out, warn, sub }));
  }
  $('#devices').replaceChildren(...cards);

  $('#fx').style.display = m ? 'block' : 'none';
  if (m) renderFx(m.fx);

  $('#hint').textContent = m ? '' : tab.audible ? 'Sonando ahora' : 'Sin audio ahora (puedes enrutarla igual)';

  const others = routes.filter(r => r.tabId !== tab.id);
  $('#others').replaceChildren(...(others.length ? others.map(r => {
    const lost = r.outputs.some(o => o.fallback);
    return el('li', {},
      el('span', { className: 'name' }, r.title || `Pestaña ${r.tabId}`,
        el('small', {
          className: lost ? 'warnTxt' : '',
          textContent: r.outputs.map(o => `${o.fallback ? '⚠' : ICON[deviceKind(o.label)]} ${o.label}`).join(' + '),
        })),
      el('button', { className: 'mini', textContent: 'Ir', ariaLabel: `Ir a ${r.title}`, onclick: () => goTo(r.tabId) }),
      el('button', { className: 'mini', textContent: '✕', ariaLabel: `Restaurar ${r.title}`,
        onclick: () => call('unroute', { tabId: r.tabId }).then(refresh, e => status(friendly(e), true)) }));
  }) : [el('li', { className: 'empty', textContent: 'Ninguna' })]));
}

async function goTo(tabId) {
  const t = await chrome.tabs.update(tabId, { active: true });
  await chrome.windows.update(t.windowId, { focused: true });
  window.close();
}

async function loadDevices() {
  const raw = await navigator.mediaDevices.enumerateDevices();
  devices = listOutputs(raw);
  $('#perm').style.display = raw.some(d => d.kind === 'audiooutput') && raw.every(d => !d.label) ? 'block' : 'none';
}

async function refresh() {
  routes = await call('list');
  render();
}

function sendFx(values) {
  const m = mine();
  if (!m) return;
  Object.assign(m.fx, values);
  renderFx(m.fx);
  call('fx', { tabId: tab.id, ...values }).catch(e => status(friendly(e), true));
}

$('#vol').oninput = () => sendFx({ volume: $('#vol').value / 100 });
$('#pan').oninput = () => sendFx({ pan: $('#pan').value / 100 });
$('#mono').onchange = () => sendFx({ mono: $('#mono').checked });
$('#fxreset').onclick = () => sendFx({ ...FX });
$('#multi').onchange = () => { chrome.storage.local.set({ multi: $('#multi').checked }); render(); };
$('#grant').onclick = () => chrome.tabs.create({ url: 'setup.html' });
$('#onLost').onchange = () => {
  chrome.storage.local.set({ onLost: $('#onLost').value });
  call('policy', { mute: $('#onLost').value === 'mute' }).catch(e => status(friendly(e), true));
};
navigator.mediaDevices.addEventListener('devicechange', () => loadDevices().then(render).catch(() => {}));
// Live updates from the engine (BT drops while popup is open, tab closed, etc.).
chrome.runtime.onMessage.addListener(msg => {
  if (msg.type === 'routes' && !busy) { routes = msg.routes; render(); }
});

async function renderKeyHint() {
  const cmd = (await chrome.commands.getAll()).find(c => c.name === 'next-output');
  const link = el('button', { className: 'link', textContent: cmd?.shortcut ? 'cambiar' : 'asignar atajo',
    onclick: () => chrome.tabs.create({ url: 'chrome://extensions/shortcuts' }) });
  $('#keyhint').replaceChildren(...(cmd?.shortcut
    ? ['Atajo ', el('kbd', { textContent: cmd.shortcut }), ' cambia de salida · ', link]
    : ['Sin atajo de teclado · ', link]));
}

$('#tab').textContent = tab.title || 'Pestaña actual';
try {
  const stored = await chrome.storage.local.get(['onLost', 'multi', host && 'site:' + host].filter(Boolean));
  $('#onLost').value = stored.onLost || 'default';
  $('#multi').checked = !!stored.multi;
  const last = stored['site:' + host];
  lastHere = Array.isArray(last) ? last : last ? [last] : []; // v1.1 stored a single object
  renderKeyHint().catch(() => {});
  const r = await chrome.runtime.sendMessage({ target: 'bg', type: 'ensure' });
  if (!r?.ok) throw new Error(r?.error || 'No arrancó el motor de audio');
  await loadDevices();
  await refresh();
} catch (e) {
  status(friendly(e), true);
  render();
}
