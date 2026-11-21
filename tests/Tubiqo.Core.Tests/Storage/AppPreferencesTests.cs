using Tubiqo.Core.Storage;

namespace Tubiqo.Core.Tests.Storage;

public sealed class AppPreferencesTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "tubiqo-tests-" + Guid.NewGuid().ToString("N"));
    readonly TubiqoDatabase db;

    public AppPreferencesTests()
    {
        Directory.CreateDirectory(dir);
        db = TubiqoDatabase.Open(Path.Combine(dir, "tubiqo.db"));
    }

    public void Dispose()
    {
        db.Dispose();
        Directory.Delete(dir, recursive: true);
    }

    static readonly ScreenArea Screen = new(0, 0, 1920, 1040);

    [Fact]
    public void Defaults_before_anything_is_saved()
    {
        var p = new AppPreferences(db);
        Assert.Null(p.WindowPlacement);
        Assert.Equal(100, p.Volume);
        Assert.False(p.Muted);
        Assert.False(p.PauseWhenMinimized);
    }

    [Fact]
    public void Everything_persists()
    {
        var p = new AppPreferences(db)
        {
            WindowPlacement = new WindowPlacement(100, 50, 1280, 800, Maximized: true),
            Volume = 35,
            Muted = true,
            PauseWhenMinimized = true,
        };

        var again = new AppPreferences(db);
        Assert.Equal(new WindowPlacement(100, 50, 1280, 800, true), again.WindowPlacement);
        Assert.Equal(35, again.Volume);
        Assert.True(again.Muted);
        Assert.True(again.PauseWhenMinimized);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(150, 100)]
    public void Volume_is_kept_within_0_to_100(int set, int stored)
    {
        var p = new AppPreferences(db) { Volume = set };
        Assert.Equal(stored, new AppPreferences(db).Volume);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("1,2,3")]
    [InlineData("1,2,NaN,4,0")]
    [InlineData("1,2,-10,400,0")]
    public void A_corrupt_saved_placement_is_ignored(string stored)
    {
        db.SetSetting("window.placement", stored);
        Assert.Null(new AppPreferences(db).WindowPlacement);
    }

    [Fact]
    public void A_placement_that_fits_the_screen_is_kept()
    {
        var p = new WindowPlacement(100, 50, 1280, 800, false);
        Assert.Equal(p, p.FitTo(Screen, 480, 320));
    }

    [Fact]
    public void A_window_left_on_a_disconnected_monitor_comes_back_onto_the_screen()
    {
        var fitted = new WindowPlacement(2500, 100, 1280, 800, false).FitTo(Screen, 480, 320);
        Assert.InRange(fitted.Left, 0, 1920 - 1280);
        Assert.Equal(100, fitted.Top);
        Assert.Equal(1280, fitted.Width);

        // Too low to fit: moved up just enough to be fully visible.
        Assert.Equal(1040 - 800, new WindowPlacement(100, 300, 1280, 800, false).FitTo(Screen, 480, 320).Top);
    }

    [Fact]
    public void A_window_bigger_than_the_screen_shrinks_to_fit()
    {
        var fitted = new WindowPlacement(-50, -50, 4000, 3000, false).FitTo(Screen, 480, 320);
        Assert.Equal(new WindowPlacement(0, 0, 1920, 1040, false), fitted);
    }

    [Fact]
    public void A_window_smaller_than_the_minimum_grows_to_it()
    {
        var fitted = new WindowPlacement(10, 10, 100, 100, false).FitTo(Screen, 480, 320);
        Assert.Equal(480, fitted.Width);
        Assert.Equal(320, fitted.Height);
    }
}
