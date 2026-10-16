using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace src;

public partial class MainWindow
{
    bool compactWorkspace, compactControls;
    bool wideEditorVisible = true, compactEditorVisible;
    bool wideDevicesVisible = true, compactDevicesVisible;
    bool applyingLayout, windowWasLoaded;
    WindowLayout savedLayout = WindowLayout.Load();

    void InitializeResponsiveLayout()
    {
        Width = savedLayout.Width; Height = savedLayout.Height;
        wideEditorVisible = savedLayout.EditorVisible;
        LayoutRoot.SizeChanged += (_, _) => UpdateResponsiveLayout();
        EditorPanel.SizeChanged += (_, _) => ArrangeEditorFields();
        SourceInitialized += (_, _) => FitWindowToWorkArea();
        Loaded += (_, _) => windowWasLoaded = true;
        Closed += (_, _) => { if (windowWasLoaded) SaveWindowLayout(); };
        UpdateResponsiveLayout(Width - 32, Height - 63);
    }

    internal void UpdateResponsiveLayout(double width = 0, double height = 0)
    {
        if (applyingLayout) return;
        if (width <= 0) width = LayoutRoot.ActualWidth;
        if (height <= 0) height = LayoutRoot.ActualHeight;
        if (width <= 0 || height <= 0) return;
        applyingLayout = true;
        try
        {
            bool nextCompact = width < 1000;
            if (nextCompact != compactWorkspace) CommitTrim(false);
            compactWorkspace = nextCompact;
            compactControls = compactWorkspace || height < 510;
            Grid.SetRow(HeaderActions, compactWorkspace ? 1 : 0);
            Grid.SetColumn(HeaderActions, compactWorkspace ? 0 : 1);
            Grid.SetColumnSpan(HeaderActions, compactWorkspace ? 5 : 1);
            HeaderActions.Visibility = !compactWorkspace || compactToolsVisible ? Visibility.Visible : Visibility.Collapsed;
            ToolsButton.Visibility = compactWorkspace ? Visibility.Visible : Visibility.Collapsed;
            HeaderActions.Margin = new Thickness(0, compactWorkspace ? 6 : 0, 0, 0);
            Grid.SetColumn(BrandPanel, 0);
            Grid.SetColumnSpan(BrandPanel, 1);
            DevicesToggle.IsChecked = compactControls ? compactDevicesVisible : wideDevicesVisible;
            DevicePanel.Visibility = DevicesToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            // On narrow windows each device gets the full row instead of fixed-width selectors.
            bool stackDevices = width < 760;
            Grid.SetColumn(AudioGroup, stackDevices ? 0 : 1);
            Grid.SetRow(AudioGroup, stackDevices ? 1 : 0);
            Grid.SetColumnSpan(AudioGroup, stackDevices ? 2 : 1);
            Grid.SetColumnSpan(MidiGroup, stackDevices ? 2 : 1);
            MidiGroup.Margin = new Thickness(0, 0, stackDevices ? 0 : 14, stackDevices ? 6 : 0);
            MeterColumn.Width = new GridLength(width < 760 ? 90 : 100);
            PadHint.Visibility = height < 520 ? Visibility.Collapsed : Visibility.Visible;
            ApplyEditorLayout();
            UpdateMusicLayout();
            for (int i = 0; i < 16; i++) UpdatePadLayout(i);
            Tick();
        }
        finally { applyingLayout = false; }
    }

    void ApplyEditorLayout()
    {
        bool visible = compactWorkspace ? compactEditorVisible : wideEditorVisible;
        EditorPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        EditorColumn.Width = new GridLength(visible && !compactWorkspace ? 280 : 0);
        EditorGap.Width = new GridLength(visible && !compactWorkspace ? 12 : 0);
        Grid.SetColumn(EditorPanel, compactWorkspace ? 0 : 2);
        PadsPanel.Visibility = visible && compactWorkspace ? Visibility.Collapsed : Visibility.Visible;
        BackToPads.Visibility = compactWorkspace ? Visibility.Visible : Visibility.Collapsed;
        EditorToggle.IsChecked = visible;
        EditorToggle.ToolTip = compactWorkspace ? "Cambiar entre los pads y el editor sin agrandar la ventana." : "Mostrar u ocultar el editor al lado de los pads.";
    }

