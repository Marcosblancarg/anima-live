using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace src;

public sealed class CuePlayer : IDisposable
{
    readonly MusicDeck deck = new() { CrossfadeSeconds = 0 };
    IWavePlayer? output;
    long generation;
    public bool Active => deck.State.Playing;
    public async Task PlayAsync(MusicItem item, string deviceId, AudioOptions options)
    {
        Stop(); long version = generation;
        deck.SetQueue(new[] { item }); await deck.PlayAsync(item);
        if (version != generation) return;
        using var enumerator = new MMDeviceEnumerator(); using var device = enumerator.GetDevice(deviceId);
        var next = new WasapiOut(device, AudioClientShareMode.Shared, options.EventSync, options.LatencyMs);
        try { next.Init(new VolumeSampleProvider(deck) { Volume = .7f }); next.Play(); output = next; }
        catch { next.Dispose(); deck.Stop(); throw; }
    }
    public void Stop() { generation++; output?.Dispose(); output = null; deck.Stop(); }
    public void Dispose() { Stop(); deck.Dispose(); }
}
