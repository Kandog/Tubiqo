using System.Globalization;

namespace Tubiqo.Core.Storage;

/// <summary>The usable screen area (work area), in WPF units.</summary>
public sealed record ScreenArea(double Left, double Top, double Width, double Height);

/// <summary>Where the window was and whether it was maximized.</summary>
public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool Maximized)
{
    /// <summary>Moves and resizes the window so it is fully on <paramref name="screen"/> (a monitor may have been unplugged).</summary>
    public WindowPlacement FitTo(ScreenArea screen, double minWidth, double minHeight)
    {
        var w = Math.Min(Math.Max(Width, minWidth), screen.Width);
        var h = Math.Min(Math.Max(Height, minHeight), screen.Height);
        var left = Math.Clamp(Left, screen.Left, screen.Left + screen.Width - w);
        var top = Math.Clamp(Top, screen.Top, screen.Top + screen.Height - h);
        return new WindowPlacement(left, top, w, h, Maximized);
    }

    internal string Format() => string.Join(",",
        new[] { Left, Top, Width, Height }.Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + (Maximized ? ",1" : ",0");

    internal static WindowPlacement? Parse(string? s)
    {
        var parts = s?.Split(',');
        if (parts is not { Length: 5 }) return null;
        var n = new double[4];
        for (var i = 0; i < 4; i++)
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out n[i]) || !double.IsFinite(n[i])) return null;
        if (n[2] <= 0 || n[3] <= 0 || parts[4] is not ("0" or "1")) return null;
        return new WindowPlacement(n[0], n[1], n[2], n[3], parts[4] == "1");
    }
}

/// <summary>Tubiqo's own small preferences, kept in Tubiqo Data.</summary>
public sealed class AppPreferences(TubiqoDatabase db)
{
    public WindowPlacement? WindowPlacement
    {
        get => WindowPlacement.Parse(db.GetSetting("window.placement"));
        set { if (value != null) db.SetSetting("window.placement", value.Format()); }
    }

    /// <summary>Player volume, 0–100.</summary>
    public int Volume
    {
        get => int.TryParse(db.GetSetting("player.volume"), out var v) ? Math.Clamp(v, 0, 100) : 100;
        set => db.SetSetting("player.volume", Math.Clamp(value, 0, 100).ToString(CultureInfo.InvariantCulture));
    }

    public bool Muted
    {
        get => db.GetSetting("player.muted") == "1";
        set => db.SetSetting("player.muted", value ? "1" : "0");
    }

    /// <summary>Off by default: playback keeps going (as audio) when Tubiqo is minimized.</summary>
    public bool PauseWhenMinimized
    {
        get => db.GetSetting("player.pauseWhenMinimized") == "1";
        set => db.SetSetting("player.pauseWhenMinimized", value ? "1" : "0");
    }
}
