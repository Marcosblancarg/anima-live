using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using NAudio.Wave;

namespace src;

public sealed class MusicItem : INotifyPropertyChanged
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Path { get; init; } = "";
    public string Title { get; init; } = "";
    public double Duration { get; init; }
    public string Length => FormatTime(Duration);
    bool active;
    public bool Active { get => active; set { if (active == value) return; active = value; PropertyChanged?.Invoke(this, new(nameof(Active))); PropertyChanged?.Invoke(this, new(nameof(Marker))); } }
    public string Marker => Active ? "▶" : "";
    public event PropertyChangedEventHandler? PropertyChanged;
    public static string FormatTime(double seconds) => TimeSpan.FromSeconds(Math.Max(0, double.IsFinite(seconds) ? seconds : 0)).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);
    public static MusicItem Probe(string path)
    {
        path = System.IO.Path.GetFullPath(path);
        using var reader = new AudioFileReader(path);
        if (reader.WaveFormat.Channels is not (1 or 2) || reader.TotalTime.TotalSeconds <= 0) throw new InvalidDataException("Audio mono o estéreo requerido: " + path);
        return new MusicItem { Path = path, Title = System.IO.Path.GetFileNameWithoutExtension(path), Duration = reader.TotalTime.TotalSeconds };
    }
}

public static class MusicFiles
{
    static MusicFiles() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    public static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase) { ".wav", ".mp3", ".aif", ".aiff", ".wma", ".m4a", ".aac", ".flac" };
    public static bool IsPlaylist(string path) => new[] { ".m3u", ".m3u8", ".pls" }.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    public static string[] ReadPlaylist(string path)
    {
        if (new FileInfo(path).Length > 8 * 1024 * 1024) throw new InvalidDataException("La lista supera 8 MB.");
        string[] lines;
        try { lines = File.ReadAllLines(path, new UTF8Encoding(false, true)); }
        catch (DecoderFallbackException) when (!System.IO.Path.GetExtension(path).Equals(".m3u8", StringComparison.OrdinalIgnoreCase)) { lines = File.ReadAllLines(path, Encoding.GetEncoding(1252)); }
        IEnumerable<string> entries = System.IO.Path.GetExtension(path).Equals(".pls", StringComparison.OrdinalIgnoreCase)
            ? lines.Select(l => l.Trim()).Where(l => l.StartsWith("File", StringComparison.OrdinalIgnoreCase) && l.Contains('='))
                .Select(l => (Key: l[..l.IndexOf('=')], Value: l[(l.IndexOf('=') + 1)..]))
                .Where(x => int.TryParse(x.Key.AsSpan(4), out _)).OrderBy(x => int.Parse(x.Key.AsSpan(4), CultureInfo.InvariantCulture)).Select(x => x.Value)
            : lines.Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'));
        string folder = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!;
        return entries.Take(5000).Select(l => l.Trim().Trim('"')).Where(l => l.Length > 0 && (!Uri.TryCreate(l, UriKind.Absolute, out var uri) || uri.IsFile))
            .Select(l => l.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? new Uri(l).LocalPath : System.IO.Path.GetFullPath(l, folder)).ToArray();
    }
    public static void SavePlaylist(string path, IEnumerable<MusicItem> tracks)
    {
        var text = new StringBuilder("#EXTM3U\n");
        string folder = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!;
        foreach (var item in tracks)
        {
            text.Append("#EXTINF:").Append((int)item.Duration).Append(',').Append(item.Title.Replace('\n', ' ').Replace('\r', ' ')).Append('\n');
            string relative = System.IO.Path.GetRelativePath(folder, item.Path);
            if (!System.IO.Path.IsPathRooted(relative)) relative = "." + System.IO.Path.DirectorySeparatorChar + relative;
            text.Append(relative).Append('\n');
        }
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, text.ToString(), new UTF8Encoding(false)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public sealed record ExplorerEntry(string Path, string Name, bool Folder, bool Playlist)
{
    public string Display => (Folder ? "▸  " : Playlist ? "≡  " : "♪  ") + Name;
}
