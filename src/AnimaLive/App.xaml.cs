using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace src;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--self-test"))
        {
            try { SelfTests.Run(!e.Args.Contains("--offline")); File.WriteAllText(e.Args[1], "PASS: pads WAV/MP3, modos, recorte, ganancia, mezcla, MIDI, páginas, bancos, vúmetro, Pad Focus y búfer WASAPI.\nPASS: música desde disco, archivo de 16 minutos con memoria limitada, precarga, avance sin huecos, crossfade estéreo, pausa, búsqueda, repetición, parada al terminar y archivos faltantes.\nPASS: M3U8/PLS con Unicode, mixer independiente, mute, faders y pisado de música.\nPASS: pestañas Pads/Reproductor/Mixer, reloj y distribución entre 640x480 y 1280x680; 16 pads sin scroll en vista de vivo.\n" + SelfTests.HardwareReport); Shutdown(0); } catch (Exception ex) { File.WriteAllText(e.Args[1], ex.ToString()); Shutdown(1); }
            return;
        }
        base.OnStartup(e);
        if (e.Args.Contains("--preview"))
        {
            var window = new MainWindow(); MainWindow = window;
            window.MakeDemo();
            window.MakeMusicDemo();
            if (e.Args.Contains("--player")) window.SwitchMode(1);
            if (e.Args.Contains("--mixer")) window.SwitchMode(2);
            bool compact = e.Args.Contains("--compact"), settings = e.Args.Contains("--settings");
            if (compact) window.SetEditorVisible(false);
            double Option(string name, double fallback) { int at = Array.IndexOf(e.Args, name); return at >= 0 && at + 1 < e.Args.Length && double.TryParse(e.Args[at + 1], out var n) && n > 0 ? n : fallback; }
            double width = Option("--width", settings ? 480 : compact ? 760 : 1120), height = Option("--height", settings ? 540 : compact ? 500 : 650);
            if (!settings) { window.ArrangePreview(width, height); if (e.Args.Contains("--editor")) { window.SetEditorVisible(true); window.ArrangePreview(width, height); } width -= 16; height -= 39; }
            FrameworkElement root = settings ? (FrameworkElement)new AudioSettingsWindow(window, new AudioOptions(), "Salida seleccionada", "WASAPI compartido · estéreo de 48 kHz").Content : (FrameworkElement)window.Content;
            root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
            var background = new DrawingVisual(); using (var dc = background.RenderOpen()) dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(7, 7, 10)), null, new Rect(0, 0, width, height)); bitmap.Render(background); bitmap.Render(root);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using (var stream = File.Create(e.Args[1])) png.Save(stream);
            Shutdown(); return;
        }
        new MainWindow().Show();
    }
}

