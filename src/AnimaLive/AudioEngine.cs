using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.IO;
using System.Text.Json.Serialization;
namespace src;

public enum TriggerMode { OneShot, Normal, Alternar }
public class Pad
{
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#9B3DFF";
    public string FileName { get; set; } = "";
    public TriggerMode Mode { get; set; }
    public double GainDb { get; set; } = 0;
    public double Start { get; set; }
    public double End { get; set; }
    public int Note { get; set; } = 36;
    public int Channel { get; set; } = 0;
    public bool IsCC { get; set; }
    public bool Velocity { get; set; }
    [JsonIgnore] public float[] Samples { get; set; } = Array.Empty<float>();
    [JsonIgnore] public byte[] Original { get; set; } = Array.Empty<byte>();
    [JsonIgnore] public bool Loading { get; set; }
    [JsonIgnore] public double Duration => Samples.Length / 96000.0;
}
public static class PadBank
{
    public static Pad[] Create() => Enumerable.Range(0, 64).Select(i => new Pad { Note = 36 + i, Color = new[] { "#9B3DFF", "#7751F5", "#397CF5", "#19B9EA" }[i % 4] }).ToArray();
}
public static class Decoder
{
    public static float[] Read(string path)
    {
        using var reader = new AudioFileReader(path);
        ISampleProvider input = reader;
        if (input.WaveFormat.Channels == 1) input = new MonoToStereoSampleProvider(input);
        if (input.WaveFormat.Channels != 2) throw new InvalidDataException("Usá un audio mono o estéreo.");
        if (input.WaveFormat.SampleRate != 48000) input = new WdlResamplingSampleProvider(input, 48000);
        var data = new List<float>(); var block = new float[16384]; int n;
        while ((n = input.Read(block, 0, block.Length)) > 0)
        {
            if (data.Count + n > 96000L * 60 * 15) throw new InvalidDataException("Máximo 15 minutos por pad para limitar el uso de memoria.");
            for (int i = 0; i < n; i++) data.Add(block[i]);
        }
        if (data.Count < 2) throw new InvalidDataException("El archivo no contiene audio.");
        return data.ToArray();
    }
}
public sealed class AudioEngine : ISampleProvider, IDisposable
{
    sealed class Voice { public required Pad Pad; public required float[] Data; public int Position; public int Start; public int End; public float Velocity; }
    readonly object gate = new(); readonly Dictionary<int, Voice> voices = new();
    IWavePlayer? output;
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    public float Master = 0.7f; public float Peak;
    public readonly MusicDeck Music = new();
    public float PadGain = 1, MusicGain = 1;
    public bool PadMuted, MusicMuted, DuckMusic;
    float smoothPadGain = 1, smoothMusicGain = 1, duck = 1;
    float[] musicBuffer = new float[8192];
    readonly ChannelMeter padMeter = new(), musicMeter = new();
    public MeterReading TakePadMeterReading() => padMeter.Take();
    public MeterReading TakeMusicMeterReading() => musicMeter.Take();
    readonly object meterGate = new();
    float meterLeft, meterRight; double energyLeft, energyRight; long framesLeft, framesRight; bool meterClipped;
    public MeterReading TakeMeterReading()
    {
        lock (meterGate)
        {
            var reading = new MeterReading(meterLeft, meterRight,
                framesLeft > 0 ? Math.Sqrt(energyLeft / framesLeft) : 0,
                framesRight > 0 ? Math.Sqrt(energyRight / framesRight) : 0, meterClipped);
            meterLeft = meterRight = 0; energyLeft = energyRight = 0; framesLeft = framesRight = 0; meterClipped = false;
            return reading;
        }
    }
    public event Action<string>? OutputFailed;
    public string OutputDescription { get; private set; } = "WASAPI compartido · motor estéreo de 48 kHz";
    public void Open(string? deviceId = null, AudioOptions? options = null)
    {
        StopAll(); output?.Dispose(); output = null;
        using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
        using var device = deviceId == null ? enumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia) : enumerator.GetDevice(deviceId);
        options ??= new AudioOptions();
        if (!AudioOptions.AllowedFrames.Contains(options.BufferFrames)) throw new ArgumentOutOfRangeException(nameof(options));
        var next = new WasapiOut(device, NAudio.CoreAudioApi.AudioClientShareMode.Shared, options.EventSync, options.LatencyMs);
        next.PlaybackStopped += (_, e) => { if (e.Exception != null) OutputFailed?.Invoke(e.Exception.Message); };
        try { next.Init(this); next.Play(); output = next; OutputDescription = $"WASAPI compartido · {device.FriendlyName} · búfer solicitado {options.BufferFrames} muestras / {options.LatencyMs} ms · {(options.EventSync ? "eventos" : "temporizado")}"; } catch { next.Dispose(); throw; }
    }
    public void Press(int id, Pad pad, int velocity = 127)
    {
        lock (gate)
        {
            if (pad.Mode == TriggerMode.Alternar && voices.Remove(id)) return;
            if (pad.Samples.Length == 0 || pad.Loading) return;
            var start = Math.Clamp((int)(pad.Start * 48000) * 2, 0, pad.Samples.Length - 2);
            var end = Math.Clamp((int)(pad.End * 48000) * 2, start + 2, pad.Samples.Length);
            voices[id] = new Voice { Pad = pad, Data = pad.Samples, Start = start, Position = start, End = end, Velocity = pad.Velocity ? velocity / 127f : 1f };
        }
    }
    public void Release(int id, Pad pad) { if (pad.Mode == TriggerMode.Normal) Stop(id); }
    public void Stop(int id) { lock (gate) voices.Remove(id); }
    public void StopPads() { lock (gate) voices.Clear(); }
    public void StopAll() { StopPads(); Music.Stop(); }
    public double Position(int id) { lock (gate) return voices.TryGetValue(id, out var v) ? v.Position / 96000.0 : -1; }
    public void CopyPositions(double[] positions)
    {
        lock (gate) { Array.Fill(positions, -1); foreach (var pair in voices) if (pair.Key >= 0 && pair.Key < positions.Length) positions[pair.Key] = pair.Value.Position / 96000.0; }
    }
    public int Read(float[] buffer, int offset, int count)
    {
        // NAudio WaveBuffer can expose a byte[] through a float[] view.
        // Array.Clear uses the runtime element type and would clear only count BYTES.
        // Typed writes clear every float, preventing stale samples from accumulating.
        for (int clear = offset; clear < offset + count; clear++) buffer[clear] = 0f;
        bool padsActive;
        lock (gate)
        {
            padsActive = voices.Count > 0;
            foreach (var v in voices.Values)
            {
                int n = Math.Min(count, v.End - v.Position); float gain = (float)Math.Pow(10, v.Pad.GainDb / 20) * v.Velocity;
                for (int j = 0; j < n; j++)
                {
                    int p = v.Position + j;
                    // Microfundidos de 2 ms para reducir clics en cortes abruptos.
                    float fade = Math.Min(1f, Math.Min((p - v.Start) / 192f, (v.End - p - 1) / 192f));
                    buffer[offset + j] += v.Data[p] * gain * Math.Max(0, fade);
                }
                v.Position += n;
            }
            foreach (var key in voices.Where(x => x.Value.Position >= x.Value.End).Select(x => x.Key).ToArray()) voices.Remove(key);
        }
        if (musicBuffer.Length < count) musicBuffer = new float[count];
        Music.Read(musicBuffer, 0, count);
        float padTarget = PadMuted ? 0 : PadGain, musicTarget = MusicMuted ? 0 : MusicGain;
        float duckTarget = DuckMusic && padsActive ? .25118864f : 1f;
        float padL = 0, padR = 0, musicL = 0, musicR = 0;
        double padLE = 0, padRE = 0, musicLE = 0, musicRE = 0;
        float peak = 0, left = 0, right = 0; double leftEnergy = 0, rightEnergy = 0;
        float master = Master;
        for (int j = offset; j < offset + count; j++)
        {
            int channel = (j - offset) & 1;
            if (channel == 0)
            {
                smoothPadGain += Math.Clamp(padTarget - smoothPadGain, -.0042f, .0042f);
                smoothMusicGain += Math.Clamp(musicTarget - smoothMusicGain, -.0042f, .0042f);
                duck += Math.Clamp(duckTarget - duck, duckTarget < duck ? -.0007f : -.00009f, .00009f);
            }
            float padSample = buffer[j] * smoothPadGain;
            float musicSample = musicBuffer[j - offset] * smoothMusicGain * duck;
            if (channel == 0) { padL = Math.Max(padL, Math.Abs(padSample)); musicL = Math.Max(musicL, Math.Abs(musicSample)); padLE += (double)padSample * padSample; musicLE += (double)musicSample * musicSample; }
            else { padR = Math.Max(padR, Math.Abs(padSample)); musicR = Math.Max(musicR, Math.Abs(musicSample)); padRE += (double)padSample * padSample; musicRE += (double)musicSample * musicSample; }
            var x = (padSample + musicSample) * master; peak = Math.Max(peak, Math.Abs(x));
            float sample = Math.Clamp(x, -1f, 1f); buffer[j] = sample;
            if (((j - offset) & 1) == 0) { left = Math.Max(left, Math.Abs(sample)); leftEnergy += (double)sample * sample; }
            else { right = Math.Max(right, Math.Abs(sample)); rightEnergy += (double)sample * sample; }
        }
        padMeter.Add(padL, padR, padLE, padRE, count / 2);
        musicMeter.Add(musicL, musicR, musicLE, musicRE, count / 2);
        lock (meterGate)
        {
            meterLeft = Math.Max(meterLeft, left); meterRight = Math.Max(meterRight, right);
            energyLeft += leftEnergy; energyRight += rightEnergy;
            framesLeft += (count + 1) / 2; framesRight += count / 2; meterClipped |= peak >= 1;
        }
        Peak = peak; return count;
    }
    public void Dispose() { StopAll(); output?.Dispose(); Music.Dispose(); }
}

sealed class ChannelMeter
{
    readonly object gate = new();
    float left, right; double energyLeft, energyRight; long frames;
    public void Add(float l, float r, double le, double re, int n) { lock (gate) { left = Math.Max(left, l); right = Math.Max(right, r); energyLeft += le; energyRight += re; frames += n; } }
    public MeterReading Take()
    {
        lock (gate)
        {
            var value = new MeterReading(Math.Min(1, left), Math.Min(1, right), frames > 0 ? Math.Sqrt(energyLeft / frames) : 0, frames > 0 ? Math.Sqrt(energyRight / frames) : 0, left >= 1 || right >= 1);
            left = right = 0; energyLeft = energyRight = 0; frames = 0; return value;
        }
    }
}
