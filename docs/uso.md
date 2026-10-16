# Guía de uso · Anima Live 0.6

[← Volver al inicio](../README.md)

## Abrir y elegir la salida

Extraé el ZIP completo y abrí `AnimaLivePad-v06/app/AnimaLivePad.exe`. La app incluye .NET y funciona sin Internet. La carpeta debe permitir escritura para recordar tu configuración.

Elegí el dispositivo en **SALIDA**. La app usa WASAPI compartido: el audio convive con otros programas de Windows. En ventanas pequeñas, **☰** despliega los controles de audio, bancos y dispositivos.

Las pestañas **Pads / F5**, **Reproductor / F6** y **Mixer / F7** cambian de vista sin detener el audio. El reloj usa la hora local de Windows. MIDI sigue activo en las tres vistas y en segundo plano.

## Cargar y disparar pads

Arrastrá un WAV o MP3 desde Windows sobre un pad. Al arrastrar varios archivos, se distribuyen desde ese pad hacia adelante. También podés usar **Cargar audio** dentro del editor.

- **Clic izquierdo:** selecciona y dispara.
- **Clic derecho:** selecciona para editar sin disparar.
- **Teclado:** `1 2 3 4`, `Q W E R`, `A S D F`, `Z X C V` corresponden a las cuatro filas.
- **MIDI:** dispara según la asignación de nota o CC y canal.

Los atajos de pads funcionan en la pestaña Pads y se suspenden al escribir en campos. Al perder el foco de la ventana, se liberan los pads Normal iniciados por teclado; One Shot y Alternar continúan.

### Tres modos de disparo

| Modo | Al pulsar | Al soltar |
|---|---|---|
| **One Shot** | Reproduce el recorte completo. Otra pulsación reinicia. | Continúa hasta el final. |
| **Normal** | Reproduce mientras mantenés pulsado. | Se detiene. |
| **Alternar** | Primera pulsación reproduce; la siguiente detiene. | Continúa. |

Cada pad tiene una voz. El redisparo reinicia esa voz; no acumula copias del mismo sonido. Los modos Normal y Alternar terminan al llegar al final y no hacen bucle. Pueden sonar pads de distintas páginas a la vez.

## Sample / editor

Seleccioná el pad y desplegá **Sample / editor**. En la vista compacta, este control alterna entre la botonera y el editor; el master y su vúmetro siguen visibles.

- Cambiá **nombre**, **color**, **modo** y **ganancia de −48 a +12 dB**.
- La ganancia también se aplica mientras suena el pad.
- **MIDI, color y archivos** abre las opciones adicionales de aprendizaje, archivo y bloqueo.
- **Vaciar** retira el audio del pad seleccionado.
- **Bloquear edición para el vivo** impide modificar o reemplazar pads, conservando el control de reproducción.

### Inicio y fin del sample

Arrastrá los marcadores **IN / OUT** de la forma de onda. Un clic fija el inicio; **Shift + clic** fija el fin. La forma de onda representa el archivo completo.

También podés escribir los tiempos en segundos usando coma o punto decimal. **Enter**, **Aplicar cortes** o salir de ambos campos aplica los límites. Los cortes conservan el archivo original; se usan microfundidos de 2 ms para reducir clics en sus extremos.

### Pad Focus

Con **Pad Focus** activado, tocar un pad MIDI lo selecciona en el editor. Los pads vacíos también se iluminan y seleccionan para identificar su posición y prepararlos. El seguimiento de página se controla aparte con **Seguir página por MIDI**.

## Controladores MIDI y cuatro páginas

1. Conectá el controlador por USB y elegilo en **MIDI**.
2. Seleccioná un pad con clic derecho.
3. Pulsá **Aprender** y tocá el botón físico. El aprendizaje no reproduce audio.
4. Repetí para los demás pads y páginas; guardá el banco.

**↻** actualiza los dispositivos, detiene los sonidos y reconecta la salida. Usalo en una pausa del programa.

El aprendizaje guarda nota o CC y canal. Una misma asignación en varios pads de la página hace que suenen juntos. La casilla de intensidad MIDI permite que velocity controle el volumen; desactivala para un nivel uniforme.

