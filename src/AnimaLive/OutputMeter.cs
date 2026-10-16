using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace src;

public readonly record struct MeterReading(float Left, float Right, double RmsLeft, double RmsRight, bool Clipped);

// The audio thread supplies accumulated measurements; all drawing stays on the UI thread.
public sealed class OutputMeter : FrameworkElement
{
    readonly double[] levels = { -60, -60 }, holds = { -60, -60 }, rms = { -60, -60 };
    readonly double[] holdSeconds = new double[2];
    double clipSeconds;
    public string Caption { get; set; } = "MASTER";
    public string Footer { get; set; } = "PADS + MÚSICA";
    public string PositionLabel { get; set; } = "POST MASTER";
    Brush low = Cyan;
    public string LowColor { set { low = Ink(value); InvalidateVisual(); } }
    public OutputMeter()
    {
        ToolTip = "Nivel estéreo en dBFS. Barras: pico; marca blanca: RMS; raya superior: pico retenido 1,5 s. Clic para borrar picos y CLIP.";
        Cursor = Cursors.Hand;
        MouseLeftButtonDown += (_, _) => { clipSeconds = 0; for (int i = 0; i < 2; i++) { holds[i] = -60; holdSeconds[i] = 0; } InvalidateVisual(); };
    }
    public static double ToDb(double value) => value > 0 ? Math.Clamp(20 * Math.Log10(value), -60, 0) : -60;
    public void Update(MeterReading reading, double elapsed)
    {
        elapsed = Math.Clamp(elapsed, 0, 1);
        for (int i = 0; i < 2; i++)
        {
            double db = ToDb(i == 0 ? reading.Left : reading.Right);
            levels[i] = Math.Max(db, levels[i] - 24 * elapsed);
            rms[i] = ToDb(i == 0 ? reading.RmsLeft : reading.RmsRight);
            if (db >= holds[i]) { holds[i] = db; holdSeconds[i] = 1.5; }
            else { holdSeconds[i] -= elapsed; if (holdSeconds[i] <= 0) holds[i] = Math.Max(db, holds[i] - 12 * elapsed); }
        }
        clipSeconds = reading.Clipped ? 3 : Math.Max(0, clipSeconds - elapsed);
        InvalidateVisual();
    }
    static SolidColorBrush Ink(string hex) { var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!; brush.Freeze(); return brush; }
    static readonly Brush Muted = Ink("#978CA8"), White = Ink("#F7F4FF"), Cyan = Ink("#20BFEE"), Yellow = Ink("#F1C469"), Red = Ink("#FF587F"), Off = Ink("#24202E");
    void Text(DrawingContext dc, string text, double x, double y, double size, Brush brush)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(formatted, new Point(x, y));
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double width = ActualWidth, height = ActualHeight;
        if (width < 70 || height < 150) return;
        bool compact = height < 330;
        Text(dc, Caption, 6, 0, 11, White);
        Text(dc, "dBFS", 6, compact ? 16 : 21, 10, Muted);
        double clipTop = compact ? 32 : 45;
        dc.DrawRoundedRectangle(clipSeconds > 0 ? Red : Off, null, new Rect(6, clipTop, width - 12, 21), 3, 3);
        Text(dc, "CLIP", (width - 25) / 2, clipTop + 3, 10, clipSeconds > 0 ? White : Muted);
        double top = compact ? 76 : 95, bottom = height - (compact ? 40 : 99), barHeight = bottom - top;
        double barWidth = Math.Max(13, (width - 50) / 2);
        double left = 6, right = left + barWidth + 7;
        Text(dc, "L", left + barWidth / 2 - 3, top - 19, 10, Muted);
        Text(dc, "R", right + barWidth / 2 - 3, top - 19, 10, Muted);
        double Y(double db) => bottom - (db + 60) / 60 * barHeight;
        for (int channel = 0; channel < 2; channel++)
        {
            double x = channel == 0 ? left : right;
            int segments = compact ? 24 : 40;
            for (int segment = 0; segment < segments; segment++)
            {
                double db = -60 + (segment + 1) * (60.0 / segments);
                Brush color = db > -3 ? Red : db > -12 ? Yellow : low;
                bool lit = levels[channel] > -60 && levels[channel] >= db - 60.0 / segments;
                dc.DrawRectangle(lit ? color : Off, null, new Rect(x, Y(db), barWidth, Math.Max(1, barHeight / segments - 1)));
            }
            if (holds[channel] > -60) dc.DrawRectangle(White, null, new Rect(x, Y(holds[channel]), barWidth, 2));
            if (rms[channel] > -60) dc.DrawRectangle(White, null, new Rect(x + barWidth / 2 - 2, Y(rms[channel]), 4, 3));
            Text(dc, holds[channel] > -60 ? holds[channel].ToString("0.0", CultureInfo.InvariantCulture) : "−∞", x - 1, bottom + 9, 10, White);
        }
        foreach (int db in compact ? new[] { 0, -12, -24, -36, -48, -60 } : new[] { 0, -3, -6, -12, -18, -24, -36, -48, -60 })
            Text(dc, db.ToString(CultureInfo.InvariantCulture), right + barWidth + 5, Y(db) - 6, 9, Muted);
        if (!compact)
        {
            Text(dc, "PICO / L · R", 6, bottom + 29, 9, Muted);
            Text(dc, PositionLabel, 6, height - 34, 8, Muted);
            Text(dc, Footer, 6, height - 18, 7, Muted);
        }
        else Text(dc, Footer, 6, height - 13, 7, Muted);
    }
}
