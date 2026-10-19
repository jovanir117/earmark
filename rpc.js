// Message the offscreen engine. Retry covers it still booting right after createDocument.
export async function call(type, extra = {}) {
  for (let i = 0; ; i++) {
    try {
      const r = await chrome.runtime.sendMessage({ target: 'offscreen', type, ...extra });
      if (r?.ok) return r.v;
      throw new Error(r?.error || 'Sin respuesta');
    } catch (e) {
      if (i >= 2 || !/Receiving end|Sin respuesta/.test(e.message)) throw e;
      await new Promise(res => setTimeout(res, 150));
    }
  }
}
