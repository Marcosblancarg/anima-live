# Verificación · Anima Live 0.6

[← Volver al inicio](../README.md)

## Pruebas automáticas

Resultado de la versión distribuida: **PASS**. Se ejercita el motor e inspeccionan sus muestras sin abrir salidas físicas con la opción `--offline`.

| Área | Cobertura |
|---|---|
| Pads | WAV/MP3, tres modos, cortes, ganancia, velocity, mezcla, redisparo y protección de salida. |
| MIDI y bancos | Note On/Off, velocity cero, canal, CC, aprendizaje, cuatro páginas, liberaciones entre páginas, Pad Focus y formatos de banco anteriores. |
| Música | Búfer acotado, archivo mono de 16 minutos convertido a estéreo de 48 kHz, precarga, avance, pausa, búsqueda y archivos faltantes. |
| Transiciones | Crossfade lineal de 250 ms, envolvente L/R, repetición y parada al terminar. |
| Listas | M3U8/PLS, Unicode, rutas relativas, M3U Windows-1252 y nombres que empiezan por `#`. |
| Mixer | Suma de buses, faders independientes, MUTE, medidores, pisado de −12 dB y detención total. |
| Interfaz | Tres pestañas, reloj, MIDI fuera de Pads y distribución de 640 × 480 a 1280 × 680, con espacio reservado para el marco de Windows. |

El [informe de la versión](resultado-pruebas.txt) conserva la salida de las comprobaciones. La prueba de limpieza del búfer incluye una vista `float[]` respaldada por almacenamiento de bytes para detectar la regresión que producía distorsión en versiones anteriores.

### Repetir las pruebas

Desde la raíz del repositorio, en Windows con SDK .NET 10:

```powershell
.\scripts\build.ps1 -Test
```

También podés ejecutar el binario ya compilado:

```powershell
$taskReport = Join-Path $PWD 'resultado-pruebas.txt'
$taskProcess = Start-Process -FilePath '.\artifacts\windows-x64\AnimaLivePad.exe' `
    -ArgumentList @('--self-test', ('"{0}"' -f $taskReport), '--offline') `
    -WindowStyle Hidden -Wait -PassThru
Get-Content -LiteralPath $taskReport -Encoding UTF8
$taskProcess.ExitCode
```

Se espera código de salida **0**. Mantener `--offline` evita abrir o modificar dispositivos de audio durante la prueba.

## Reproducción por salida virtual

Se utilizó **CABLE Input (VB-Audio Virtual Cable)** con captura WASAPI loopback a 48 kHz, estéreo y 32 bits float. La señal de referencia fue una senoide de 440 Hz con pico 0,08. No se cambió el dispositivo predeterminado de Windows ni se abrió la salida Audient.

| Medida | Música con repetición y crossfade | Preescucha independiente |
|---|---:|---:|
| Duración aproximada de captura | 6,8 s | 3,7 s |
| Pico capturado | 0,080000 | 0,056000 |
| Discontinuidades detectadas | **0** | **0** |
| Error relativo de continuidad sinusoidal | −142,58 dB | −140,50 dB |
| Error del dispositivo | Ninguno | Ninguno |
| Mezcla principal durante Cue | — | **Silencio** |

En música se repitió un WAV de tres segundos con solapamientos de medio segundo. La posición avanzó entre repeticiones y no se registraron faltas de datos en el búfer. En Cue se comprobó el nivel fijo 0,7 y que el medidor de emisión permaneciera en cero.

El detector compara las muestras con la recurrencia de una senoide de 440 Hz, omite los extremos de la señal y marca errores superiores a 0,015. Estos resultados comprueban ese recorrido digital; no son una medición de latencia total.

## Alcance pendiente

Faltan una prueba prolongada en el equipo de emisión, la comprobación con nanoPAD2 físico y el ruteo real de los auriculares de la Audient iD24. Las pruebas automáticas y la salida virtual no sustituyen esas comprobaciones.

Las [capturas](images/) muestran las tres vistas normales y compactas con datos de demostración generados por la app. No son mediciones de rendimiento ni una biblioteca de audio incluida.
