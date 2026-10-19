import { call } from './rpc.js';
import { nextOutput } from './resolve.js';

// Keeps exactly one offscreen document alive (it owns the streams), paints the badge,
// and handles the keyboard shortcut.
let creating = null;

async function ensureOffscreen() {
  if (await chrome.offscreen.hasDocument()) return;
  creating ??= (async () => {
    // Fresh document = every old capture is dead; wipe stale badges.
    await paint([]);
    await chrome.offscreen.createDocument({
      url: 'offscreen.html',
      reasons: ['USER_MEDIA'],
      justification: 'Reproducir el audio capturado de cada pestaña en el dispositivo elegido',
    });
    const { onLost } = await chrome.storage.local.get('onLost');
    await call('policy', { mute: onLost === 'mute' }).catch(() => {});
  })().finally(() => { creating = null; });
  await creating;
}

function badge(tabId, r) {
  const lost = r?.outputs.some(o => o.fallback);
  chrome.action.setBadgeText({ tabId, text: r ? (lost ? '!' : '♪') : '' }).catch(() => {});
  chrome.action.setTitle({ tabId, title: r ? `Earmark: ${r.outputs.map(o => o.label).join(' + ')}` : 'Earmark' }).catch(() => {});
  if (r) chrome.action.setBadgeBackgroundColor({ tabId, color: lost ? '#d97706' : '#2563eb' }).catch(() => {});
}

async function paint(routes) {
  const { routes: old = [] } = await chrome.storage.session.get('routes');
  await chrome.storage.session.set({ routes });
  old.filter(o => !routes.some(r => r.tabId === o.tabId)).forEach(o => badge(o.tabId, null));
  routes.forEach(r => badge(r.tabId, r));
}

function flashError(tabId, title) {
  chrome.action.setBadgeBackgroundColor({ tabId, color: '#dc2626' }).catch(() => {});
  chrome.action.setBadgeText({ tabId, text: '×' }).catch(() => {});
  chrome.action.setTitle({ tabId, title: `Earmark: ${title}` }).catch(() => {});
  setTimeout(async () => {
    const { routes = [] } = await chrome.storage.session.get('routes');
    badge(tabId, routes.find(r => r.tabId === tabId) || null);
  }, 2500);
}

// Alt+Shift+A: default -> dev1 -> dev2 -> ... -> default, on the active tab.
async function cycle(tab) {
  await ensureOffscreen();
  const [devices, routes] = await Promise.all([call('devices'), call('list')]);
  const cur = routes.find(r => r.tabId === tab.id);
  const next = nextOutput(devices, cur?.outputs[0]);
  if (!next) return cur && call('unroute', { tabId: tab.id });
  // The shortcut counts as invoking the extension, so capture is allowed here.
  const streamId = cur ? undefined : await chrome.tabCapture.getMediaStreamId({ targetTabId: tab.id });
  const want = { deviceId: next.deviceId, label: next.label };
  await call('route', { tabId: tab.id, title: tab.title, streamId, wants: [want] });
  try { chrome.storage.local.set({ ['site:' + new URL(tab.url).host]: [want] }); } catch {}
}

chrome.commands.onCommand.addListener((cmd, tab) => {
  if (cmd !== 'next-output' || !tab?.id) return;
  cycle(tab).catch(e => flashError(tab.id, String(e?.message || e)));
});

// Tab-specific badges reset on navigation; repaint.
chrome.tabs.onUpdated.addListener(async (tabId, info) => {
  if (info.status !== 'loading') return;
  const { routes = [] } = await chrome.storage.session.get('routes');
  const r = routes.find(x => x.tabId === tabId);
  if (r) badge(tabId, r);
});

chrome.runtime.onMessage.addListener((msg, _sender, reply) => {
  if (msg.target !== 'bg') return;
  if (msg.type === 'routes') { paint(msg.routes); return; }
  if (msg.type !== 'ensure') return;
  ensureOffscreen().then(() => reply({ ok: true }), e => reply({ ok: false, error: String(e?.message || e) }));
  return true;
});
