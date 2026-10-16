using NAudio.Wave;

namespace src;

public sealed record MusicState(MusicItem? Item, double Position, bool Playing, bool Paused, bool Loading, string Notice, int Underruns, MusicItem? Next);

public sealed class MusicDeck : ISampleProvider, IDisposable
{
    readonly object gate = new();
    MusicItem[] queue = Array.Empty<MusicItem>();
    MusicItem? item, preparedItem;
    BufferedTrack? current, prepared, tail, loadingTrack;
    float[] a = new float[8192], b = new float[8192];
    long loadVersion, prepareVersion, fadePosition, fadeLength;
    int anchor = -1;
    bool playing, paused, loading, preparing, exhausted = true, disposed;
    string notice = "";
    double crossfade = 2;
    double finishedPosition;
    bool repeat;
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    public bool StopAfterCurrent { get; set; }
    public double CrossfadeSeconds { get { lock (gate) return crossfade; } set { lock (gate) crossfade = Math.Clamp(value, 0, 10); } }
    public bool Repeat { get { lock (gate) return repeat; } set { lock (gate) { repeat = value; RequestNext(); } } }
    public MusicState State { get { lock (gate) return new(item, current?.Position ?? finishedPosition, playing, paused, loading, notice, (current?.Underruns ?? 0) + (tail?.Underruns ?? 0), preparedItem); } }
    internal bool NextReady { get { lock (gate) return prepared != null || !preparing; } }

