using System.IO;
using NAudio.Wave;

namespace src;

public static class MusicTests
{
    public static async Task CheckMp3(string path)
    {
        var item = MusicItem.Probe(path);
        using var deck = new MusicDeck { CrossfadeSeconds = 0 };
        deck.SetQueue(new[] { item }); await deck.PlayAsync(item).ConfigureAwait(false);
        var buffer = new float[9600]; deck.Read(buffer, 0, buffer.Length);
        Check(buffer.Any(x => Math.Abs(x) > .01), "MP3 reproducido desde el decodificador de música en segundo plano");
    }
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static async Task WaitNext(MusicDeck deck)
    {
        for (int n = 0; n < 500 && !deck.NextReady; n++) await Task.Delay(10).ConfigureAwait(false);
        Check(deck.NextReady, "Precarga de la siguiente canción");
    }
    static MusicItem Write(string path, float value, double seconds = 1)
    {
        using (var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)))
        {
            var block = Enumerable.Repeat(value, 960).ToArray();
            for (int n = 0; n < seconds * 100; n++) writer.WriteSamples(block, 0, block.Length);
        }
        return MusicItem.Probe(path);
    }
    public static async Task Run(string folder)
    {
        var one = Write(Path.Combine(folder, "Cortina ñ.wav"), .1f);
        var two = Write(Path.Combine(folder, "Tema dos.wav"), .3f);
        Check(one.Length == "0:01", "Length de los audios");
        var list = Path.Combine(folder, "música.m3u8");
        MusicFiles.SavePlaylist(list, new[] { one, two });
        Check(MusicFiles.ReadPlaylist(list).SequenceEqual(new[] { one.Path, two.Path }), "M3U8 con Unicode y rutas relativas");
        var ansi = Path.Combine(folder, "antigua.m3u");
        File.WriteAllText(ansi, "Cortina ñ.wav\n", System.Text.Encoding.GetEncoding(1252));
        Check(MusicFiles.ReadPlaylist(ansi).Single() == one.Path, "M3U antiguo con caracteres de Windows-1252");
        var hashItem = new MusicItem { Path = Path.Combine(folder, "#Indicativo.wav"), Title = "#Indicativo", Duration = 1 };
        var hashList = Path.Combine(folder, "numeral.m3u8"); MusicFiles.SavePlaylist(hashList, new[] { hashItem });
        Check(MusicFiles.ReadPlaylist(hashList).Single() == hashItem.Path, "Nombre de archivo con numeral no se confunde con comentario M3U");
        File.WriteAllText(Path.Combine(folder, "orden.pls"), "[playlist]\nFile2=Tema dos.wav\nFile1=Cortina ñ.wav\nNumberOfEntries=2\nVersion=2\n");
        Check(MusicFiles.ReadPlaylist(Path.Combine(folder, "orden.pls")).SequenceEqual(new[] { one.Path, two.Path }), "PLS ordenado por número");
        using (var deck = new MusicDeck { CrossfadeSeconds = 0 })
        {
            deck.SetQueue(new[] { one, two }); await deck.PlayAsync(one).ConfigureAwait(false); await WaitNext(deck).ConfigureAwait(false);
            var output = new float[192000]; deck.Read(output, 0, output.Length);
            Check(output.Take(96000).All(x => Math.Abs(x - .1f) < .000001) && output.Skip(96000).All(x => Math.Abs(x - .3f) < .000001), "Avance automático sin hueco entre temas y sin acumular audio anterior");
            deck.Read(output, 0, 960); Check(output.Take(960).All(x => x == 0) && !deck.State.Playing, "Fin de lista devuelve silencio");
            await deck.PlayAsync(one).ConfigureAwait(false); deck.Read(output, 0, 9600); double position = deck.State.Position;
            deck.TogglePause(); deck.Read(output, 0, 960); Check(deck.State.Position == position && output.Take(960).All(x => x == 0), "Pausa conserva la posición");
            await deck.SeekAsync(.5).ConfigureAwait(false); Check(Math.Abs(deck.State.Position - .5) < .001 && deck.State.Paused, "Buscar posición conserva pausa");
            deck.TogglePause(); deck.Read(output, 0, 960); Check(output.Take(960).All(x => Math.Abs(x - .1f) < .000001), "Reanudar música");
            deck.StopAfterCurrent = true; await deck.PlayAsync(one).ConfigureAwait(false); await WaitNext(deck).ConfigureAwait(false); deck.Read(output, 0, output.Length);
            Check(output.Skip(96000).All(x => x == 0), "Parar al terminar no dispara el siguiente");
            deck.StopAfterCurrent = false; deck.Repeat = true; deck.SetQueue(new[] { one }); await deck.PlayAsync(one).ConfigureAwait(false); await WaitNext(deck).ConfigureAwait(false);
            deck.Read(output, 0, 144000); Check(output.Take(144000).All(x => Math.Abs(x - .1f) < .000001), "Repetir lista de una cortina");
            deck.Stop(); deck.Read(output, 0, 960); Check(output.Take(960).All(x => x == 0), "Stop invalida todas las precargas");
        }
        using (var deck = new MusicDeck { CrossfadeSeconds = .25 })
        {
            deck.SetQueue(new[] { one, two }); await deck.PlayAsync(one).ConfigureAwait(false); await WaitNext(deck).ConfigureAwait(false);
            var output = new float[192000]; deck.Read(output, 0, output.Length);
            Check(Math.Abs(output[80000] - .1666667) < .0001 && output.Take(168000).All(x => x >= .09999 && x <= .30001), "Crossfade lineal de 250 ms");
            Check(output.Skip(168000).All(x => x == 0), "Duración total descuenta el solapamiento");
            for (int j = 0; j < 168000; j += 2) Check(output[j] == output[j + 1], "Crossfade estéreo con la misma envolvente L/R");
        }
        using (var deck = new MusicDeck { CrossfadeSeconds = 0 })
        {
            var missing = new MusicItem { Path = Path.Combine(folder, "falta.wav"), Title = "Falta", Duration = 1 };
            deck.SetQueue(new[] { one, missing, two }); await deck.PlayAsync(one).ConfigureAwait(false); await WaitNext(deck).ConfigureAwait(false);
            var output = new float[192000]; deck.Read(output, 0, output.Length);
            Check(Math.Abs(output[100000] - .3f) < .000001 && deck.State.Notice.Contains("omitió"), "Saltar próximo archivo faltante sin detener la lista");
        }
        string longPath = Path.Combine(folder, "Programa largo.wav");
        using (var writer = new WaveFileWriter(longPath, new WaveFormat(8000, 16, 1))) { var block = new byte[16000]; for (int n = 0; n < 16 * 60; n++) writer.Write(block, 0, block.Length); }
        using (var track = new BufferedTrack(longPath)) { await track.Ready.ConfigureAwait(false); Check(track.Duration >= 959 && track.BufferedSamples <= 48000 * 2 * 8, "Música de más de 15 minutos con búfer de tamaño fijo"); }
        using (var engine = new AudioEngine { Master = 1 })
        {
            engine.Music.CrossfadeSeconds = 0; engine.Music.SetQueue(new[] { two }); await engine.Music.PlayAsync(two).ConfigureAwait(false);
            var pad = new Pad { Samples = Enumerable.Repeat(.2f, 96000).ToArray(), End = 1 };
            engine.Press(0, pad); var output = new float[9600]; engine.Read(output, 0, output.Length);
            Check(Math.Abs(output[1000] - .5f) < .000001, "Pads y música se mezclan en una sola salida");
            var pm = engine.TakePadMeterReading(); var mm = engine.TakeMusicMeterReading();
            Check(Math.Abs(pm.Left - .2) < .0001 && Math.Abs(mm.Left - .3) < .0001, "Vúmetros de canales independientes antes del master");
            engine.PadMuted = true; engine.Read(output, 0, output.Length); Check(Math.Abs(output[^1] - .3f) < .000001, "Mute pads conserva la música");
            engine.PadMuted = false; engine.MusicMuted = true; engine.Read(output, 0, output.Length); Check(Math.Abs(output[^1] - .2f) < .000001, "Mute música conserva los pads");
            engine.MusicMuted = false; engine.MusicGain = .5f; engine.Read(output, 0, output.Length); Check(Math.Abs(output[^1] - .35f) < .000001, "Fader de música independiente");
            engine.MusicGain = 1; engine.DuckMusic = true; engine.Read(output, 0, output.Length); Check(Math.Abs(output[^1] - (.2 + .3 * .25118864)) < .00001, "Pisador de música de menos 12 dB");
            engine.StopAll(); engine.Read(output, 0, output.Length); Check(output.All(x => x == 0), "Detener todo silencia pads y música");
        }
    }
}
