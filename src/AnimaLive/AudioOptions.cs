using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace src;

public sealed class AudioOptions
{
    public int BufferFrames { get; set; } = 1440;
    public bool EventSync { get; set; } = true;
    public int LatencyMs => (int)Math.Ceiling(BufferFrames / 48.0);
    public static readonly int[] AllowedFrames = { 256, 512, 1024, 1440, 2048, 4096, 8192 };
    static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "audio-settings.json");
    public static AudioOptions Load()
    {
        try { var settings = JsonSerializer.Deserialize<AudioOptions>(File.ReadAllText(SettingsPath)); if (settings != null && AllowedFrames.Contains(settings.BufferFrames)) return settings; } catch { }
        return new AudioOptions();
    }
    public void Save()
    {
        var temp = SettingsPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, SettingsPath, true);
    }
}

public sealed class AudioSettingsWindow : Window
{
    public AudioOptions Result { get; private set; }
    public AudioSettingsWindow(Window owner, AudioOptions current, string device, string description)
    {
        if (owner.IsVisible) Owner = owner;
        Title = "Anima · Ajustes de audio"; Width = 480; Height = 540; MinWidth = 360; MinHeight = 320;
        ResizeMode = ResizeMode.CanResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(16, 16, 22)); Foreground = Brushes.White; Resources = owner.Resources;
        Result = current;
        var panel = new StackPanel { Margin = new Thickness(20) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        if (owner.IsVisible) { Width = Math.Min(Width, owner.ActualWidth); Height = Math.Min(Height, owner.ActualHeight); }
        void Label(string text, int size = 12) => panel.Children.Add(new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12), Foreground = Brushes.LightGray });
        Label("SALIDA DE AUDIO", 18); Label(device); Label(description, 11);
        Label("Búfer solicitado · muestras a 48 kHz");
        var frames = new ComboBox { Margin = new Thickness(0, 0, 0, 16) };
        foreach (int n in AudioOptions.AllowedFrames) frames.Items.Add($"{n} muestras  ·  {n / 48.0:0.0} ms");
        frames.SelectedIndex = Array.IndexOf(AudioOptions.AllowedFrames, current.BufferFrames); panel.Children.Add(frames);
        Label("Sincronización WASAPI");
        var sync = new ComboBox { Margin = new Thickness(0, 0, 0, 16) };
        sync.Items.Add("Por eventos · predeterminado"); sync.Items.Add("Temporizada · compatibilidad"); sync.SelectedIndex = current.EventSync ? 0 : 1; panel.Children.Add(sync);
        Label("Más búfer da más margen al equipo, con mayor demora al disparar. Estos ajustes son de la app; el driver y Windows determinan el búfer efectivo. No modifican el panel ASIO de Audient.", 11);
        Label("Aplicar detiene los sonidos y reinicia la salida seleccionada.", 11);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancelar", IsCancel = true };
        var apply = new Button { Content = "Aplicar", IsDefault = true, Margin = new Thickness(0) };
        apply.Click += (_, _) => { Result = new AudioOptions { BufferFrames = AudioOptions.AllowedFrames[frames.SelectedIndex], EventSync = sync.SelectedIndex == 0 }; DialogResult = true; };
        buttons.Children.Add(cancel); buttons.Children.Add(apply); panel.Children.Add(buttons);
    }
}

public partial class MainWindow
{
    void ShowAudioSettings(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var dialog = new AudioSettingsWindow(this, audioOptions, AudioDevices.SelectedItem?.ToString() ?? "Sin salida", engine.OutputDescription);
        if (dialog.ShowDialog() != true) return;
        if (AudioDevices.SelectedIndex < 0) { Say("Seleccioná una salida de audio primero."); return; }
        var previous = audioOptions;
        cue.Stop();
        try
        {
            engine.Open(audioIds[AudioDevices.SelectedIndex], dialog.Result); audioOptions = dialog.Result;
            try { audioOptions.Save(); Say(engine.OutputDescription); }
            catch (Exception ex) { Say(engine.OutputDescription + " · Ajustes activos; no se pudieron guardar: " + ex.Message); }
        }
        catch (Exception ex)
        {
            try { engine.Open(audioIds[AudioDevices.SelectedIndex], previous); } catch { }
            Error(ex);
        }
    }
}
