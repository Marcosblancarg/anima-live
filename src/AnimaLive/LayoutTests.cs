using System.Windows;
using System.Windows.Controls;

namespace src;

public partial class MainWindow
{
    internal void ArrangePreview(double width, double height)
    {
        Width = width; Height = height;
        var root = (FrameworkElement)Content;
        // Reserve the standard Windows frame/title bar; the tested area is the usable client area.
        var size = new Size(Math.Max(1, width - 16), Math.Max(1, height - 39));
        root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
        UpdateResponsiveLayout(); root.UpdateLayout();
        Tick();
    }

    void TestResponsiveLayout()
    {
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        Rect Bounds(FrameworkElement e) => e.TransformToAncestor(LayoutRoot).TransformBounds(new Rect(e.RenderSize));
        bool Overlap(FrameworkElement a, FrameworkElement b) { var intersection = Rect.Intersect(Bounds(a), Bounds(b)); return !intersection.IsEmpty && intersection.Width > 1 && intersection.Height > 1; }
        void Inside(FrameworkElement e, string message)
        {
            var b = Bounds(e);
            Check(b.Left >= -1 && b.Top >= -1 && b.Right <= LayoutRoot.ActualWidth + 1 && b.Bottom <= LayoutRoot.ActualHeight + 1, message + $" ({b}; raíz {LayoutRoot.RenderSize})");
        }
        foreach (var size in new[] { new Size(1120, 650), new Size(1280, 680), new Size(1000, 600), new Size(900, 550), new Size(760, 500), new Size(640, 480) })
        {
            ArrangePreview(size.Width, size.Height);
            Check(MixMeter.ActualWidth >= 70 && MixMeter.ActualHeight >= 150, "Vúmetro legible a " + size);
            Inside(MeterPanel, "Vúmetro dentro de la ventana a " + size);
            Inside(PanicButton, "Detener todo accesible a " + size);
            Inside(HeaderActions, "Barra de acciones dentro de la ventana a " + size);
            Inside(PageTabs, "Cuatro páginas accesibles a " + size);
            Check(!Overlap(BrandPanel, PanicButton), "Logo y detener todo sin superposición a " + size);
            Check(!Overlap(HeaderActions, PanicButton), "Acciones y detener todo sin superposición a " + size);
            Check(PadsPanel.Visibility == Visibility.Visible, "Pads visibles al reducir la ventana a " + size);
            Check(PadScroll.ScrollableHeight <= 1, "Los 16 pads deben caber sin scroll en la vista de vivo a " + size);
            for (int i = 0; i < 16; i++)
            {
                Inside(tiles[i], "Pad dentro de la ventana a " + size);
                Check(tiles[i].ActualWidth >= 100 && tiles[i].ActualHeight >= 54, "Área de disparo utilizable a " + size);
                Check(names[i].ActualHeight >= 14, "Nombre del pad visible a " + size);
            }
            double previousWidth = Width, previousHeight = Height;
            SetEditorVisible(true); LayoutRoot.UpdateLayout();
            Check(Width == previousWidth && Height == previousHeight && MinWidth == 640, "Abrir editor no debe agrandar la ventana");
            Inside(EditorPanel, "Editor dentro de la ventana a " + size);
            Check(EditorScroll.ActualWidth >= 240 && EditorScroll.ActualHeight >= 150, "Editor utilizable con scroll a " + size);
            if (compactWorkspace)
            {
                Check(PadsPanel.Visibility == Visibility.Collapsed && BackToPads.Visibility == Visibility.Visible, "Vistas alternables en ventana compacta");
                foreach (var control in new FrameworkElement[] { GainSlider, StartBox, EndBox, ModeBox })
                    Check(Bounds(control).Top >= Bounds(EditorScroll).Top - 1 && Bounds(control).Bottom <= Bounds(EditorScroll).Bottom + 1, "Ganancia, cortes y modo visibles sin scroll en el editor compacto a " + size);
                PadFocus.IsChecked = true; HandleMidi(0x90 | (39 << 8) | (100 << 16)); HandleMidi(0x80 | (39 << 8));
                Check(selected == 3 && EditorPanel.Visibility == Visibility.Visible, "Pad Focus funciona en editor compacto");
                SetEditorVisible(false); LayoutRoot.UpdateLayout();
                Check(PadsPanel.Visibility == Visibility.Visible, "Volver a los pads sin cambiar dimensiones");
            }
            else Check(!Overlap(PadsPanel, EditorPanel), "Editor lateral no invade los pads");
        }
        var fitted = FitSize(new Size(1120, 650), new Size(1334, 696));
        Check(fitted.Width == 1120 && fitted.Height == 650, "La apertura debe caber en el área de trabajo de 1366x768");
        fitted = FitSize(new Size(1800, 1000), new Size(1050, 540));
        Check(fitted.Width == 1050 && fitted.Height == 540, "Adaptar ventana recordada al área disponible con escalado");
        fitted = FitSize(new Size(1120, 650), new Size(840, 440));
        Check(fitted.Width == 840 && fitted.Height == 440, "El área de trabajo prevalece sobre el mínimo con escalado alto");
        ArrangePreview(1120, 650); SetEditorVisible(true);
        foreach (var size in new[] { new Size(1120, 650), new Size(900, 550), new Size(640, 480) })
        {
            SwitchMode(1); ArrangePreview(size.Width, size.Height);
            Inside(ModeTabs, "Pestañas visibles"); Inside(ClockLabel, "Reloj dentro de la ventana");
            Inside(PlayerPanel, "Reproductor dentro de la ventana"); Inside(MusicQueue, "Main Music dentro de la ventana");
            Check(MusicQueue.ActualHeight >= 90 && MusicQueue.ActualWidth >= 250, "Cola legible en ventana compacta a " + size + ": " + MusicQueue.RenderSize);
            Check(MainArea.Visibility == Visibility.Collapsed && PlayerPanel.Visibility == Visibility.Visible, "Vista del reproductor seleccionada");
            SwitchMode(2); LayoutRoot.UpdateLayout();
            Inside(MixerPanel, "Mixer dentro de la ventana"); Inside(PadFader, "Fader de pads dentro de la ventana"); Inside(MusicFader, "Fader de música dentro de la ventana");
            Check(PadChannelMeter.ActualHeight >= 150 && MusicChannelMeter.ActualHeight >= 150, "Vúmetros de mixer legibles a " + size);
            Check(MixMeter.ActualHeight >= 150, "Master accesible en mixer");
        }
        pads[4] = new Pad { Samples = new float[96000], End = 1, Note = 40 };
        SwitchMode(1); HandleMidi(0x90 | (40 << 8) | (100 << 16));
        Check(engine.Position(4) >= 0, "MIDI funciona en la pestaña Reproductor");
        SwitchMode(2); Check(engine.Position(4) >= 0, "Cambiar pestaña conserva One Shot");
        engine.StopAll(); SwitchMode(0); ArrangePreview(1120, 650);
    }
}
