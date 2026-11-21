namespace Tubiqo.Core.Tests;

/// <summary>A clock the test moves by hand.</summary>
public sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    DateTimeOffset now = start;

    public ManualClock() : this(new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero)) { }

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan by) => now += by;
    public void AdvanceSeconds(double seconds) => now += TimeSpan.FromSeconds(seconds);
}