**Normal necesita liberación:** Note Off, Note On con velocity cero, o CC momentáneo. Para CC, 64–127 presiona y 0–63 suelta. No se implementan aftertouch, pitch bend ni MIDI 2.0.

### nanoPAD2: cuatro escenas, cuatro bancos

Los botones **01–04** y **F1–F4** cambian entre las cuatro páginas de 16 pads. Cambiar de página conserva la reproducción y la liberación de los pads Normal de la página anterior.

Para distinguir las escenas del nanoPAD2, asignales **notas o canales diferentes**. Con **Seguir página por MIDI**, la app busca la asignación en las cuatro páginas y muestra la correspondiente al tocar un pad. Si el mensaje está repetido, prioriza la página visible; si no coincide con ella, usa la primera que coincida. Mientras escribís en un campo, se dispara el audio sin cambiar la página que editás.

Las asignaciones iniciales de Anima Live son:

| Página | Notas MIDI | Canal |
|---|---|---|
| 1 | 36–51 | Todos |
| 2 | 52–67 | Todos |
| 3 | 68–83 | Todos |
| 4 | 84–99 | Todos |

Estas asignaciones no presuponen la configuración de fábrica del controlador. Podés adaptarlo con su editor o aprender cada pad desde la app. La pantalla sigue la escena **al tocar un pad asignado**; pulsar solamente SCENE no la cambia. No hay sincronización SysEx del botón SCENE.

Con seguimiento desactivado, el MIDI dispara únicamente los pads de la página visible. Un indicador ▶ en la página avisa que tiene audio sonando.

## Guardar bancos de pads

**Guardar banco** crea un archivo `.anima` con las cuatro páginas, asignaciones, cortes, ganancias, colores y audios originales. Podés moverlo a otra PC. El archivo se reemplaza cuando el nuevo banco está completo.

**Abrir banco** carga los 64 pads. Si falla un audio, se conserva el banco anterior. Los bancos antiguos de 16 pads `.airepads` se importan en la primera página y dejan vacías las otras tres.

Los bancos se guardan manualmente. La app avisa cuando hay cambios pendientes.

## Reproductor: Audio Explorer

**Carpeta…** elige la carpeta inicial; doble clic abre una subcarpeta y **↑** sube un nivel. Seleccioná uno o varios audios o listas y pulsá **Añadir selección →**. El doble clic sobre un audio lo añade a la cola.

**Listas** muestra listas recientes y las de la carpeta actual. Se importan **M3U, M3U8 y PLS** con rutas locales, Unicode y M3U antiguos en Windows-1252. Se exporta M3U8 con rutas relativas cuando es posible.

Las listas contienen referencias: sus audios deben permanecer disponibles en disco. En ventanas pequeñas, **☰ Explorer** alterna con Main Music; **← Main Music** vuelve a la cola.

## Reproductor: Main Music

La cola muestra **Title** y **Length**. El título es el nombre del archivo. La fila ▶ identifica el tema actual; arriba aparecen posición, duración, tiempo restante y próximo tema precargado.

| Control | Acción |
|---|---|
| **＋ Audio** | Añade archivos o listas. También admite arrastrar y soltar a la cola. |
| **Play / doble clic** | Reproduce la fila seleccionada. |
| **Pausa / Espacio** | Pausa o reanuda conservando la posición. |
| **Stop** | Detiene solamente la música. |
| **▶\|** | Salta al siguiente tema. |
| **↑ / ↓** | Reordena el tema seleccionado. |
| **Quitar** | Retira las filas seleccionadas. Si una ya suena, deja que termine. |
| **Abrir lista** | Lee la nueva lista y luego reemplaza la cola y detiene la música actual. |
| **Guardar** | Exporta la cola como M3U8. |

Arrastrar una carpeta añade los audios que contiene directamente, sin recorrer todas sus subcarpetas. La barra de posición permite buscar dentro del tema; con el foco en ella, las flechas mueven cinco segundos y Home/End llevan al inicio/final.

