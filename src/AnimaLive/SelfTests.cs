using System.IO;
using NAudio.Wave;
using System.Windows.Input;
namespace src;

public static class SelfTests
{
    static void TestByteBackedAudioBuffer()
    {
        using var engine = new AudioEngine { Master = 1 };
        var provider = new NAudio.Wave.SampleProviders.SampleToWaveProvider(engine);
        // Exercise the SAME byte-backed float view used by WASAPI. A plain float[] misses this bug.
        var bytes = Enumerable.Repeat((byte)0x55, 8192).ToArray();
        provider.Read(bytes, 32, 4096);
        Assert(bytes.Skip(32).Take(4096).All(b => b == 0), "El búfer byte[] de WASAPI debe quedar completamente en silencio");
        Assert(bytes.Take(32).All(b => b == 0x55), "Se debe respetar el offset de audio");
        var data = new float[96000 * 2];
        for (int frame = 0; frame < data.Length / 2; frame++) data[frame * 2] = data[frame * 2 + 1] = (float)(.08 * Math.Sin(2 * Math.PI * 440 * frame / 48000));
        engine.Press(0, new Pad { Samples = data, End = 2 });
        int samplesRead = 0;
        for (int block = 0; block < 120; block++)
        {
            int byteCount = block % 2 == 0 ? 4096 : 2048;
            provider.Read(bytes, 32, byteCount);
            for (int j = 0; j < byteCount / 4; j++)
            {
                float actual = BitConverter.ToSingle(bytes, 32 + j * 4);
                int p = samplesRead + j;
                float fade = Math.Min(1f, Math.Min(p / 192f, (data.Length - p - 1) / 192f));
                Assert(Math.Abs(actual - data[p] * fade) < 0.000001, "No debe acumular audio anterior ni introducir discontinuidades entre buffers");
            }
            samplesRead += byteCount / 4;
        }
        engine.StopAll(); provider.Read(bytes, 32, 4096);
        Assert(bytes.Skip(32).Take(4096).All(b => b == 0), "Stop debe devolver silencio incluso cuando se reutiliza el búfer de bytes");
        Assert(TrimRange.TryParse("0,25", "1,75", 2, out var start, out var end) && start == .25 && end == 1.75, "Cortes con coma decimal");
        Assert(!TrimRange.TryParse("2", "1", 2, out _, out _), "Rechazar límites invertidos");
        Assert(!TrimRange.TryParse("NaN", "1", 2, out _, out _), "Rechazar límites no finitos");
        var bounds = TrimRange.Move(.25, 1.75, 2, 2, false);
        Assert(bounds.Start < bounds.End && Math.Abs(bounds.End - bounds.Start - TrimRange.Frame) < 1e-10, "Arrastrar inicio no puede cruzar el final");
        bounds = TrimRange.Move(.25, 1.75, 2, 0, true);
        Assert(bounds.End > bounds.Start, "Arrastrar fin no puede cruzar el inicio");
        double shortDuration = 1001 / 48000.0;
        Assert(TrimRange.TryParse("0", shortDuration.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture), shortDuration, out _, out end) && Math.Abs(end - shortDuration) < 1e-9, "El final del archivo debe sobrevivir al redondeo del editor");
        Assert(new AudioOptions { BufferFrames = 4096 }.LatencyMs == 86, "Conversión de muestras a latencia solicitada");
    }
    static void TestMeter()
    {
        using var engine = new AudioEngine { Master = .5f };
        var data = new float[96000];
        for (int i = 0; i < data.Length; i += 2) { data[i] = .5f; data[i + 1] = .25f; }
        var pad = new Pad { Samples = data, End = 1 };
        var buffer = new float[1000];
        engine.Press(0, pad); engine.Read(buffer, 10, 960); engine.TakeMeterReading();
        engine.Read(buffer, 10, 960); var reading = engine.TakeMeterReading();
        Assert(Math.Abs(reading.Left - .25) < .0001 && Math.Abs(reading.Right - .125) < .0001, "Medidor estéreo después del master");
        Assert(Math.Abs(reading.RmsLeft - .25) < .0001 && Math.Abs(reading.RmsRight - .125) < .0001, "RMS independiente por canal");
        Assert(Math.Abs(OutputMeter.ToDb(reading.Left) + 12.0412) < .001, "Escala dBFS");
        Assert(engine.TakeMeterReading().Left == 0, "Lectura consume el intervalo del medidor");
        engine.Read(buffer, 0, 960); engine.StopAll(); engine.Read(buffer, 0, 960);
        Assert(engine.TakeMeterReading().Left > 0, "Un bloque silencioso no debe borrar un pico entre refrescos");
        engine.Master = 1; engine.Press(0, pad); engine.Press(48, pad); engine.Press(63, pad); engine.Read(buffer, 0, 960);
        reading = engine.TakeMeterReading();
        Assert(reading.Clipped && reading.Left == 1 && Math.Abs(reading.Right - .75) < .001, "Medidor de mezcla de todas las páginas y detección de saturación");
        Assert(buffer.All(x => x <= 1 && x >= -1), "Medición no altera la protección existente");
        engine.StopAll(); engine.Read(buffer, 0, 960); reading = engine.TakeMeterReading();
        Assert(reading.Left == 0 && reading.Right == 0 && !reading.Clipped && OutputMeter.ToDb(0) == -60, "Medidor en silencio");
        engine.Master = 0; engine.Press(0, pad); engine.Read(buffer, 0, 960);
        Assert(engine.TakeMeterReading().Left == 0, "Master en cero se refleja en el medidor");
    }
    public static string HardwareReport = "";
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    static Pad Tone(TriggerMode mode = TriggerMode.OneShot) => new() { Samples = Enumerable.Repeat(0.5f, 96000).ToArray(), End = 1, Mode = mode };
    public static void Run(bool checkHardware = true)
    {
        using var engine = new AudioEngine { Master = 1 }; var p = Tone(); var output = new float[960];
        engine.Press(0, p); engine.Release(0, p); engine.Read(output, 0, 960); Assert(engine.Position(0) > 0, "One Shot debe continuar al soltar"); Assert(Math.Abs(output[300] - 0.5) < 0.001, "Ganancia unitaria"); engine.StopAll();
        p = Tone(TriggerMode.Normal); engine.Press(0, p); engine.Release(0, p); engine.Read(output, 0, 960); Assert(output.All(x => x == 0), "Normal debe detenerse al soltar");
        p = Tone(TriggerMode.Alternar); engine.Press(0, p); engine.Release(0, p); Assert(engine.Position(0) >= 0, "Alternar debe continuar"); engine.Press(0, p); Assert(engine.Position(0) < 0, "Alternar debe detenerse en segunda pulsación");
        p = Tone(); p.Start = 0.2; p.End = 0.3; engine.Press(0, p); Assert(Math.Abs(engine.Position(0) - 0.2) < 0.0001, "Inicio del recorte"); engine.Read(new float[9600], 0, 9600); Assert(engine.Position(0) < 0, "Fin exacto del recorte");
        p = Tone(); p.GainDb = -6.0206; engine.Press(0, p); engine.Read(output, 0, 960); Assert(Math.Abs(output[300] - 0.25) < 0.001, "Ganancia -6 dB"); engine.StopAll();
        p = Tone(); p.Velocity = true; engine.Press(0, p, 64); engine.Read(output, 0, 960); Assert(Math.Abs(output[300] - 0.5 * 64 / 127) < 0.001, "Velocity"); engine.StopAll();
        p = Tone(); engine.Press(0, p); engine.Press(1, p); engine.Read(output, 0, 960); Assert(Math.Abs(output[300] - 1) < 0.001, "Mezcla simultánea"); engine.Press(2, p); engine.Read(output, 0, 960); Assert(output.All(x => x <= 1 && x >= -1), "Protección de salida"); engine.StopAll();
        engine.Press(0, p); engine.Read(output, 0, 960); engine.Press(0, p); Assert(engine.Position(0) == 0, "Redisparo desde inicio"); engine.StopAll();
        var dir = Path.Combine(Path.GetTempPath(), "airepads-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            MusicTests.Run(dir).GetAwaiter().GetResult();
            var wav = Path.Combine(dir, "tone.wav"); using (var writer = new WaveFileWriter(wav, new WaveFormat(44100, 16, 1))) { for (int i = 0; i < 4410; i++) writer.WriteSample((float)Math.Sin(i * 0.1) * 0.25f); }
            var decoded = Decoder.Read(wav); Assert(decoded.Length > 9000 && decoded.Length < 10000, "Conversión mono 44.1 a estéreo 48 kHz");
            var mp3 = Path.Combine(dir, "tone.mp3"); using (var input = new WaveFileReader(wav)) MediaFoundationEncoder.EncodeToMp3(input, mp3);
            MusicTests.CheckMp3(mp3).GetAwaiter().GetResult();
            var mp3Data = Decoder.Read(mp3); Assert(mp3Data.Length > 0 && mp3Data.Any(x => Math.Abs(x) > 0.01), "Decodificación MP3");
            var pads = Enumerable.Range(0, 16).Select(i => new Pad { Note = 36 + i }).ToArray(); pads[0] = new Pad { Name = "Prueba ñ", FileName = "tone.wav", Samples = decoded, Original = File.ReadAllBytes(wav), End = decoded.Length / 96000.0, Mode = TriggerMode.Normal, Channel = 10, Note = 48, GainDb = -3 };
            var bank = Path.Combine(dir, "test.airepads"); BankFile.Save(bank, pads); var read = BankFile.Load(bank); Assert(read[0].Name == "Prueba ñ" && read[0].Samples.Length == decoded.Length && read[0].Original.SequenceEqual(pads[0].Original) && read[0].Mode == TriggerMode.Normal && read[0].Channel == 10, "Banco con audio y configuración");
            Assert(read.Length == 64 && read.Skip(16).All(p => p.Samples.Length == 0), "Migración del banco anterior a cuatro páginas");
            var all = PadBank.Create(); all[0] = pads[0]; all[63] = new Pad { Name = "Último pad", FileName = "tone.wav", Samples = decoded, Original = File.ReadAllBytes(wav), End = decoded.Length / 96000.0, Note = 99, Mode = TriggerMode.Alternar };
            var four = Path.Combine(dir, "cuatro.anima"); BankFile.Save(four, all); var restored = BankFile.Load(four);
            Assert(restored.Length == 64 && restored[63].Name == "Último pad" && restored[63].Original.SequenceEqual(all[63].Original) && restored[63].Mode == TriggerMode.Alternar && restored[0].Name == all[0].Name, "Guardado y recuperación de las cuatro páginas");
        }
        finally { foreach (var f in Directory.GetFiles(dir)) File.Delete(f); Directory.Delete(dir); }
        TestMeter();
        TestByteBackedAudioBuffer();
        var window = new MainWindow(); window.TestMidi();
        if (!checkHardware) { HardwareReport = "Pruebas offline: no se abrió ni modificó ningún dispositivo de audio."; return; }
        try { using var live = new AudioEngine(); live.Open(); System.Threading.Thread.Sleep(120); HardwareReport = "WASAPI: salida abierta correctamente (silencio). MIDI disponibles: " + NAudio.Midi.MidiIn.NumberOfDevices; } catch (Exception ex) { HardwareReport = "Hardware sin verificar: " + ex.Message; }
    }
}
public partial class MainWindow
{
    internal void TestMidi()
    {
        pads[0] = new Pad { Samples = Enumerable.Repeat(0.25f, 96000).ToArray(), End = 1, Mode = TriggerMode.Normal, Note = 36 };
        HandleMidi(0x90 | (36 << 8) | (100 << 16)); if (engine.Position(0) < 0) throw new Exception("MIDI Note On");
        HandleMidi(0x90 | (36 << 8)); if (engine.Position(0) >= 0) throw new Exception("MIDI velocidad cero = Note Off");
        HandleMidi(0x90 | (36 << 8) | (100 << 16)); HandleMidi(0x80 | (36 << 8)); if (engine.Position(0) >= 0) throw new Exception("MIDI Note Off");
        pads[0].Channel = 2; HandleMidi(0x90 | (36 << 8) | (100 << 16)); if (engine.Position(0) >= 0) throw new Exception("Filtro de canal MIDI"); HandleMidi(0x80 | (36 << 8));
        pads[0].IsCC = true; HandleMidi(0xB1 | (36 << 8) | (127 << 16)); if (engine.Position(0) < 0) throw new Exception("MIDI CC On"); HandleMidi(0xB1 | (36 << 8)); if (engine.Position(0) >= 0) throw new Exception("MIDI CC Off");
        learn = 0; HandleMidi(0x99 | (60 << 8) | (127 << 16)); if (pads[0].Note != 60 || pads[0].Channel != 10 || pads[0].IsCC || engine.Position(0) >= 0) throw new Exception("MIDI Learn");
        TestPages(); TestEditorFocusAndTrim(); TestResponsiveLayout(); dirty = false; engine.Dispose();
    }
    void TestEditorFocusAndTrim()
    {
        pads = PadBank.Create(); heldMidi.Clear(); engine.StopAll(); SwitchPage(0);
        PadFocus.IsChecked = true; HandleMidi(0x90 | (37 << 8) | (100 << 16));
        if (selected != 1 || engine.Position(1) >= 0 || midiFlashUntil[1] <= System.Diagnostics.Stopwatch.GetTimestamp()) throw new Exception("Pad Focus debe seleccionar e iluminar un pad MIDI vacío");
        GainSlider.Value = -4; if (pads[1].GainDb != -4) throw new Exception("Se debe poder editar la ganancia del pad vacío enfocado");
        HandleMidi(0x80 | (37 << 8));
        PadFocus.IsChecked = false; SelectPad(0); HandleMidi(0x90 | (38 << 8) | (100 << 16));
        if (selected != 0) throw new Exception("Pad Focus desactivado debe preservar la selección");
        HandleMidi(0x80 | (38 << 8));
        UpdateResponsiveLayout(1120, 650);
        SetEditorVisible(false);
        if (EditorPanel.Visibility != System.Windows.Visibility.Collapsed || EditorColumn.Width.Value != 0 || MixMeter.Visibility != System.Windows.Visibility.Visible || MinWidth != 640) throw new Exception("Editor colapsable sin ocultar vúmetro");
        SetEditorVisible(true);
        if (EditorColumn.Width.Value != 280 || EditorPanel.Visibility != System.Windows.Visibility.Visible) throw new Exception("Restaurar editor");
        pads[0].Samples = new float[192000]; pads[0].End = 2; ShowPad();
        StartBox.Text = "0,25"; EndBox.Text = "1,75";
        if (!CommitTrim(true) || pads[0].Start != .25 || pads[0].End != 1.75) throw new Exception("Aplicar cortes desde el editor");
        StartBox.Text = "0.5"; EndBox.Text = "1.5"; SelectPad(1);
        if (pads[0].Start != .5 || pads[0].End != 1.5 || pads[1].Start != 0) throw new Exception("Cambiar pad debe aplicar el corte al pad anterior");
        SelectPad(0); StartBox.Text = "1.9"; EndBox.Text = "1";
        if (CommitTrim(false) || pads[0].Start != .5 || pads[0].End != 1.5) throw new Exception("Corte inválido no debe alterar el pad");
        ShowPad(); Waveform.Measure(new System.Windows.Size(200, 100)); Waveform.Arrange(new System.Windows.Rect(0, 0, 200, 100));
        trimDrag = 0; MoveTrim(50); trimDrag = 1; MoveTrim(150); trimDrag = -1;
        if (pads[0].Start != .5 || pads[0].End != 1.5 || Waveform.Background == null) throw new Exception("Arrastre de cortes y clic en zonas vacías");
        PadFocus.IsChecked = true;
    }
    void TestPages()
    {
        pads = PadBank.Create(); heldMidi.Clear(); heldKeys.Clear(); engine.StopAll();
        for (int n = 0; n < 4; n++) { var p = pads[n * 16]; p.Samples = Enumerable.Repeat(.25f, 96000).ToArray(); p.End = 1; p.Mode = TriggerMode.Normal; }
        SwitchPage(1); PressKey(Key.D1); SwitchPage(0); ReleaseKey(Key.D1);
        if (engine.Position(16) >= 0) throw new Exception("Liberación de teclado tras cambiar página");
        pads[0].Mode = TriggerMode.OneShot; engine.Press(0, pads[0]); SwitchPage(3);
        if (engine.Position(0) < 0) throw new Exception("Cambiar página no debe cortar One Shot"); engine.StopAll();
        FollowMidi.IsChecked = true; HandleMidi(0x90 | (52 << 8) | (100 << 16));
        if (page != 1 || engine.Position(16) < 0 || engine.Position(0) >= 0) throw new Exception("Escena MIDI 2 debe disparar página 2");
        SwitchPage(0); HandleMidi(0x80 | (52 << 8)); if (engine.Position(16) >= 0) throw new Exception("Note Off debe liberar el pad de su página original");
        foreach (int target in new[] { 0, 2, 3 })
        {
            int note = 36 + target * 16; HandleMidi(0x90 | (note << 8) | (100 << 16));
            if (page != target || engine.Position(target * 16) < 0) throw new Exception("Seguimiento de las cuatro escenas MIDI");
            HandleMidi(0x80 | (note << 8));
        }
        engine.StopAll(); SwitchPage(0); FollowMidi.IsChecked = false;
        HandleMidi(0x90 | (52 << 8) | (100 << 16)); if (page != 0 || engine.Position(16) >= 0) throw new Exception("MIDI sin seguimiento solo usa página actual"); HandleMidi(0x80 | (52 << 8)); FollowMidi.IsChecked = true;
        pads[16].Note = 36; SwitchPage(1); HandleMidi(0x90 | (36 << 8) | (100 << 16));
        if (engine.Position(16) < 0 || engine.Position(0) >= 0) throw new Exception("Asignaciones compartidas deben priorizar la página actual");
        HandleMidi(0x80 | (36 << 8)); engine.StopAll(); SwitchPage(0);
    }
    internal void MakeDemo()
    {
        string[] labels = { "Apertura", "Cortina informativa", "Aplausos", "Separador", "Último momento", "Risa", "Suspenso", "Campana", "Entrevista", "Transición", "Cuenta regresiva", "Efecto espacial", "Cierre", "Ambiente", "Golpe", "Identificación" };
        string[] colors = { "#9B3DFF", "#7751F5", "#397CF5", "#19B9EA" };
        for (int i = 0; i < 16; i++) { pads[i].Name = labels[i]; pads[i].Color = colors[i % 4]; pads[i].Mode = (TriggerMode)(i % 3); pads[i].Samples = Enumerable.Range(0, 96000).Select(j => (float)(Math.Sin(j * 0.06) * Math.Exp(-j / 24000.0) * 0.8)).ToArray(); pads[i].End = 1; pads[i].FileName = labels[i] + ".wav"; }
        refreshing = true; MidiDevices.Items.Add("Controlador MIDI"); MidiDevices.SelectedIndex = 0; AudioDevices.Items.Add("Salida predeterminada"); AudioDevices.SelectedIndex = 0; refreshing = false;
        ShowPad();
    }
}

