using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace src;

public partial class MainWindow
{
    readonly ObservableCollection<MusicItem> musicQueue = new();
    readonly CuePlayer cue = new();
    readonly List<string?> cueIds = new();
    readonly MusicSettings musicSettings = MusicSettings.Load();
    readonly CancellationTokenSource musicJobs = new();
    int mode, folderGeneration, musicUiTicks;
    bool musicUiReady, musicBusy, musicClosed, seeking, explorerVisible = true, compactExplorerVisible, compactToolsVisible, musicDemo;
    string musicFolder = "";
    string lastMusicNotice = "";
    Guid? shownMusicId;

    void InitializeMusicUI()
    {
        MusicQueue.ItemsSource = musicQueue;
        PadFader.Value = musicSettings.PadDb; MusicFader.Value = musicSettings.MusicDb;
        MasterSlider.Value = musicSettings.Master;
        DuckMusicBox.IsChecked = musicSettings.Duck; engine.DuckMusic = musicSettings.Duck;
        RepeatMusicBox.IsChecked = musicSettings.Repeat; engine.Music.Repeat = musicSettings.Repeat;
        CrossfadeBox.SelectedIndex = Array.IndexOf(new[] { 0, 1, 2, 3, 5 }, musicSettings.Crossfade);
        if (CrossfadeBox.SelectedIndex < 0) CrossfadeBox.SelectedIndex = 2;
        musicUiReady = true;
        Loaded += async (_, _) => await RestoreMusicWorkspace();
    }
    async Task RestoreMusicWorkspace()
    {
        string folder = Directory.Exists(musicSettings.Folder) ? musicSettings.Folder : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        if (Directory.Exists(folder)) await ShowMusicFolder(folder);
        if (musicSettings.Queue.Length > 0) await AddPaths(musicSettings.Queue.Where(File.Exists).ToArray());
    }
    void SaveMusicSettings()
    {
        musicClosed = true; musicJobs.Cancel(); folderGeneration++;
        if (!windowWasLoaded) return;
        new MusicSettings { PadDb = PadFader.Value, MusicDb = MusicFader.Value, Master = MasterSlider.Value, Duck = DuckMusicBox.IsChecked == true, Repeat = RepeatMusicBox.IsChecked == true,
            Crossfade = (int)engine.Music.CrossfadeSeconds, Folder = musicFolder, Queue = musicQueue.Select(i => i.Path).ToArray(),
            Lists = musicSettings.Lists, CueId = CueDevices.SelectedIndex >= 0 && CueDevices.SelectedIndex < cueIds.Count ? cueIds[CueDevices.SelectedIndex] : null }.Save();
    }
    void SelectMode(object sender, RoutedEventArgs e) => SwitchMode(int.Parse(((Button)sender).Tag.ToString()!));
    internal void SwitchMode(int value)
    {
        if (!musicUiReady) return;
        CommitTrim(false); mode = Math.Clamp(value, 0, 2);
        PlayerOptionsPopup.IsOpen = false;
        foreach (int i in heldKeys.Values) engine.Release(i, pads[i]); heldKeys.Clear();
        MainArea.Visibility = mode == 0 ? Visibility.Visible : Visibility.Collapsed;
        PlayerPanel.Visibility = mode == 1 ? Visibility.Visible : Visibility.Collapsed;
        MixerPanel.Visibility = mode == 2 ? Visibility.Visible : Visibility.Collapsed;
        PadsTab.BorderBrush = Brush(mode == 0 ? "#BD87FF" : "#453052");
        PlayerTab.BorderBrush = Brush(mode == 1 ? "#6DEA98" : "#295C3D");
        MixerTab.BorderBrush = Brush(mode == 2 ? "#FF728A" : "#68333E");
        PadsTab.Background = Brush(mode == 0 ? "#3A2250" : "#17111F");
        PlayerTab.Background = Brush(mode == 1 ? "#1A4C2E" : "#0E2016");
        MixerTab.Background = Brush(mode == 2 ? "#5C2030" : "#271117");
        Resources["ActiveAccent"] = Brush(mode == 1 ? "#6DEA98" : mode == 2 ? "#FF728A" : "#A377FF");
        MixMeter.LowColor = mode == 1 ? "#4FD27C" : mode == 2 ? "#ED526B" : "#20BFEE";
        UpdateMusicLayout();
    }
    void ToggleCompactTools(object sender, RoutedEventArgs e) { compactToolsVisible = !compactToolsVisible; UpdateResponsiveLayout(); }
    void UpdateMusicLayout()
    {
        if (!musicUiReady) return;
        bool compact = LayoutRoot.ActualWidth < 800;
        bool show = compact ? compactExplorerVisible : explorerVisible;
        ExplorerPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        MusicMain.Visibility = compact && show ? Visibility.Collapsed : Visibility.Visible;
        ExplorerColumn.Width = compact ? new GridLength(0) : new GridLength(show ? Math.Clamp(LayoutRoot.ActualWidth * .23, 200, 270) : 0);
        ExplorerGap.Width = new GridLength(show && !compact ? 12 : 0);
        Grid.SetColumn(ExplorerPanel, compact ? 2 : 0);
        BackToMusicButton.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        ClockLabel.FontSize = LayoutRoot.ActualWidth < 720 ? 17 : 21;
        bool shortWindow = LayoutRoot.ActualHeight < 500;
        NowPlayingPanel.Padding = new Thickness(shortWindow ? 7 : 10, shortWindow ? 4 : 7, shortWindow ? 7 : 10, shortWindow ? 4 : 7);
        NowPlayingTitle.FontSize = shortWindow ? 15 : 17;
        MusicSeek.Height = shortWindow ? 16 : 22;
        ExplorerToggleButton.Padding = new Thickness(9, shortWindow ? 3 : 6, 9, shortWindow ? 3 : 6);
        UpdateQueueColumns();
    }
    void ToggleExplorer(object sender, RoutedEventArgs e)
    {
        if (LayoutRoot.ActualWidth < 800) compactExplorerVisible = !compactExplorerVisible;
        else explorerVisible = !explorerVisible;
        UpdateMusicLayout();
    }
    void BackToMusic(object sender, RoutedEventArgs e) { compactExplorerVisible = false; UpdateMusicLayout(); }
    void UpdateQueueColumns() { if (MusicQueue != null && MusicTitleColumn != null) MusicTitleColumn.Width = Math.Max(90, MusicQueue.ActualWidth - 113); }
    void QueueSizeChanged(object sender, SizeChangedEventArgs e) => UpdateQueueColumns();
    void TickMusic(double elapsed)
    {
        if (!musicUiReady) return;
        if (musicDemo)
        {
            ClockLabel.Text = "20:35:42";
            PadChannelMeter.Update(new(.34f, .29f, .15, .12, false), elapsed);
            MusicChannelMeter.Update(new(.19f, .22f, .08, .09, false), elapsed);
            return;
        }
        PadChannelMeter.Update(engine.TakePadMeterReading(), elapsed);
        MusicChannelMeter.Update(engine.TakeMusicMeterReading(), elapsed);
        string clock = DateTime.Now.ToString("HH:mm:ss");
        if (ClockLabel.Text != clock) { ClockLabel.Text = clock; ClockLabel.ToolTip = DateTime.Now.ToString("dddd dd/MM/yyyy"); }
        if (++musicUiTicks % 5 != 0) return;
        var state = engine.Music.State;
        NowPlayingTitle.Text = state.Loading ? "Preparando audio…" : state.Item?.Title ?? "Listo para reproducir";
        NowPlayingTitle.ToolTip = state.Item?.Path;
        NextMusicLabel.Text = state.Next == null ? "" : "Sigue: " + state.Next.Title;
        NextMusicLabel.ToolTip = state.Next?.Title;
        MusicElapsed.Text = MusicItem.FormatTime(state.Position) + " / " + MusicItem.FormatTime(state.Item?.Duration ?? 0);
        MusicRemaining.Text = state.Paused ? "PAUSA" : state.Playing ? "−" + MusicItem.FormatTime(Math.Max(0, (state.Item?.Duration ?? 0) - state.Position)) : state.Item != null ? "FINALIZADO" : "";
        PauseMusicButton.Content = state.Paused ? "▶ Seguir" : "Ⅱ Pausa";
        if (!seeking) { MusicSeek.Maximum = Math.Max(1, state.Item?.Duration ?? 1); MusicSeek.Value = Math.Clamp(state.Position, 0, MusicSeek.Maximum); }
        Guid? activeId = state.Playing ? state.Item?.Id : null;
        if (shownMusicId != activeId)
        {
            shownMusicId = activeId;
            foreach (var track in musicQueue) track.Active = track.Id == shownMusicId;
        }
        MusicRemaining.ToolTip = state.Notice;
        if (state.Notice != lastMusicNotice) { lastMusicNotice = state.Notice; if (state.Notice.Length > 0) Say(state.Notice); }
        QueueSummary.Text = $"{musicQueue.Count} temas · {MusicItem.FormatTime(musicQueue.Sum(i => i.Duration))}";
    }
    void UpdateCueDevices()
    {
        string? previous = CueDevices.SelectedIndex >= 0 && CueDevices.SelectedIndex < cueIds.Count ? cueIds[CueDevices.SelectedIndex] : musicSettings.CueId;
        CueDevices.Items.Clear(); cueIds.Clear(); CueDevices.Items.Add("Elegí una salida de preescucha"); cueIds.Add(null);
        for (int i = 0; i < audioIds.Count; i++) { cueIds.Add(audioIds[i]); CueDevices.Items.Add(AudioDevices.Items[i]); }
        CueDevices.SelectedIndex = previous != null && cueIds.Contains(previous) ? cueIds.IndexOf(previous) : 0;
    }
    async void BrowseMusicFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Audio Explorer · elegir carpeta", Multiselect = false };
        if (Directory.Exists(musicFolder)) dialog.InitialDirectory = musicFolder;
        if (dialog.ShowDialog(this) == true) await ShowMusicFolder(dialog.FolderName);
    }
    async Task ShowMusicFolder(string folder)
    {
        int version = ++folderGeneration;
        try
        {
            var entries = await Task.Run(() => Directory.EnumerateDirectories(folder).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).Select(p => new ExplorerEntry(p, System.IO.Path.GetFileName(p), true, false))
                .Concat(Directory.EnumerateFiles(folder).Where(p => MusicFiles.AudioExtensions.Contains(System.IO.Path.GetExtension(p)) || MusicFiles.IsPlaylist(p)).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).Select(p => new ExplorerEntry(p, System.IO.Path.GetFileName(p), false, MusicFiles.IsPlaylist(p)))).Take(5000).ToArray());
            if (version != folderGeneration || musicClosed) return;
            musicFolder = folder; ExplorerPath.Text = folder; ExplorerPath.ToolTip = folder; ExplorerList.ItemsSource = entries;
        }
        catch (Exception ex) { if (!musicClosed && version == folderGeneration) Say("No se pudo abrir la carpeta: " + ex.Message); }
    }
    async void ShowFolders(object sender, RoutedEventArgs e) { if (Directory.Exists(musicFolder)) await ShowMusicFolder(musicFolder); else BrowseMusicFolder(sender, e); }
    async void MusicFolderUp(object sender, RoutedEventArgs e) { var parent = Directory.Exists(musicFolder) ? Directory.GetParent(musicFolder) : null; if (parent != null) await ShowMusicFolder(parent.FullName); }
    async void ShowPlaylists(object sender, RoutedEventArgs e)
    {
        int version = ++folderGeneration;
        try
        {
            var entries = await Task.Run(() => musicSettings.Lists.Where(File.Exists).Concat(Directory.Exists(musicFolder) ? Directory.EnumerateFiles(musicFolder).Where(MusicFiles.IsPlaylist) : Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).Select(p => new ExplorerEntry(p, System.IO.Path.GetFileName(p), false, true)).ToArray());
            if (version != folderGeneration || musicClosed) return;
            ExplorerPath.Text = "Listas guardadas y de la carpeta actual"; ExplorerList.ItemsSource = entries;
        }
        catch (Exception ex) { if (!musicClosed) Say(ex.Message); }
    }
    async void ExplorerDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ExplorerList.SelectedItem is not ExplorerEntry entry) return;
        if (entry.Folder) await ShowMusicFolder(entry.Path); else await AddPaths(new[] { entry.Path });
    }
    async void AddExplorerSelection(object sender, RoutedEventArgs e)
    {
        await AddPaths(ExplorerList.SelectedItems.Cast<ExplorerEntry>().Select(i => i.Path).ToArray());
        if (LayoutRoot.ActualWidth < 800) { compactExplorerVisible = false; UpdateMusicLayout(); }
    }
    async Task AddPaths(string[] paths, bool replace = false)
    {
        if (musicBusy || paths.Length == 0 || musicClosed) return;
        musicBusy = true; Say("Leyendo títulos y duraciones…");
        try
        {
            var result = await Task.Run(() =>
            {
                var tracks = new List<MusicItem>(); int errors = 0;
                foreach (var path in paths.Take(5000))
                {
                    if (musicJobs.IsCancellationRequested) break;
                    try
                    {
                        var files = Directory.Exists(path) ? Directory.EnumerateFiles(path).Where(p => MusicFiles.AudioExtensions.Contains(System.IO.Path.GetExtension(p))).OrderBy(p => p).ToArray() : MusicFiles.IsPlaylist(path) ? MusicFiles.ReadPlaylist(path) : new[] { path };
                        foreach (var file in files)
                        {
                            if (tracks.Count >= 5000 || musicJobs.IsCancellationRequested) break;
                            try { tracks.Add(MusicItem.Probe(file)); } catch { errors++; }
                        }
                    }
                    catch { errors++; }
                }
                return (tracks, errors);
            });
            if (musicClosed) return;
            if (result.tracks.Count == 0) { Say("No se encontraron audios compatibles. Las listas admiten archivos locales."); return; }
            if (replace) { engine.Music.Stop(); musicQueue.Clear(); }
            int first = musicQueue.Count;
            foreach (var track in result.tracks.Take(Math.Max(0, 5000 - musicQueue.Count))) musicQueue.Add(track);
            engine.Music.SetQueue(musicQueue); shownMusicId = null;
            MusicQueue.SelectedIndex = first < musicQueue.Count ? first : 0;
            Say($"{musicQueue.Count - first} audios añadidos" + (result.errors > 0 ? $" · {result.errors} archivos omitidos." : "."));
        }
        catch (Exception ex) { if (!musicClosed) Error(ex); }
        finally { musicBusy = false; }
    }
    async void AddMusicFiles(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Multiselect = true, Filter = "Audio y listas|*.wav;*.mp3;*.wma;*.aif;*.aiff;*.m4a;*.aac;*.flac;*.m3u;*.m3u8;*.pls|Todos|*.*" };
        if (dialog.ShowDialog(this) == true) await AddPaths(dialog.FileNames);
    }
    async void OpenMusicList(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Listas de música|*.m3u;*.m3u8;*.pls" };
        if (dialog.ShowDialog(this) == true) { await AddPaths(new[] { dialog.FileName }, true); RememberPlaylist(dialog.FileName); }
    }
    void SaveMusicList(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Lista M3U8|*.m3u8", DefaultExt = ".m3u8", FileName = "Mi música.m3u8" };
        if (dialog.ShowDialog(this) != true) return;
        try { MusicFiles.SavePlaylist(dialog.FileName, musicQueue); RememberPlaylist(dialog.FileName); Say("Lista guardada. Los audios permanecen en sus carpetas."); } catch (Exception ex) { Error(ex); }
    }
    void RememberPlaylist(string path) => musicSettings.Lists = new[] { path }.Concat(musicSettings.Lists).Distinct(StringComparer.OrdinalIgnoreCase).Take(30).ToArray();
    void MoveMusicUp(object sender, RoutedEventArgs e) => MoveMusic(-1);
    void MoveMusicDown(object sender, RoutedEventArgs e) => MoveMusic(1);
    void MoveMusic(int step)
    {
        int at = MusicQueue.SelectedIndex, next = at + step;
        if (at < 0 || next < 0 || next >= musicQueue.Count) return;
        musicQueue.Move(at, next); MusicQueue.SelectedIndex = next; engine.Music.SetQueue(musicQueue);
    }
    void RemoveMusic(object sender, RoutedEventArgs e) { foreach (var item in MusicQueue.SelectedItems.Cast<MusicItem>().ToArray()) musicQueue.Remove(item); engine.Music.SetQueue(musicQueue); }
    void QueueDragOver(object sender, DragEventArgs e) { e.Effects = !musicBusy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    async void QueueDrop(object sender, DragEventArgs e) { if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) await AddPaths(paths); }
    async void QueueDoubleClick(object sender, MouseButtonEventArgs e) { if (MusicQueue.SelectedItem is MusicItem item) await PlayMusicItem(item); }
    async Task PlayMusicItem(MusicItem item) { try { await engine.Music.PlayAsync(item); } catch (Exception ex) { if (!musicClosed) Error(ex); } }
    async void MusicPlay(object sender, RoutedEventArgs e)
    {
        var state = engine.Music.State;
        if (state.Playing && state.Paused) { engine.Music.TogglePause(); return; }
        var item = MusicQueue.SelectedItem as MusicItem ?? musicQueue.FirstOrDefault();
        if (state.Playing && state.Item?.Id == item?.Id) return;
        if (item != null) await PlayMusicItem(item);
    }
    void MusicPause(object sender, RoutedEventArgs e) => engine.Music.TogglePause();
    void MusicStop(object sender, RoutedEventArgs e) => engine.Music.Stop();
    async void MusicNext(object sender, RoutedEventArgs e) { try { await engine.Music.NextAsync(); } catch (Exception ex) { Error(ex); } }
    void StopAfterChanged(object sender, RoutedEventArgs e) => engine.Music.StopAfterCurrent = StopAfterBox.IsChecked == true;
    void RepeatMusicChanged(object sender, RoutedEventArgs e) => engine.Music.Repeat = RepeatMusicBox.IsChecked == true;
    void CrossfadeChanged(object sender, SelectionChangedEventArgs e) { if (CrossfadeBox.SelectedIndex >= 0) engine.Music.CrossfadeSeconds = new[] { 0, 1, 2, 3, 5 }[CrossfadeBox.SelectedIndex]; }
    void SeekDown(object sender, MouseButtonEventArgs e) => seeking = true;
    async void SeekUp(object sender, MouseButtonEventArgs e) { seeking = false; try { await engine.Music.SeekAsync(MusicSeek.Value); } catch (Exception ex) { Error(ex); } }
    async void SeekKey(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Left or Key.Right or Key.Home or Key.End)) return;
        e.Handled = true;
        double value = e.Key == Key.Home ? 0 : e.Key == Key.End ? MusicSeek.Maximum : MusicSeek.Value + (e.Key == Key.Right ? 5 : -5);
        try { await engine.Music.SeekAsync(Math.Clamp(value, 0, MusicSeek.Maximum)); } catch (Exception ex) { Error(ex); }
    }
    async void PlayCue(object sender, RoutedEventArgs e)
    {
        if (CueDevices.SelectedIndex <= 0 || CueDevices.SelectedIndex >= cueIds.Count) { Say("Elegí una salida de preescucha independiente."); return; }
        string id = cueIds[CueDevices.SelectedIndex]!;
        if (AudioDevices.SelectedIndex >= 0 && id == audioIds[AudioDevices.SelectedIndex]) { Say("La preescucha necesita una salida diferente de la emisión. Elegí otro par de canales o dispositivo."); return; }
        var item = MusicQueue.SelectedItem as MusicItem;
        try
        {
            if (item == null && ExplorerList.SelectedItem is ExplorerEntry entry && !entry.Folder && !entry.Playlist) item = await Task.Run(() => MusicItem.Probe(entry.Path));
            if (item != null) { await cue.PlayAsync(item, id, audioOptions); Say("Preescucha en: " + CueDevices.SelectedItem); }
        }
        catch (Exception ex) { Error(ex); }
    }
    void StopCue(object sender, RoutedEventArgs e) => cue.Stop();
    void OpenPlayerOptions(object sender, RoutedEventArgs e) => PlayerOptionsPopup.IsOpen = !PlayerOptionsPopup.IsOpen;
    static float FaderGain(double db) => db <= -60 ? 0 : (float)Math.Pow(10, db / 20);
    void PadFaderChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { engine.PadGain = FaderGain(e.NewValue); if (PadFaderLabel != null) PadFaderLabel.Text = e.NewValue <= -60 ? "−∞ dB" : $"{e.NewValue:0.0} dB"; }
    void MusicFaderChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { engine.MusicGain = FaderGain(e.NewValue); if (MusicFaderLabel != null) MusicFaderLabel.Text = e.NewValue <= -60 ? "−∞ dB" : $"{e.NewValue:0.0} dB"; }
    void MixerMuteChanged(object sender, RoutedEventArgs e) { engine.PadMuted = PadMute.IsChecked == true; engine.MusicMuted = MusicMute.IsChecked == true; }
    void DuckMusicChanged(object sender, RoutedEventArgs e) => engine.DuckMusic = DuckMusicBox.IsChecked == true;
    void ResetFader(object sender, MouseButtonEventArgs e) { ((Slider)sender).Value = 0; e.Handled = true; }
    internal void MakeMusicDemo()
    {
        musicDemo = true;
        foreach (var track in new[] { ("Cortina · Espera", 182), ("Apertura del programa", 28), ("Música de la tarde", 234), ("Separador · Anima Live", 12), ("Entrevista · Bloque 01", 785), ("Cierre", 154) })
            musicQueue.Add(new MusicItem { Title = track.Item1, Duration = track.Item2, Path = "C:\\Música\\" + track.Item1 + ".mp3" });
        musicQueue[0].Active = true;
        ExplorerPath.Text = "C:\\Música\\Streaming";
        ExplorerList.ItemsSource = new[] { new ExplorerEntry("", "Cortinas", true, false), new ExplorerEntry("", "Programas", true, false), new ExplorerEntry("", "Separadores", true, false), new ExplorerEntry("", "Tarde.m3u8", false, true), new ExplorerEntry("", "Espera.mp3", false, false) };
        NowPlayingTitle.Text = "Cortina · Espera"; MusicElapsed.Text = "0:48 / 3:02"; MusicRemaining.Text = "−2:14"; NextMusicLabel.Text = "Sigue: Apertura del programa";
        MusicSeek.Maximum = 182; MusicSeek.Value = 48; QueueSummary.Text = "6 temas · 23:15";
    }
}