    void ArrangeEditorFields()
    {
        bool split = EditorPanel.ActualWidth >= 450;
        Grid.SetColumn(PlaybackGroup, split ? 2 : 0);
        Grid.SetRow(PlaybackGroup, split ? 0 : 1);
        PlaybackColumn.Width = split ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        EditorFieldsGap.Width = new GridLength(split ? 14 : 0);
        PlaybackGroup.Margin = new Thickness(0, split ? 0 : 10, 0, 0);
    }

    void BackToPadsClick(object sender, RoutedEventArgs e) => SetEditorVisible(false);
    void ToggleDevices(object sender, RoutedEventArgs e)
    {
        if (compactControls) compactDevicesVisible = DevicesToggle.IsChecked == true;
        else wideDevicesVisible = DevicesToggle.IsChecked == true;
        DevicePanel.Visibility = DevicesToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    void UpdatePadLayout(int i)
    {
        if (tiles[i] == null || faces[i] == null) return;
        double height = tiles[i].ActualHeight, width = tiles[i].ActualWidth;
        bool shortPad = height > 0 && height < 82;
        bool compactPad = height > 0 && height < 125 || width > 0 && width < 160;
        var grid = (Grid)faces[i].Child;
        grid.Margin = shortPad ? new Thickness(7, 3, 7, 4) : compactPad ? new Thickness(9, 5, 9, 6) : new Thickness(14, 10, 14, 12);
        names[i].FontSize = shortPad ? 12 : compactPad ? 14 : 17;
        names[i].TextWrapping = height >= 105 ? TextWrapping.Wrap : TextWrapping.NoWrap;
        names[i].TextTrimming = TextTrimming.CharacterEllipsis;
        names[i].MaxHeight = height >= 105 ? 48 : 22;
        details[i].TextTrimming = TextTrimming.CharacterEllipsis;
        details[i].FontSize = compactPad ? 9 : 10;
        details[i].Visibility = shortPad ? Visibility.Collapsed : Visibility.Visible;
        times[i].Visibility = compactPad ? Visibility.Collapsed : Visibility.Visible;
        var header = (DockPanel)grid.Children[0];
        ((TextBlock)header.Children[0]).FontSize = shortPad ? 12 : compactPad ? 13 : 16;
        ((Border)header.Children[1]).Padding = new Thickness(shortPad ? 4 : 6, 1, shortPad ? 4 : 6, 1);
    }

    internal static Size FitSize(Size requested, Size available) => new(
        Math.Min(Math.Max(640, requested.Width), Math.Max(1, available.Width)),
        Math.Min(Math.Max(480, requested.Height), Math.Max(1, available.Height)));

    void FitWindowToWorkArea()
    {
        // Native monitor coordinates are pixels; WPF window dimensions are device-independent units.
        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        var area = SystemParameters.WorkArea;
        if (GetMonitorInfo(MonitorFromWindow(source.Handle, 2), ref info))
        {
            var transform = source.CompositionTarget.TransformFromDevice;
            var topLeft = transform.Transform(new Point(info.Work.Left, info.Work.Top));
            var bottomRight = transform.Transform(new Point(info.Work.Right, info.Work.Bottom));
            area = new Rect(topLeft, bottomRight);
        }
        var available = new Size(Math.Max(1, area.Width - 32), Math.Max(1, area.Height - 32));
        var fit = FitSize(new Size(Width, Height), available);
        MinWidth = Math.Min(640, available.Width); MinHeight = Math.Min(480, available.Height);
        Width = fit.Width; Height = fit.Height;
        Left = area.Left + (area.Width - Width) / 2; Top = area.Top + (area.Height - Height) / 2;
    }

    void SaveWindowLayout()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        new WindowLayout { Width = bounds.Width, Height = bounds.Height, EditorVisible = wideEditorVisible }.Save();
    }

    [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}

public sealed class WindowLayout
{
    public double Width { get; set; } = 1120;
    public double Height { get; set; } = 650;
    public bool EditorVisible { get; set; } = true;
    static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "window-settings.json");
    public static WindowLayout Load()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<WindowLayout>(File.ReadAllText(SettingsPath));
            if (settings != null && double.IsFinite(settings.Width) && double.IsFinite(settings.Height) && settings.Width >= 640 && settings.Height >= 480) return settings;
        }
        catch { }
        return new WindowLayout();
    }
    public void Save()
    {
        try
        {
            File.WriteAllText(SettingsPath + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(SettingsPath + ".tmp", SettingsPath, true);
        }
        catch { /* Window preferences must never prevent the live app from closing. */ }
    }
}
