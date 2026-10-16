using System.Globalization;

namespace src;

public static class TrimRange
{
    public const double Frame = 1.0 / 48000;
    public static bool TryParse(string startText, string endText, double duration, out double start, out double end)
    {
        start = end = 0;
        if (!double.TryParse(startText.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var a) ||
            !double.TryParse(endText.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var b) ||
            !double.IsFinite(a) || !double.IsFinite(b) || a < 0 || b > duration + Frame / 2 || b <= a) return false;
        start = Math.Round(a * 48000) / 48000;
        end = Math.Min(duration, Math.Round(b * 48000) / 48000);
        return end - start >= Frame - 1e-10;
    }
    public static (double Start, double End) Move(double start, double end, double duration, double time, bool moveEnd)
    {
        time = Math.Round(Math.Clamp(time, 0, duration) * 48000) / 48000;
        return moveEnd ? (start, Math.Clamp(time, Math.Min(duration, start + Frame), duration)) : (Math.Clamp(time, 0, Math.Max(0, end - Frame)), end);
    }
}
