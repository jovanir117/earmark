# Audio Router

Extensión de Chrome/Edge (MV3) que manda el audio de **cada pestaña a una salida distinta**: pestaña 1 a los audífonos, pestaña 2 a las bocinas, pestaña 3 a un Bluetooth, todo al mismo tiempo.

## Instalar
1. `chrome://extensions` (o `edge://extensions`), activa **Modo desarrollador**.
2. **Cargar descomprimida** y elige esta carpeta.
3. Fija el ícono. La primera vez pulsa **Dar permiso** para que aparezcan los nombres de los dispositivos.

## Uso
Abre la pestaña con audio, clic en el ícono, **clic en el dispositivo**. Listo. **Predeterminado del sistema** la devuelve a la normalidad.

- **▶** junto a cada dispositivo suena un tono de prueba ahí, para saber cuál es cuál.
- El ícono de la extensión muestra **♪** en pestañas enrutadas y **!** si su dispositivo se desconectó.
- Recuerda el último dispositivo que usaste en cada sitio y te lo marca.
- **Varias a la vez**: activa el switch y marca varios dispositivos; la pestaña suena en todos.
- **Volumen hasta 200%**, **balance** izquierda/derecha y **mono**. Un limitador evita que distorsione al subirle.
- **Atajo `Alt+Shift+A`**: cambia la pestaña actual a la siguiente salida (predeterminado → 1 → 2 → … → predeterminado) sin abrir el popup. Se cambia en `chrome://extensions/shortcuts`.
- Abajo eliges qué pasa al desconectarse: seguir en predeterminado o silenciar (para que lo de los audífonos no salga por las bocinas).

## Cómo funciona
- `chrome.tabCapture` captura el audio de la pestaña (Chrome la silencia en su salida original).
- Un documento offscreen lo reproduce con `HTMLMediaElement.setSinkId(dispositivo)`.
- En `devicechange` re-resuelve cada ruta: si el dispositivo se va, cae a la salida predeterminada; si vuelve, regresa solo. El emparejamiento es por ID y, si el ID cambió, por nombre (`resolve.js`).
- Si se cierra la pestaña, el track termina y la ruta se limpia sola.

## Pruebas
```
npm test
```

## Límites
- Añade algo de latencia (~decenas de ms); en video puede notarse un desfase mínimo de labios.
- No funciona en `chrome://` ni en la Chrome Web Store (Chrome lo prohíbe).
- Si reinicias el navegador, las rutas se pierden (las capturas mueren con él).

## Apps de Windows (juegos, Discord, Spotify…)
`windows/AudioRouterApps.exe`: ventana con cada app que está usando audio y un menú para elegir su salida. Ejemplo: juego → audífonos, Chrome → bocinas.

- **Vive junto al reloj.** Clic derecho en el ícono 🔊: cada app que suena, a dónde va (`Chrome → STEREN`) y un submenú para cambiarla ahí mismo. Clic izquierdo abre la ventana.
- Cerrar la ventana no la cierra: sigue en la bandeja. **Salir** está en el menú.
- **Iniciar con Windows** en el menú (arranca oculta, solo el ícono).
- En reposo: ~1 MB en el Administrador de tareas, ~0% CPU; no hace polling, solo lee el audio al abrir el menú.
- Si algo falla queda registro en `%TEMP%\AudioRouterApps.log`.
- Compilar: `windows\build.cmd` (usa el compilador de C# que ya trae Windows, sin instalar nada).
- La app aparece cuando empieza a sonar; la lista se actualiza sola cada 2 s.
- Windows recuerda la elección por app, aunque cierres esta ventana.
- **Restablecer todo** regresa todas las apps al predeterminado.
- Diagnóstico sin cambiar nada: `AudioRouterApps.exe --dump > dump.txt`.
- Usa la misma API interna (no documentada) que el Mezclador de Windows y EarTrumpet; si una actualización de Windows la rompe, la ventana lo avisa y queda el botón al Mezclador.
- Juegos con su propio selector de dispositivo de audio: ese manda; cámbialo dentro del juego.
