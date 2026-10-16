# Arquitectura · Anima Live 0.6

[← Volver al inicio](../README.md)

## Plataforma

Aplicación de escritorio **C# / WPF**, destinada a `net10.0-windows`. La publicación x64 incluye .NET y genera `AnimaLivePad.exe`. El acceso a audio y MIDI usa **NAudio 2.2.1**. No necesita navegador, servidor ni conexión a Internet para funcionar.

El proyecto conserva el namespace `src` de las versiones iniciales para mantener las referencias XAML y las clases parciales. El nombre del ensamblado es `AnimaLivePad`; la interfaz identifica el producto como Anima Live.

## Recorrido del audio

```text
Pad: archivo → PCM 48 kHz estéreo en RAM → IN/OUT → ganancia + velocity
                                                    ↓
                                         bus Pads: fader / mute → medidor
                                                    ↓
Música: archivo → decodificador → ring buffer → deck / crossfade
                                                    ↓
                               bus Música: fader / mute / pisado → medidor
                                                    ↓
                             suma Pads + Música → master → protección de picos
                                                    ↓
                                    medidor master → salida WASAPI compartida

Preescucha: archivo → deck independiente → ganancia 0,7 → otra salida WASAPI
```

Los medidores de canal son posteriores al fader y anteriores al master. El medidor master toma la mezcla final y conserva la indicación de saturación previa al recorte. La protección limita muestras a −1…+1; no es un limitador de sonoridad.

### Pads

`AudioEngine` implementa `ISampleProvider` y suma las voces activas. Hay un máximo de una voz por pad; una nueva pulsación reinicia esa voz o la detiene en modo Alternar. `Decoder` transforma mono/estéreo y remuestrea a 48 kHz. Los samples y los bytes originales se conservan en memoria.

Los cortes se expresan en segundos y se convierten a posiciones estéreo. Se aplican microfundidos de 2 ms. `TrimRange` valida y normaliza el intervalo. La lectura de audio y el control de voces usan sincronización para proteger el estado compartido.

El búfer de salida se limpia mediante escrituras tipadas `float`. Esto evita el fallo de limpieza parcial de `WaveBuffer` corregido en la 0.4, donde el arreglo podía tener un tipo de almacenamiento distinto y acumular muestras anteriores.

### Música

`BufferedTrack` alimenta en segundo plano un búfer circular de PCM de ocho segundos por fuente. `MusicDeck` administra reproducción, pausa, búsqueda, repetición, parada al terminar, precarga y transición al próximo tema. El consumo de PCM depende del búfer, no de la duración completa de la canción.

`MusicFiles` identifica archivos y lee/escribe listas. Las listas guardan rutas locales y los títulos se toman del nombre de archivo. No hay un índice de biblioteca ni una base de datos musical en esta versión.

`CuePlayer` usa un deck y una salida propios. No pasa por la suma ni los medidores de emisión.

## Interfaz y MIDI

`MainWindow.xaml` define las tres vistas. `MainWindow.xaml.cs` maneja pads, edición, banco, dispositivos y MIDI. `MusicWorkspace` contiene la interacción del explorador, cola, mixer y preescucha. `ResponsiveLayout` reorganiza los controles según el tamaño y guarda la geometría de ventana.

Los mensajes MIDI se asignan a nota o CC y canal. El seguimiento de página busca una asignación entre los 64 pads; Pad Focus actualiza la selección del editor. Las escenas del nanoPAD2 deben diferenciarse mediante las asignaciones. No se interpreta SysEx de SCENE.

La interfaz extrae lecturas acumuladas de los medidores para que los picos entre actualizaciones no se pierdan. `OutputMeter` dibuja barras, RMS, retenciones y CLIP.

## Persistencia

| Archivo | Contenido |
|---|---|
| `.anima` | ZIP con `bank.json` y audios originales; formato de 64 pads, versión de banco 2. |
| `.airepads` anterior | Banco de 16 pads, versión 1; se importa en la primera página. |
| `.m3u8` exportado | Cola con referencias a archivos, rutas relativas cuando es posible. |
| `window-settings.json` | Tamaño y opciones de ventana. |
| `music-settings.json` | Cola, carpeta, listas recientes, niveles y preferencias musicales. |
| `audio-settings.json` | Tamaño de búfer y sincronización WASAPI. |

Los JSON de preferencias se escriben junto al ejecutable. Los bancos se guardan mediante archivo temporal y reemplazo al finalizar. Su carga valida formato, límites y cortes antes de sustituir el banco activo. No se incluyen configuraciones personales ni audios del usuario en Git.

## Mapa del proyecto

Los archivos están en [`src/AnimaLive`](../src/AnimaLive).

| Archivo | Responsabilidad |
|---|---|
| `App.xaml.cs` | Inicio normal, modo de pruebas y generación de capturas de demostración. |
| `MainWindow.xaml` / `.xaml.cs` | Ventana, pads, editor, teclado, MIDI y bancos. |
| `AudioEngine.cs` | Modelos de pads, decodificación, mezcla, buses y salida. |
| `AudioOptions.cs` | Opciones, persistencia y diálogo de audio. |
| `TrimRange.cs` | Límites y validación del recorte. |
| `BankFile.cs` | Lectura y escritura de bancos con audio. |
| `BufferedTrack.cs` | Decodificación en segundo plano y búfer circular. |
| `MusicDeck.cs` | Transporte, precarga y crossfade. |
| `MusicFiles.cs` | Archivos, duración y listas M3U/M3U8/PLS. |
| `MusicWorkspace.cs` | Audio Explorer, cola, mixer y ajustes musicales. |
| `CuePlayer.cs` | Preescucha independiente. |
| `OutputMeter.cs` | Lecturas y visualización de vúmetros. |
| `ResponsiveLayout.cs` | Distribución adaptable y geometría de ventana. |
| `SelfTests.cs` / `MusicTests.cs` / `LayoutTests.cs` | Pruebas integradas. |

## Compilación y pruebas

El [script de compilación](../scripts/build.ps1) publica Windows x64 con el runtime y permite ejecutar las pruebas offline con `-Test`. La versión del SDK se resuelve con `global.json` dentro de .NET 10.

El ejecutable reconoce `--self-test RUTA_ABSOLUTA.txt --offline`; escribe el informe y devuelve código 0 cuando pasa. Ver [método y resultados](pruebas.md).