**Repetir lista** mantiene la cortina o música de espera. **Parar al terminar** tiene prioridad sobre la repetición. En **⚙ Crossfade / Cue** elegís un solapamiento de 0, 1, 2, 3 o 5 segundos.

La música se decodifica en segundo plano con ocho segundos de búfer por fuente y precarga del siguiente tema. Admite archivos de más de 15 minutos. Si un próximo archivo desapareció o no puede abrirse, se omite y se informa en la barra de estado.

## Preescucha / Cue

En **⚙ Crossfade / Cue**, elegí una salida de Windows diferente de la emisión y pulsá **Preescuchar** para oír el tema seleccionado. **■ Cue** la detiene. Su nivel fijo es el 70 % de la señal original y no pasa por el master.

Para usar auriculares de una interfaz como la Audient iD24, configurá sus rutas de audio y un dispositivo o par de salida independiente visible en Windows. La app impide usar para Cue el mismo dispositivo que figura en SALIDA. Si Windows expone un solo par, necesitás otra salida para separar la preescucha.

## Mixer y vúmetro master

**Pads** controla conjuntamente las cuatro páginas; **Música** controla el reproductor. Cada canal tiene fader de −60 a +6 dB, lectura numérica, MUTE y medidor estéreo de pico/RMS. −60 dB equivale a silencio. Doble clic devuelve el fader a 0 dB.

**Bajar música al disparar pads** reduce la música 12 dB mientras hay pads activos, con una transición suave, y recupera su nivel al terminar.

Los medidores de canal miden después de su fader y antes del master. El de la derecha mide la mezcla de pads y música después del master; Cue queda fuera.

- Barras **L / R** entre −60 y 0 dBFS; cian, amarillo desde −12 dBFS y rojo desde −3 dBFS.
- Pico, marca RMS, retención de pico de 1,5 segundos y lectura numérica.
- **CLIP** permanece tres segundos al alcanzar el máximo digital. Clic sobre el medidor borra la retención.
- Muestra pico de muestras y RMS; no mide LUFS, true peak ni el volumen físico de los auriculares.

**DETENER TODO / Esc** detiene pads, música y preescucha.

## Audio, búfer y formatos

**⚙ Audio** permite elegir el tamaño de búfer solicitado y sincronización por eventos o temporizada. Los tamaños van de 256 a 8192 frames. El motor trabaja a **48 kHz, estéreo**, mediante WASAPI compartido. El ajuste es una solicitud a Windows; no cambia el panel ASIO de la interfaz ni mide la latencia total.

Aplicar ajustes detiene los sonidos y reinicia la salida elegida. Para investigar cortes, probá un búfer mayor durante una pausa y verificá que el master no alcance CLIP.

WAV y MP3 están probados. AIFF y otros formatos dependen de los decodificadores de Windows/NAudio; no se garantiza cualquier códec. Los pads admiten hasta **15 minutos y 256 MB de archivo por pad**, y **1 GB de PCM por banco**. El consumo total también incluye originales y memoria temporal de carga. El reproductor musical usa búferes acotados y no tiene ese límite de 15 minutos.

## Tamaño de ventana y configuración

La ventana abre en 1120 × 650, se adapta al área disponible y puede reducirse hasta 640 × 480. El master y su vúmetro permanecen visibles. En ventanas pequeñas se puede alternar el editor y el explorador para dejar espacio al trabajo principal.

Se recuerdan tamaño, última cola, carpeta, listas recientes, niveles, master, repetición, crossfade, pisado y salida Cue. La música queda detenida al abrir; emisión y MIDI se eligen por sesión.

Los archivos `window-settings.json`, `music-settings.json` y `audio-settings.json` se guardan junto al ejecutable. Los bancos de pads se guardan aparte mediante **Guardar banco**.

## Alcance de las comprobaciones

El [informe de pruebas](pruebas.md) detalla las regresiones y la captura de una salida virtual. La validación prolongada en el equipo de emisión, el nanoPAD2 físico y el ruteo físico de auriculares de la iD24 requieren una comprobación local.
