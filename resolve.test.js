import test from 'node:test';
import assert from 'node:assert/strict';
import { resolveSink, listOutputs, deviceKind } from './resolve.js';

const devs = [
  { kind: 'audioinput', deviceId: 'mic', label: 'Mic' },
  { kind: 'audiooutput', deviceId: 'default', label: 'Predeterminado' },
  { kind: 'audiooutput', deviceId: 'communications', label: 'Comunicaciones' },
  { kind: 'audiooutput', deviceId: 'bt2', label: 'Sony WH-1000XM5' },
  { kind: 'audiooutput', deviceId: 'spk', label: 'Altavoces Realtek' },
];

test('match por id', () => assert.deepEqual(resolveSink(devs, { deviceId: 'spk', label: 'x' }), { deviceId: 'spk', fallback: false }));
test('id cambió (BT re-emparejado), match por label', () =>
  assert.deepEqual(resolveSink(devs, { deviceId: 'bt1', label: 'Sony WH-1000XM5' }), { deviceId: 'bt2', fallback: false }));
test('dispositivo desconectado -> default + fallback', () =>
  assert.deepEqual(resolveSink(devs, { deviceId: 'gone', label: 'JBL' }), { deviceId: '', fallback: true }));
test('nunca matchea un input', () => assert.equal(resolveSink(devs, { deviceId: 'mic', label: 'Mic' }).fallback, true));
test('nunca matchea un alias de Chrome por label', () =>
  assert.equal(resolveSink(devs, { deviceId: 'x', label: 'Predeterminado' }).fallback, true));
test('label vacío no matchea outputs sin label', () =>
  assert.equal(resolveSink([{ kind: 'audiooutput', deviceId: 'a', label: '' }], { deviceId: 'z', label: '' }).fallback, true));
test('default pedido explícito', () => assert.deepEqual(resolveSink([], { deviceId: 'default' }), { deviceId: '', fallback: false }));
test('listOutputs quita inputs y alias', () => assert.deepEqual(listOutputs(devs).map(d => d.deviceId), ['bt2', 'spk']));
test('deviceKind', () => {
  assert.equal(deviceKind('Sony WH-1000XM5'), 'headphones');
  assert.equal(deviceKind('Auriculares (Realtek Audio)'), 'headphones');
  assert.equal(deviceKind('LG ULTRAGEAR (NVIDIA High Definition Audio)'), 'screen');
  assert.equal(deviceKind('Altavoces (Realtek Audio)'), 'speaker');
  assert.equal(deviceKind(), 'speaker');
});

import { planSinks, nextOutput } from './resolve.js';
const outs = listOutputs(devs);
const sony = { deviceId: 'bt2', label: 'Sony WH-1000XM5' }, spk = { deviceId: 'spk', label: 'Altavoces Realtek' };
const jbl = { deviceId: 'jbl', label: 'JBL Flip' }, jbl2 = { deviceId: 'jbl2', label: 'JBL Go' };
const pick = plan => plan.map(p => [p.deviceId, p.muted]);

test('plan: varias salidas vivas suenan todas', () =>
  assert.deepEqual(pick(planSinks(devs, [sony, spk], false)), [['bt2', false], ['spk', false]]));
test('plan: una se cae y otra sigue -> la caída se silencia (no duplica en default)', () =>
  assert.deepEqual(pick(planSinks(devs, [jbl, spk], false)), [['', true], ['spk', false]]));
test('plan: todas caídas -> sólo una suena en default', () =>
  assert.deepEqual(pick(planSinks(devs, [jbl, jbl2], false)), [['', false], ['', true]]));
test('plan: todas caídas + silenciar -> nada suena', () =>
  assert.deepEqual(pick(planSinks(devs, [jbl], true)), [['', true]]));
test('plan: mismo dispositivo dos veces no duplica', () =>
  assert.deepEqual(pick(planSinks(devs, [sony, { deviceId: 'old', label: 'Sony WH-1000XM5' }], false)), [['bt2', false], ['bt2', true]]));
test('atajo: ciclo default -> 1 -> 2 -> default', () => {
  assert.equal(nextOutput(outs, null).deviceId, 'bt2');
  assert.equal(nextOutput(outs, sony).deviceId, 'spk');
  assert.equal(nextOutput(outs, spk), null);
  assert.equal(nextOutput(outs, jbl).deviceId, 'bt2'); // actual desconocido -> primero
  assert.equal(nextOutput([], null), null);
});
