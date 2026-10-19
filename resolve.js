// Device IDs can change (Bluetooth re-pair, cleared site data) but labels survive,
// so match by ID first, then by label, else fall back to the system default.
export function resolveSink(devices, want) {
  if (!want.deviceId || want.deviceId === 'default') return { deviceId: '', fallback: false };
  const outs = listOutputs(devices);
  const hit = outs.find(d => d.deviceId === want.deviceId) || (want.label && outs.find(d => d.label === want.label));
  return hit ? { deviceId: hit.deviceId, fallback: false } : { deviceId: '', fallback: true };
}

// One plan entry per wanted output. A lost output falls back to default only when nothing
// else is audible and the user didn't ask to mute; otherwise it goes silent, so the default
// device never doubles up or leaks headphone audio to the speakers.
export function planSinks(devices, wants, muteOnLost) {
  const plan = wants.map(want => ({ want, ...resolveSink(devices, want), muted: false }));
  const anyLive = plan.some(p => !p.fallback);
  const seen = new Set();
  let firstLost = true;
  for (const p of plan) {
    if (p.fallback) {
      p.muted = muteOnLost || anyLive || !firstLost;
      firstLost = false;
    } else if (seen.has(p.deviceId)) {
      p.muted = true; // two wants resolved to the same device
    }
    if (!p.muted) seen.add(p.deviceId);
  }
  return plan;
}

// Shortcut cycle: default -> dev1 -> dev2 -> ... -> default. null means "back to default".
export function nextOutput(devices, current) {
  if (!current) return devices[0] ?? null;
  const i = devices.findIndex(d => d.deviceId === current.deviceId || d.label === current.label);
  return devices[i + 1] ?? null;
}

// Chrome adds 'default' and 'communications' aliases that duplicate real devices.
export const listOutputs = devices =>
  devices.filter(d => d.kind === 'audiooutput' && d.deviceId !== 'default' && d.deviceId !== 'communications');

// ponytail: label heuristic, only drives the icon; wrong guess costs nothing.
export function deviceKind(label = '') {
  const l = label.toLowerCase();
  if (/hdmi|displayport|monitor|nvidia|\btv\b|pantalla|display/.test(l)) return 'screen';
  if (/aud[ií]fono|auricular|headphone|headset|earbud|buds|airpods|wh-|wf-|hands-free|manos libres/.test(l)) return 'headphones';
  return 'speaker';
}
