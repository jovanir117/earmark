# Historias de usuario

## HU-1 · Mandar una pestaña a un dispositivo
**Como** usuario con varias salidas de audio, **quiero** elegir a qué dispositivo va el audio de la pestaña actual, **para** escuchar cosas distintas en sitios distintos.
- Dado que abro el popup en una pestaña con audio, cuando elijo "Audífonos" y pulso *Enviar aquí*, el audio de esa pestaña suena sólo en los audífonos.
- Las demás pestañas siguen sonando en la salida predeterminada de Windows.

## HU-2 · Varias pestañas, varias salidas a la vez
**Como** usuario, **quiero** que la pestaña 1 suene en audífonos y la pestaña 2 en bocinas o un Bluetooth al mismo tiempo.
- Puedo enrutar N pestañas, cada una a su dispositivo, simultáneamente.
- El popup lista todas las pestañas enrutadas con su dispositivo.

## HU-3 · Cambiar de dispositivo sin cortar
**Como** usuario, **quiero** mover una pestaña ya enrutada a otro dispositivo.
- Elijo otro dispositivo y pulso *Enviar aquí*: cambia sin recargar la página ni reiniciar el video.

## HU-4 · Restaurar
**Como** usuario, **quiero** devolver una pestaña a su salida normal.
- *Restaurar* (o *Quitar* en la lista) detiene la captura y la pestaña vuelve a sonar como antes.

## HU-5 · Volumen por pestaña
**Como** usuario, **quiero** ajustar el volumen de cada pestaña enrutada sin tocar el volumen del sistema.

## HU-6 · Bluetooth que se desconecta (antifrágil)
**Como** usuario con audífonos Bluetooth, **quiero** que si se desconectan el audio no se pierda y que vuelva solo cuando se reconecten.
- Si el dispositivo desaparece, la pestaña sigue sonando en la salida predeterminada y el popup muestra ⚠.
- Cuando el dispositivo reaparece (aunque Windows le cambie el ID), la pestaña regresa sola a él.

## HU-7 · Pestaña cerrada
**Como** usuario, **quiero** que al cerrar una pestaña enrutada se libere todo.
- La ruta desaparece de la lista; no quedan capturas colgadas.

## HU-8 · Nombres reales de dispositivos
**Como** usuario, **quiero** ver "Sony WH-1000XM5" y no un ID raro.
- Si Chrome oculta los nombres, el popup ofrece *Dar permiso* una sola vez; el micrófono no se graba.

## HU-10 · Un clic
**Como** usuario, **quiero** cambiar la salida con un solo clic sobre el dispositivo, sin botones extra.
- Clic en un dispositivo = la pestaña suena ahí. Clic en *Predeterminado del sistema* = restaurar.
- El dispositivo activo se ve marcado (✓ y borde de color).

## HU-11 · ¿Cuál es cuál?
**Como** usuario con nombres raros de dispositivos, **quiero** un botón ▶ que suene un tono de prueba en cada uno.

## HU-12 · Saber de un vistazo
**Como** usuario, **quiero** ver en el ícono de la extensión si la pestaña está enrutada (♪) o si su dispositivo se cayó (!).

## HU-13 · Privacidad al desconectar
**Como** usuario con audífonos, **quiero** poder elegir que el audio se silencie si se desconectan, en vez de salir por las bocinas.

## HU-14 · Recordar por sitio
**Como** usuario, **quiero** que la extensión me marque el dispositivo que usé la última vez en ese sitio.

## HU-15 · Saltar entre pestañas enrutadas
**Como** usuario, **quiero** ver las otras pestañas enrutadas, ir a ellas (*Ir*) o restaurarlas (✕) desde el popup.

## HU-16 · Una pestaña en varias salidas
**Como** usuario, **quiero** que una pestaña suene en varios dispositivos a la vez (audífonos + bocinas, dos Bluetooth).
- Con *Varias a la vez* activo, cada clic agrega o quita el dispositivo.
- Si uno se desconecta y otro sigue, el caído se silencia (no duplica en predeterminado).
- Si se desconectan todos, suena uno en predeterminado (o nada, si elegí *Silenciar*).

## HU-17 · Volumen extra, balance y mono
**Como** usuario, **quiero** subir una pestaña bajita hasta 200% sin que distorsione, moverla a un oído y ponerla en mono.
- *Restablecer* regresa todo a 100%, centro, estéreo.

## HU-18 · Atajo de teclado
**Como** usuario, **quiero** cambiar la salida de la pestaña actual con `Alt+Shift+A` sin abrir el popup.
- Cicla predeterminado → dispositivo 1 → 2 → … → predeterminado.
- El tooltip del ícono dice dónde suena; si falla, el ícono muestra × en rojo unos segundos.

## HU-9 · Páginas que no se pueden capturar
**Como** usuario, **quiero** un mensaje claro si Chrome no permite capturar la página (chrome://, Web Store).

## HU-19 · Cada app de Windows a su salida
**Como** gamer, **quiero** mandar el juego a los audífonos y Chrome a las bocinas desde una ventana sencilla.
- Veo cada app que usa audio con su ícono, nombre y si está sonando.
- Elijo la salida en un menú; el cambio aplica al instante y Windows lo recuerda.
- Si el dispositivo recordado no está conectado, la fila lo marca ⚠ y suena en predeterminado.
- Apps nuevas aparecen solas al empezar a sonar.
- *Restablecer todo* (con confirmación) regresa todas al predeterminado.

## HU-20 · Desde la bandeja, sin abrir nada
**Como** gamer, **quiero** un ícono junto al reloj que con clic derecho me muestre cada app, a dónde suena y me deje cambiarla ahí mismo.
- Cada app aparece como `App → Dispositivo`; ● si está sonando.
- El submenú marca con ✓ la salida actual.
- Cerrar la ventana deja el ícono vivo; *Salir* lo termina.
- Abrir el exe otra vez no duplica: trae la ventana existente.
- *Iniciar con Windows* arranca solo el ícono.
- En reposo no consume CPU ni memoria apreciable.

## HU-21 · Sin desfase entre imagen y sonido
**Como** usuario viendo video con el audio enrutado a otro dispositivo, **quiero** que el sonido vaya a tiempo con la imagen.
- Cada salida reproduce directo en su dispositivo (`AudioContext.setSinkId`), sin el búfer de un `<audio>` intermedio.
- Límite: la latencia propia de los audífonos Bluetooth (~150-250 ms) no se puede compensar en una pestaña capturada. Para eso, enruta Chrome completo con la app de Windows: ahí Chrome sí sincroniza el video.