    public void SetQueue(IEnumerable<MusicItem> items)
    {
        lock (gate)
        {
            int old = item == null ? -1 : Array.FindIndex(queue, i => i.Id == item.Id);
            queue = items.Take(5000).ToArray();
            int found = item == null ? -1 : Array.FindIndex(queue, i => i.Id == item.Id);
            anchor = found >= 0 ? found : Math.Clamp(old - 1, -1, queue.Length - 1);
            RequestNext();
        }
    }
    public async Task PlayAsync(MusicItem selected, double start = 0, bool keepPaused = false)
    {
        BufferedTrack track; long version;
        lock (gate)
        {
            if (disposed) return;
            StopSources(); version = ++loadVersion;
            item = selected; anchor = Array.FindIndex(queue, i => i.Id == selected.Id);
            playing = paused = false; loading = true; notice = "Preparando audio…";
            finishedPosition = 0;
            track = loadingTrack = new BufferedTrack(selected.Path, Math.Clamp(start, 0, Math.Max(0, selected.Duration - .001)));
        }
        try
        {
            await track.Ready.ConfigureAwait(false);
            lock (gate)
            {
                if (version != loadVersion || disposed) { track.Dispose(); return; }
                loadingTrack = null; current = track; playing = true; paused = keepPaused; loading = false; notice = "";
                RequestNext();
            }
        }
        catch (Exception ex)
        {
            track.Dispose();
            lock (gate)
            {
                if (version != loadVersion || disposed) return;
                loadingTrack = null; loading = playing = paused = false; notice = "No se pudo reproducir: " + ex.Message;
            }
            throw;
        }
    }
    public Task SeekAsync(double seconds)
    {
        MusicItem? selected; bool wasPaused;
        lock (gate) { selected = item; wasPaused = paused; }
        return selected == null ? Task.CompletedTask : PlayAsync(selected, seconds, wasPaused);
    }
    public void TogglePause() { lock (gate) if (playing) paused = !paused; }
    public Task NextAsync()
    {
        MusicItem? next;
        lock (gate) { next = Candidates().FirstOrDefault(); }
        if (next == null) { Stop(); return Task.CompletedTask; }
        return PlayAsync(next);
    }
    MusicItem[] Candidates()
    {
        if (queue.Length == 0) return Array.Empty<MusicItem>();
        int start = anchor + 1;
        if (!repeat) return queue.Skip(Math.Max(0, start)).ToArray();
        return Enumerable.Range(0, queue.Length).Select(i => queue[(Math.Max(0, start) + i) % queue.Length]).ToArray();
    }
    void RequestNext()
    {
        long version = ++prepareVersion;
        prepared?.Dispose(); prepared = null; preparedItem = null; preparing = false; exhausted = true;
        if (!playing || current == null || disposed) return;
        var candidates = Candidates();
        if (candidates.Length == 0) return;
        preparing = true; exhausted = false;
        _ = Task.Run(async () =>
        {
            foreach (var candidate in candidates)
            {
                lock (gate) if (version != prepareVersion || disposed) return;
                var track = new BufferedTrack(candidate.Path);
                try
                {
                    await track.Ready.ConfigureAwait(false);
                    lock (gate)
                    {
                        if (version != prepareVersion || disposed) { track.Dispose(); return; }
                        prepared = track; preparedItem = candidate; preparing = false; return;
                    }
                }
                catch (Exception ex)
                {
                    track.Dispose();
                    lock (gate) { if (version != prepareVersion || disposed) return; notice = "Se omitió " + candidate.Title + ": " + ex.Message; }
                }
            }
            lock (gate) if (version == prepareVersion) { preparing = false; exhausted = true; }
        });
    }
    void Advance(bool fade)
    {
        var previous = current;
        double remaining = previous == null ? 0 : Math.Max(0, previous.Duration - previous.Position);
        current = prepared; item = preparedItem; prepared = null; preparedItem = null;
        anchor = item == null ? -1 : Array.FindIndex(queue, i => i.Id == item.Id);
        if (fade && previous != null)
        {
            tail?.Dispose(); tail = previous;
            fadePosition = 0; fadeLength = Math.Max(2, (long)(Math.Min(crossfade, remaining) * 48000) * 2);
        }
        else previous?.Dispose();
        RequestNext();
    }
    public int Read(float[] buffer, int offset, int count)
    {
        // Typed writes also support NAudio's byte-backed WaveBuffer float view.
        for (int j = offset; j < offset + count; j++) buffer[j] = 0;
        lock (gate)
        {
            if (!playing || paused || disposed) return count;
            if (a.Length < count) { a = new float[count]; b = new float[count]; }
            int written = 0, transitions = 0;
            while (written < count && playing && transitions++ < 64)
            {
                if (current == null || current.Ended)
                {
                    if (current?.Error != null) notice = current.Error;
                    if (StopAfterCurrent) { Finish(); break; }
                    if (prepared != null) { Advance(false); continue; }
                    if (!preparing && exhausted) Finish();
                    break;
                }
                double remaining = Math.Max(0, current.Duration - current.Position);
                if (tail == null && prepared != null && !StopAfterCurrent && crossfade > 0 && remaining <= crossfade) Advance(true);
                int request = count - written;
                if (tail == null && prepared != null && !StopAfterCurrent && crossfade > 0)
                {
                    double beforeFade = current!.Duration - current.Position - crossfade;
                    if (beforeFade > 0) request = Math.Min(request, Math.Max(2, (int)Math.Min(request, beforeFade * 48000) * 2));
                }
                int n = current!.Read(a, 0, request);
                if (tail != null)
                {
                    int old = tail.Read(b, 0, request);
                    int mixed = Math.Max(n, old);
                    for (int j = 0; j < mixed; j++)
                    {
                        float amount = Math.Min(1f, (float)((fadePosition + j) / 2) / Math.Max(1, fadeLength / 2));
                        buffer[offset + written + j] = (j < n ? a[j] : 0) * amount + (j < old ? b[j] : 0) * (1 - amount);
                    }
                    fadePosition += mixed; n = mixed;
                    if (fadePosition >= fadeLength || tail.Ended) { tail.Dispose(); tail = null; }
                }
                else for (int j = 0; j < n; j++) buffer[offset + written + j] = a[j];
                written += n;
                if (n == 0 && !current.Ended) break;
            }
        }
        return count;
    }
    void Finish()
    {
        finishedPosition = current?.Position ?? item?.Duration ?? 0;
        ++prepareVersion; prepared?.Dispose(); prepared = null; preparedItem = null; preparing = false; exhausted = true;
        current?.Dispose(); current = null; tail?.Dispose(); tail = null;
        playing = paused = false; notice = notice.Length > 0 ? notice : "Lista finalizada.";
    }
    void StopSources()
    {
        ++prepareVersion; prepared?.Dispose(); prepared = null; preparedItem = null;
        current?.Dispose(); current = null; tail?.Dispose(); tail = null; loadingTrack?.Dispose(); loadingTrack = null;
        preparing = false; exhausted = true;
    }
    public void Stop()
    {
        lock (gate) { ++loadVersion; StopSources(); playing = paused = loading = false; item = null; anchor = -1; finishedPosition = 0; notice = ""; }
    }
    public void Dispose() { lock (gate) { if (disposed) return; Stop(); disposed = true; } }
}
