using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using NAudio.Midi;
using NAudio.CoreAudioApi;
namespace src;

public partial class MainWindow : Window
{
    Pad[] pads = PadBank.Create();
    int page;
    readonly long[] midiFlashUntil = new long[64];
    readonly double[] positions = new double[64];
    int trimDrag = -1;
    bool trimUpdating;
    AudioOptions audioOptions = AudioOptions.Load();
    readonly Button[] pageButtons = new Button[4];
    readonly AudioEngine engine = new();
    long lastMeterTick = System.Diagnostics.Stopwatch.GetTimestamp();
    readonly Border[] faces = new Border[16]; readonly string[] visualStates = new string[16];
    readonly Border[] tiles = new Border[16]; readonly TextBlock[] names = new TextBlock[16]; readonly TextBlock[] details = new TextBlock[16]; readonly TextBlock[] times = new TextBlock[16];
    readonly Key[] keys = { Key.D1, Key.D2, Key.D3, Key.D4, Key.Q, Key.W, Key.E, Key.R, Key.A, Key.S, Key.D, Key.F, Key.Z, Key.X, Key.C, Key.V };
    readonly Dictionary<Key, int> heldKeys = new(); readonly Dictionary<int, int[]> heldMidi = new();
    readonly List<string?> audioIds = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    MidiIn? midi; int selected; bool updating = true, busy, dirty, refreshing; int learn = -1;
    bool Locked => LiveLock?.IsChecked == true;
    bool CanEdit => !updating && !busy && !Locked;
    Pad Current => pads[selected];
    public MainWindow()
    {
        InitializeComponent(); BuildPads(); BuildColors(); BuildPages(); InitializeMusicUI(); ShowPad();
        InitializeResponsiveLayout();
        engine.OutputFailed += message => Dispatcher.BeginInvoke(() => Say("Salida de audio interrumpida: " + message + ". Volvé a seleccionar una salida."));
        Loaded += (_, _) => { RefreshDeviceLists(); timer.Start(); };
        timer.Tick += (_, _) => Tick(); PreviewKeyDown += OnKeyDown; PreviewKeyUp += OnKeyUp;
        Deactivated += (_, _) => { foreach (int i in heldKeys.Values) engine.Release(i, pads[i]); heldKeys.Clear(); };
        Closing += (_, e) => { if (!ConfirmDiscard()) { e.Cancel = true; return; } timer.Stop(); SaveMusicSettings(); cue.Dispose(); CloseMidi(); engine.Dispose(); };
    }
    void BuildPads()
    {
        for (int i = 0; i < 16; i++)
        {
            int id = i; int pressed = -1; var grid = new Grid { Margin = new Thickness(16, 12, 16, 14) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var header = new DockPanel(); header.Children.Add(new TextBlock { Text = $"{i + 1:00}", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brush("#9B90AE") });
            var keyText = new TextBlock { Text = keys[i].ToString().Replace("D", i < 4 ? "" : "D"), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Right, Foreground = Brushes.SlateGray }; header.Children.Add(new Border { Child = keyText, Background = Brush("#211B2C"), BorderBrush = Brush("#45334E"), BorderThickness = new Thickness(1), Padding = new Thickness(7, 2, 7, 2), CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Right }); grid.Children.Add(header);
            names[i] = new TextBlock { Text = "Arrastrá audio", TextWrapping = TextWrapping.Wrap, FontSize = 17, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, MaxHeight = 65 }; Grid.SetRow(names[i], 1); grid.Children.Add(names[i]);
            var bottom = new StackPanel(); times[i] = new TextBlock { FontSize = 10, Foreground = Brush("#9E93AD"), Margin = new Thickness(0, 0, 0, 5) }; details[i] = new TextBlock { FontSize = 10 }; bottom.Children.Add(times[i]); bottom.Children.Add(details[i]); Grid.SetRow(bottom, 2); grid.Children.Add(bottom);
            faces[i] = new Border { Child = grid, CornerRadius = new CornerRadius(7), BorderBrush = Brush("#39303F"), BorderThickness = new Thickness(1) };
            var b = new Border { Child = faces[i], Background = Brush("#050507"), Padding = new Thickness(2), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(11), Margin = new Thickness(4), AllowDrop = true, Cursor = Cursors.Hand, Focusable = true }; tiles[i] = b;
            b.SizeChanged += (_, _) => UpdatePadLayout(id);
            b.MouseEnter += (_, _) => { visualStates[id] = ""; Tick(); };
            b.MouseLeave += (_, _) => { visualStates[id] = ""; Tick(); };
            b.MouseLeftButtonDown += (_, e) => { if (busy) return; SelectPad(page * 16 + id); b.Focus(); b.CaptureMouse(); pressed = selected; engine.Press(pressed, pads[pressed]); e.Handled = true; };
            b.MouseLeftButtonUp += (_, e) => { if (pressed >= 0) engine.Release(pressed, pads[pressed]); pressed = -1; b.ReleaseMouseCapture(); e.Handled = true; };
            b.LostMouseCapture += (_, _) => { if (pressed >= 0) engine.Release(pressed, pads[pressed]); pressed = -1; };
            b.MouseRightButtonDown += (_, e) => { SelectPad(page * 16 + id); e.Handled = true; };
            b.DragOver += (_, e) => { e.Effects = CanEdit && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
            b.Drop += async (_, e) => { if (!CanEdit) return; if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) { int start = page * 16 + id; for (int j = 0; j < paths.Length && id + j < 16; j++) await LoadAudio(start + j, paths[j]); selected = start; ShowPad(); } };
            PadsGrid.Children.Add(b);
        }
    }
    static readonly Dictionary<string, SolidColorBrush> brushCache = new();
    static SolidColorBrush Brush(string color)
    {
        if (brushCache.TryGetValue(color, out var cached)) return cached;
        SolidColorBrush brush; try { brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!; } catch { brush = new SolidColorBrush(Color.FromRgb(155, 61, 255)); }
        brush.Freeze(); brushCache[color] = brush; return brush;
    }
    void BuildColors() { foreach (var color in new[] { "#19B9EA", "#397CF5", "#7751F5", "#9B3DFF", "#BE68FF", "#EE6BA7" }) { var b = new Button { Background = Brush(color), Margin = new Thickness(0, 0, 7, 0), Padding = new Thickness(0) }; b.Click += (_, _) => { if (!CanEdit) return; Current.Color = color; Changed(); DrawWave(); }; Colors.Children.Add(b); } }
    void Changed() { dirty = true; Title = "Anima Live · 0.6 *"; Tick(); }
    void ShowPad() { updating = true; PadNumber.Text = $"P{page + 1} / {selected % 16 + 1:00}"; PadName.Text = Current.Name; FileLabel.Text = Current.FileName == "" ? "Sin audio · arrastrá o cargá un archivo" : Current.FileName; ModeBox.SelectedIndex = (int)Current.Mode; GainSlider.Value = Current.GainDb; GainLabel.Text = $"{Current.GainDb:0.0} dB"; StartBox.Text = Current.Start.ToString("0.######", CultureInfo.InvariantCulture); EndBox.Text = Current.End.ToString("0.######", CultureInfo.InvariantCulture); VelocityBox.IsChecked = Current.Velocity; MidiLabel.Text = $"{(Current.IsCC ? "CC" : "Nota")} {Current.Note} · {(Current.Channel == 0 ? "todos" : $"canal {Current.Channel}")}"; updating = false; DrawWave(); Tick(); }
    static LinearGradientBrush FaceBrush(string hex, bool highlighted, bool playing)
    {
        var c = Brush(hex).Color;
        Color Tint(double factor) => Color.FromRgb((byte)(10 + c.R * factor), (byte)(9 + c.G * factor), (byte)(14 + c.B * factor));
        var fill = new LinearGradientBrush(Tint(playing ? .46 : highlighted ? .19 : .075), Color.FromRgb(12, 11, 17), 90);
        fill.Freeze(); return fill;
    }
    void Tick()
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        double elapsed = (now - lastMeterTick) / (double)System.Diagnostics.Stopwatch.Frequency;
        MixMeter.Update(engine.TakeMeterReading(), elapsed);
        TickMusic(elapsed);
        lastMeterTick = now;
        engine.CopyPositions(positions);
        for (int i = 0; i < 16; i++)
        {
            int global = page * 16 + i; var p = pads[global]; double pos = positions[global]; bool playing = pos >= 0 || System.Diagnostics.Stopwatch.GetTimestamp() < midiFlashUntil[global];
            bool highlighted = global == selected || tiles[i].IsMouseOver;
            var state = $"{p.Color}/{highlighted}/{playing}";
            if (visualStates[i] != state)
            {
                visualStates[i] = state;
                var color = Brush(p.Color).Color;
                var outline = new LinearGradientBrush(playing ? ColorsWhite : color, playing ? color : Color.FromRgb((byte)(color.R * .45), (byte)(color.G * .45), (byte)(color.B * .60)), 65);
                outline.Opacity = highlighted || playing ? 1 : .65; outline.Freeze();
                tiles[i].BorderBrush = outline;
                faces[i].Background = FaceBrush(p.Color, highlighted, playing);
                tiles[i].Effect = highlighted || playing ? new System.Windows.Media.Effects.DropShadowEffect { Color = color, BlurRadius = playing ? 18 : 12, ShadowDepth = 0, Opacity = playing ? .65 : .32 } : null;
            }
            names[i].Text = p.Loading ? "Cargando…" : p.Name == "" ? "Cargar sonido" : p.Name;
            tiles[i].ToolTip = $"{i + 1:00} · {names[i].Text}\n{p.Mode} · {(p.IsCC ? "CC" : "Nota")} {p.Note} · {p.End - p.Start:0.00} s\nClic derecho: seleccionar para editar";
            names[i].Foreground = p.Samples.Length == 0 ? Brush("#B2A7C2") : Brushes.White;
            details[i].Text = $"{(p.Mode == TriggerMode.OneShot ? "ONE SHOT" : p.Mode == TriggerMode.Normal ? "NORMAL" : "ALTERNAR")}  ·  {(p.IsCC ? "CC" : "N")} {p.Note}";
            details[i].Foreground = Brush(p.Color);
            times[i].Text = pos >= 0 ? $"▶  {pos:0.0} / {p.End:0.0} s" : p.Samples.Length == 0 ? "＋  ARRASTRÁ AUDIO" : $"{p.End - p.Start:0.00} s";
        }
        for (int n = 0; n < 4; n++)
        {
            if (pageButtons[n] == null) continue;
            int loaded = pads.Skip(n * 16).Take(16).Count(p => p.Samples.Length > 0);
            bool sounding = Enumerable.Range(n * 16, 16).Any(i => positions[i] >= 0);
            pageButtons[n].Content = (PageTabs.ActualWidth < 620 ? $"{n + 1:00} · {loaded}/16" : $"{n + 1:00}   PÁGINA   {loaded}/16") + (sounding ? "  ▶" : "");
            pageButtons[n].Background = Brush(n == page ? "#362048" : "#14111B");
            pageButtons[n].BorderBrush = Brush(n == page ? "#A565F2" : "#35263F");
        }
        PeakLabel.Text = engine.Peak >= 1 ? "CLIP · bajá ganancia" : $"{20 * Math.Log10(Math.Max(engine.Master, 0.00001)):0} dB";
        PeakLabel.Foreground = engine.Peak >= 1 ? Brush("#FF79B0") : Brush("#22BCEA");
    }
    static Color ColorsWhite => System.Windows.Media.Colors.White;
    void Say(string text) => Status.Text = text;
    void Error(Exception ex) { Say(ex.Message); MessageBox.Show(this, ex.Message, "Anima Live Pad", MessageBoxButton.OK, MessageBoxImage.Warning); }
    async Task LoadAudio(int id, string path) { if (!CanEdit) return; var pad = pads[id]; pad.Loading = true; busy = true; engine.Stop(id); Say("Preparando audio…"); try { var loaded = await Task.Run(() => { if (new FileInfo(path).Length > 256L * 1024 * 1024) throw new InvalidDataException("El archivo supera 256 MB."); return (Decoder.Read(path), File.ReadAllBytes(path)); }); if (pads.Sum(p => (long)p.Samples.Length) - pad.Samples.Length + loaded.Item1.Length > 268435456) throw new InvalidDataException("El banco supera 1 GB de audio decodificado."); pad.Samples = loaded.Item1; pad.Original = loaded.Item2; pad.FileName = System.IO.Path.GetFileName(path); pad.Name = System.IO.Path.GetFileNameWithoutExtension(path); pad.Start = 0; pad.End = pad.Duration; Changed(); Say($"Cargado: {pad.FileName} · {pad.Duration:0.00} s"); } catch (Exception ex) { Error(ex); } finally { pad.Loading = false; busy = false; ShowPad(); } }
    async void ChooseAudio(object sender, RoutedEventArgs e) { if (!CanEdit) return; var dialog = new OpenFileDialog { Filter = "Audio|*.wav;*.mp3;*.aiff;*.aif;*.wma;*.m4a;*.aac;*.flac|Todos los archivos|*.*" }; if (dialog.ShowDialog() == true) await LoadAudio(selected, dialog.FileName); }
    void NameChanged(object sender, TextChangedEventArgs e) { if (!CanEdit) return; Current.Name = PadName.Text; Changed(); }
    void ModeChanged(object sender, SelectionChangedEventArgs e) { if (!CanEdit || ModeBox.SelectedIndex < 0) return; engine.Stop(selected); Current.Mode = (TriggerMode)ModeBox.SelectedIndex; Changed(); }
    void GainChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (!CanEdit) return; Current.GainDb = e.NewValue; GainLabel.Text = $"{e.NewValue:0.0} dB"; Changed(); }
    void MasterChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { engine.Master = (float)e.NewValue; }
    void VelocityChanged(object sender, RoutedEventArgs e) { if (!CanEdit) return; Current.Velocity = VelocityBox.IsChecked == true; Changed(); }
    void ToggleEditor(object sender, RoutedEventArgs e) => SetEditorVisible(EditorToggle.IsChecked == true);
    internal void SetEditorVisible(bool visible)
    {
        CommitTrim(false);
        if (compactWorkspace) compactEditorVisible = visible;
        else wideEditorVisible = visible;
        ApplyEditorLayout();
    }
    void SelectPad(int id)
    {
        if (id != selected) CommitTrim(false);
        selected = id; page = id / 16;
        ShowPad();
    }
    void TrimKey(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { CommitTrim(true); e.Handled = true; } }
    void ApplyTrim(object sender, RoutedEventArgs e) => CommitTrim(true);
    void TrimChanged(object sender, RoutedEventArgs e)
    {
        if (updating || trimUpdating) return;
        // Moving between the two fields must not reject a partially edited pair.
        Dispatcher.BeginInvoke(() => { if (Keyboard.FocusedElement != StartBox && Keyboard.FocusedElement != EndBox) CommitTrim(false); });
    }
    bool CommitTrim(bool report)
    {
        if (!CanEdit || trimUpdating || Current.Samples.Length == 0) return true;
        if (!TrimRange.TryParse(StartBox.Text, EndBox.Text, Current.Duration, out double start, out double end))
        {
            if (report) Say("Corte inválido: inicio y fin deben estar dentro del audio, con inicio menor que fin.");
            return false;
        }
        if (Math.Abs(Current.Start - start) < 0.0000001 && Math.Abs(Current.End - end) < 0.0000001) return true;
        engine.Stop(selected); Current.Start = start; Current.End = end;
        Changed(); UpdateTrimText(); DrawWave(); return true;
    }
    void UpdateTrimText()
    {
        trimUpdating = true;
        StartBox.Text = Current.Start.ToString("0.######", CultureInfo.InvariantCulture);
        EndBox.Text = Current.End.ToString("0.######", CultureInfo.InvariantCulture);
        trimUpdating = false;
    }
    void WaveSizeChanged(object sender, SizeChangedEventArgs e) => DrawWave();
    void WaveClick(object sender, MouseButtonEventArgs e)
    {
        if (!CanEdit || Current.Samples.Length == 0) return;
        CommitTrim(false);
        double x = e.GetPosition(Waveform).X, width = Math.Max(1, Waveform.ActualWidth);
        double startX = Current.Start / Current.Duration * width, endX = Current.End / Current.Duration * width;
        trimDrag = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 1 : Math.Abs(x - endX) < 12 && Math.Abs(x - endX) < Math.Abs(x - startX) ? 1 : 0;
        engine.Stop(selected); Waveform.CaptureMouse(); MoveTrim(x); e.Handled = true;
    }
    void MoveTrim(double x)
    {
        if (trimDrag < 0 || Current.Samples.Length == 0) return;
        double time = Math.Clamp(x / Math.Max(1, Waveform.ActualWidth), 0, 1) * Current.Duration;
        var range = TrimRange.Move(Current.Start, Current.End, Current.Duration, time, trimDrag == 1);
        Current.Start = range.Start; Current.End = range.End; Changed(); UpdateTrimText(); DrawWave();
    }
    void WaveMove(object sender, MouseEventArgs e) { if (trimDrag >= 0 && Waveform.IsMouseCaptured) MoveTrim(e.GetPosition(Waveform).X); }
    void WaveRelease(object sender, MouseButtonEventArgs e) { if (trimDrag < 0) return; MoveTrim(e.GetPosition(Waveform).X); trimDrag = -1; Waveform.ReleaseMouseCapture(); e.Handled = true; }
    void WaveLostCapture(object sender, MouseEventArgs e) => trimDrag = -1;
    void DrawWave()
    {
        if (Waveform == null) return;
        Waveform.Children.Clear(); double w = Waveform.ActualWidth, h = Waveform.Height;
        if (w <= 0 || Current.Samples.Length == 0)
        {
            var text = new TextBlock { Text = "FORMA DE ONDA", Foreground = Brush("#78658E"), FontSize = 11, IsHitTestVisible = false };
            Canvas.SetLeft(text, 14); Canvas.SetTop(text, 42); Waveform.Children.Add(text); return;
        }
        var p = Current; double x1 = p.Start / p.Duration * w, x2 = p.End / p.Duration * w;
        var region = new Rectangle { Width = Math.Max(0, x2 - x1), Height = h, Fill = Brush("#19112F"), IsHitTestVisible = false };
        Canvas.SetLeft(region, x1); Waveform.Children.Add(region);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            int columns = (int)w;
            for (int x = 0; x < columns; x++)
            {
                int a = (int)((long)x * p.Samples.Length / columns), b = (int)((long)(x + 1) * p.Samples.Length / columns); float peak = 0;
                int step = Math.Max(1, (b - a) / 100);
                for (int j = a; j < b; j += step) peak = Math.Max(peak, Math.Abs(p.Samples[j]));
                double amp = Math.Min(1, peak) * 32;
                context.BeginFigure(new Point(x, h / 2 - amp), false, false); context.LineTo(new Point(x, h / 2 + amp), true, false);
            }
        }
        geometry.Freeze(); Waveform.Children.Add(new System.Windows.Shapes.Path { Data = geometry, Stroke = Brush(p.Color), StrokeThickness = 1, IsHitTestVisible = false });
        foreach (var marker in new[] { (X: x1, Text: "IN", Color: "#20BFEE"), (X: x2, Text: "OUT", Color: "#BE68FF") })
        {
            double x = Math.Clamp(marker.X, 1, w - 1);
            Waveform.Children.Add(new Line { X1 = x, X2 = x, Y1 = 0, Y2 = h, Stroke = Brush(marker.Color), StrokeThickness = 2, IsHitTestVisible = false });
            var label = new TextBlock { Text = marker.Text, Background = Brush(marker.Color), Foreground = Brushes.Black, FontSize = 9, Padding = new Thickness(3, 2, 3, 2), IsHitTestVisible = false };
            Canvas.SetLeft(label, Math.Clamp(x - (marker.Text == "OUT" ? 28 : 0), 0, Math.Max(0, w - 29))); Waveform.Children.Add(label);
        }
    }
    void ClearPad(object sender, RoutedEventArgs e) { if (!CanEdit) return; engine.Stop(selected); var old = Current; pads[selected] = new Pad { Note = old.Note, Channel = old.Channel, IsCC = old.IsCC, Color = old.Color }; Changed(); ShowPad(); }
    void Panic(object sender, RoutedEventArgs e) { engine.StopAll(); cue.Stop(); heldKeys.Clear(); heldMidi.Clear(); Say("Todos los sonidos detenidos."); }
    void BuildPages()
    {
        for (int i = 0; i < 4; i++)
        {
            int index = i;
            var button = new Button { Padding = new Thickness(6, 5, 6, 5), FontSize = 11, Margin = new Thickness(0, 0, i == 3 ? 0 : 8, 0), ToolTip = $"Página {i + 1} · F{i + 1}" };
            button.Click += (_, _) => SwitchPage(index);
            pageButtons[i] = button; PageTabs.Children.Add(button);
        }
    }
    void SwitchPage(int target)
    {
        if (busy || target < 0 || target > 3) return;
        CommitTrim(false); page = target; selected = page * 16 + selected % 16;
        learn = -1; LearnButton.Content = "Aprender"; ShowPad();
    }
    void PressKey(Key key)
    {
        int local = Array.IndexOf(keys, key); if (local < 0 || heldKeys.ContainsKey(key)) return;
        int id = page * 16 + local; heldKeys[key] = id; SelectPad(id); engine.Press(id, pads[id]);
    }
    void ReleaseKey(Key key) { if (heldKeys.Remove(key, out int id)) engine.Release(id, pads[id]); }
    void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Panic(sender, e); e.Handled = true; return; }
        if (e.Key >= Key.F5 && e.Key <= Key.F7) { SwitchMode(e.Key - Key.F5); e.Handled = true; return; }
        if (mode == 1 && e.Key == Key.Space && e.OriginalSource is not (TextBox or ComboBox)) { if (!e.IsRepeat) engine.Music.TogglePause(); e.Handled = true; return; }
        if (mode != 0) return;
        if (busy) return;
        if (e.Key >= Key.F1 && e.Key <= Key.F4) { SwitchPage(e.Key - Key.F1); e.Handled = true; return; }
        if (e.OriginalSource is TextBox || e.OriginalSource is ComboBox || Keyboard.Modifiers != ModifierKeys.None) return;
        if (Array.IndexOf(keys, e.Key) < 0) return;
        if (!e.IsRepeat) PressKey(e.Key); e.Handled = true;
    }
    void OnKeyUp(object sender, KeyEventArgs e) { if (!heldKeys.ContainsKey(e.Key)) return; ReleaseKey(e.Key); e.Handled = true; }
    void LockChanged(object sender, RoutedEventArgs e) { learn = -1; LearnButton.Content = "Aprender"; foreach (var c in new Control[] { PadName, ModeBox, GainSlider, StartBox, EndBox, VelocityBox, LearnButton, OpenBankButton }) c.IsEnabled = !Locked; Colors.IsEnabled = !Locked; Say(Locked ? "Edición bloqueada. Los pads siguen activos." : "Edición habilitada."); }
    void RefreshDevices(object sender, RoutedEventArgs e) => RefreshDeviceLists();
    void RefreshDeviceLists() { refreshing = true; var previous = AudioDevices.SelectedItem as string; MidiDevices.Items.Clear(); MidiDevices.Items.Add("Sin conexión MIDI"); for (int i = 0; i < MidiIn.NumberOfDevices; i++) MidiDevices.Items.Add(MidiIn.DeviceInfo(i).ProductName); CloseMidi(); MidiDevices.SelectedIndex = 0; AudioDevices.Items.Clear(); audioIds.Clear(); try { using var enumerator = new MMDeviceEnumerator(); string? defaultId = null; try { using var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); defaultId = defaultDevice.ID; } catch { } foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)) { audioIds.Add(device.ID); AudioDevices.Items.Add(device.FriendlyName); device.Dispose(); } AudioDevices.SelectedIndex = previous != null && AudioDevices.Items.Contains(previous) ? AudioDevices.Items.IndexOf(previous) : defaultId != null && audioIds.Contains(defaultId) ? audioIds.IndexOf(defaultId) : audioIds.Count > 0 ? 0 : -1; } catch (Exception ex) { Say(ex.Message); } refreshing = false; UpdateCueDevices(); if (AudioDevices.SelectedIndex >= 0) OpenAudio(); else Say("No se encontró una salida de audio."); }
    void OpenAudio() { try { cue.Stop(); engine.Open(audioIds[AudioDevices.SelectedIndex], audioOptions); Say(engine.OutputDescription); } catch (Exception ex) { Error(ex); } }
    void AudioChanged(object sender, SelectionChangedEventArgs e) { if (!refreshing && AudioDevices.SelectedIndex >= 0) OpenAudio(); }
    void CloseMidi() { if (midi != null) { try { midi.Stop(); } catch { } midi.Dispose(); midi = null; } foreach (var pair in heldMidi) foreach (int i in pair.Value) engine.Release(i, pads[i]); heldMidi.Clear(); }
    void MidiChanged(object sender, SelectionChangedEventArgs e) { if (refreshing) return; CloseMidi(); int index = MidiDevices.SelectedIndex - 1; if (index < 0) return; try { midi = new MidiIn(index); midi.MessageReceived += MidiReceived; midi.ErrorReceived += (_, _) => Dispatcher.BeginInvoke(() => Say("Se recibió un mensaje MIDI inválido.")); midi.Start(); Say("MIDI conectado: " + MidiDevices.SelectedItem); } catch (Exception ex) { CloseMidi(); Error(ex); } }
    void Learn(object sender, RoutedEventArgs e) { if (!CanEdit) return; if (midi == null) { Say("Seleccioná un dispositivo MIDI antes de aprender."); return; } learn = learn == selected ? -1 : selected; LearnButton.Content = learn < 0 ? "Aprender" : "Esperando…"; Say(learn < 0 ? "Aprendizaje cancelado." : "Presioná un pad MIDI. Se guarda la nota o CC y el canal."); }
    void MidiReceived(object? sender, MidiInMessageEventArgs e) { int raw = e.RawMessage; Dispatcher.BeginInvoke(() => HandleMidi(raw)); }
    void HandleMidi(int raw)
    {
        int command = raw & 0xF0, channel = (raw & 15) + 1, number = (raw >> 8) & 127, value = (raw >> 16) & 127; bool cc = command == 0xB0; if (command != 0x90 && command != 0x80 && !cc) return; bool on = cc ? value >= 64 : command == 0x90 && value > 0; int key = (cc ? 65536 : 0) + channel * 128 + number;
        if (learn >= 0 && on && !busy && !Locked) { var p = pads[learn]; p.Note = number; p.Channel = channel; p.IsCC = cc; learn = -1; LearnButton.Content = "Aprender"; Changed(); ShowPad(); Say(pads.Where((p, i) => i / 16 != page).Any(other => other.Note == p.Note && other.IsCC == p.IsCC && (other.Channel == 0 || other.Channel == p.Channel)) ? "Mensaje repetido en otra página: usá notas o canales distintos para seguir escenas." : "Asignación MIDI guardada."); heldMidi[key] = Array.Empty<int>(); return; }
        if (on)
        {
            if (busy || heldMidi.ContainsKey(key)) return; var matches = Enumerable.Range(0, 64).Where(i => pads[i].Note == number && pads[i].IsCC == cc && (pads[i].Channel == 0 || pads[i].Channel == channel)).ToArray();
            int target = matches.Any(i => i / 16 == page) ? page : matches.Length > 0 && FollowMidi.IsChecked == true ? matches[0] / 16 : page;
            var ids = matches.Where(i => i / 16 == target).ToArray(); heldMidi[key] = ids;
            if (target != page && Keyboard.FocusedElement is not TextBox) SwitchPage(target);
            foreach (int i in ids) { midiFlashUntil[i] = System.Diagnostics.Stopwatch.GetTimestamp() + (long)(.2 * System.Diagnostics.Stopwatch.Frequency); engine.Press(i, pads[i], cc ? 127 : value); }
            if (ids.Length > 0 && PadFocus.IsChecked == true && !Waveform.IsMouseCaptured)
            {
                if (ids[0] != selected) SelectPad(ids[0]); else Tick();
            }
        }
        else if (heldMidi.Remove(key, out var ids)) foreach (int i in ids) engine.Release(i, pads[i]);
    }
    bool ConfirmDiscard() { if (busy) { Say("Esperá a que termine la carga o el guardado."); return false; } return !dirty || MessageBox.Show(this, "Hay cambios sin guardar. ¿Querés descartarlos?", "Anima Live Pad", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes; }
    async void SaveBank(object sender, RoutedEventArgs e) { if (busy || updating) return; var dialog = new SaveFileDialog { Filter = "Banco Anima Live Pad|*.anima", DefaultExt = ".anima", FileName = "Mi banco.anima" }; if (dialog.ShowDialog() != true) return; busy = true; learn = -1; LearnButton.Content = "Aprender"; Say("Guardando banco con sus audios…"); try { await Task.Run(() => BankFile.Save(dialog.FileName, pads)); dirty = false; Title = "Anima Live · 0.6 · " + System.IO.Path.GetFileNameWithoutExtension(dialog.FileName); BankTitle.Text = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName).ToUpperInvariant(); Say("Banco guardado. Incluye los archivos de audio."); } catch (Exception ex) { Error(ex); } finally { busy = false; } }
    async void OpenBank(object sender, RoutedEventArgs e) { if (!CanEdit || !ConfirmDiscard()) return; var dialog = new OpenFileDialog { Filter = "Bancos compatibles|*.anima;*.airepads" }; if (dialog.ShowDialog() != true) return; busy = true; engine.StopPads(); learn = -1; LearnButton.Content = "Aprender"; Say("Cargando banco…"); try { var next = await Task.Run(() => BankFile.Load(dialog.FileName)); pads = next; page = 0; selected = 0; heldMidi.Clear(); heldKeys.Clear(); dirty = false; Title = "Anima Live · 0.6 · " + System.IO.Path.GetFileNameWithoutExtension(dialog.FileName); BankTitle.Text = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName).ToUpperInvariant(); ShowPad(); Say("Banco cargado y listo."); } catch (Exception ex) { Error(ex); } finally { busy = false; } }
}