public sealed class MusicSettings
{
    public double PadDb { get; set; }
    public double MusicDb { get; set; }
    public double Master { get; set; } = .7;
    public bool Duck { get; set; }
    public bool Repeat { get; set; }
    public int Crossfade { get; set; } = 2;
    public string Folder { get; set; } = "";
    public string[] Queue { get; set; } = Array.Empty<string>();
    public string[] Lists { get; set; } = Array.Empty<string>();
    public string? CueId { get; set; }
    static string Path => System.IO.Path.Combine(AppContext.BaseDirectory, "music-settings.json");
    public static MusicSettings Load()
    {
        try
        {
            if (new FileInfo(Path).Length > 4 * 1024 * 1024) return new();
            var value = JsonSerializer.Deserialize<MusicSettings>(File.ReadAllText(Path));
            if (value != null && double.IsFinite(value.PadDb) && double.IsFinite(value.MusicDb) && double.IsFinite(value.Master) && value.Master >= 0 && value.Master <= 1 && value.PadDb >= -60 && value.PadDb <= 6 && value.MusicDb >= -60 && value.MusicDb <= 6 && value.Queue != null && value.Lists != null && value.Folder != null) return value;
        }
        catch { }
        return new();
    }
    public void Save()
    {
        try { File.WriteAllText(Path + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); File.Move(Path + ".tmp", Path, true); } catch { }
    }
}
