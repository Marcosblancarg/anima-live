using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace src;

// Decoding and disk access run on a worker. The device callback only copies bounded PCM buffers.
public sealed class BufferedTrack : IDisposable
{
    const int Capacity = 48000 * 2 * 8;
    readonly float[] ring = new float[Capacity];
    readonly object gate = new();
    readonly CancellationTokenSource cancel = new();
    readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly Task producer;
    int read, write, available;
    long consumed;
    bool eof, disposed;
    public Task Ready => ready.Task;
    public double Duration { get; private set; }
    public double Start { get; }
    public string? Error { get; private set; }
    public int Underruns { get; private set; }
    public double Position { get { lock (gate) return Start + consumed / 96000.0; } }
    public bool Ended { get { lock (gate) return (eof || disposed) && available == 0; } }
    public int BufferedSamples { get { lock (gate) return available; } }
    public BufferedTrack(string path, double start = 0)
    {
        Start = Math.Max(0, start);
        producer = Task.Run(() => Produce(path));
    }
    void Produce(string path)
    {
        try
        {
            using var reader = new AudioFileReader(path);
            Duration = reader.TotalTime.TotalSeconds;
            reader.CurrentTime = TimeSpan.FromSeconds(Math.Min(Start, Duration));
            ISampleProvider input = reader;
            if (input.WaveFormat.Channels == 1) input = new MonoToStereoSampleProvider(input);
            if (input.WaveFormat.Channels != 2) throw new InvalidDataException("El audio debe ser mono o estéreo.");
            if (input.WaveFormat.SampleRate != 48000) input = new WdlResamplingSampleProvider(input, 48000);
            var block = new float[16384];
            while (!cancel.IsCancellationRequested)
            {
                int n = input.Read(block, 0, block.Length);
                if (n == 0) break;
                for (int j = 0; j < n; j++) if (!float.IsFinite(block[j])) block[j] = 0;
                int copied = 0;
                while (copied < n && !cancel.IsCancellationRequested)
                {
                    lock (gate)
                    {
                        while (available == Capacity && !cancel.IsCancellationRequested) Monitor.Wait(gate, 50);
                        if (cancel.IsCancellationRequested) break;
                        int take = Math.Min(n - copied, Math.Min(Capacity - available, Capacity - write));
                        Array.Copy(block, copied, ring, write, take);
                        write = (write + take) % Capacity; available += take; copied += take;
                        if (available >= 48000) ready.TrySetResult();
                    }
                }
            }
            lock (gate)
            {
                eof = true;
                if (available > 0) ready.TrySetResult();
                else if (!cancel.IsCancellationRequested) ready.TrySetException(new InvalidDataException("El audio está vacío o el inicio está fuera del archivo."));
            }
        }
        catch (Exception ex) { lock (gate) { Error = ex.Message; eof = true; ready.TrySetException(ex); } }
        finally { if (cancel.IsCancellationRequested) ready.TrySetCanceled(); }
    }
    public int Read(float[] buffer, int offset, int count)
    {
        lock (gate)
        {
            int n = Math.Min(count, available), first = Math.Min(n, Capacity - read);
            Array.Copy(ring, read, buffer, offset, first);
            if (first < n) Array.Copy(ring, 0, buffer, offset + first, n - first);
            read = (read + n) % Capacity; available -= n; consumed += n;
            if (n < count && !eof && !disposed) Underruns++;
            Monitor.PulseAll(gate); return n;
        }
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true; cancel.Cancel(); available = 0; ready.TrySetCanceled(); Monitor.PulseAll(gate);
        }
        _ = producer.ContinueWith(_ => cancel.Dispose(), TaskScheduler.Default);
    }
}
