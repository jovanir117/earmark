document.querySelector('#go').onclick = async () => {
  const msg = document.querySelector('#msg');
  try {
    const s = await navigator.mediaDevices.getUserMedia({ audio: true });
    s.getTracks().forEach(t => t.stop());
    msg.textContent = 'Listo. Cierra esta pestaña y vuelve a abrir la extensión.';
  } catch (e) {
    msg.textContent = `No se pudo (${e.name}). Revisa el permiso de micrófono en chrome://settings/content/microphone.`;
  }
};
