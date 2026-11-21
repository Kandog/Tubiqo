using H2o.Core.Playback;

namespace H2o.Core.History;

/// <summary>
/// Turns the Lean Player's state reports into Watch History writes:
/// a video becomes a Watch Entry after about 10 s of actual playback, then its
/// Resume Position is saved every 5 s while playing, and at once on pause, end or leaving.
/// </summary>
public sealed class WatchTracker(WatchHistory history, TimeProvider clock)
{
    static readonly TimeSpan Threshold = TimeSpan.FromSeconds(10);
    static readonly TimeSpan SaveEvery = TimeSpan.FromSeconds(5);
    static readonly TimeSpan MaxCountedGap = TimeSpan.FromSeconds(2); // a longer gap means sleep or a stall

    string? videoId;
    PlayerStateMessage? last;
    DateTimeOffset lastAt;
    TimeSpan played;
    bool recorded;
    DateTimeOffset lastSaved;

    public void OnState(PlayerStateMessage s)
    {
        var now = clock.GetUtcNow();
        if (s.VideoId != videoId)
        {
            Flush();
            videoId = s.VideoId;
            played = TimeSpan.Zero;
            recorded = false;
            last = null;
        }

        if (last is { State: PlayerState.Playing })
        {
            var gap = now - lastAt;
            if (gap > TimeSpan.Zero) played += gap < MaxCountedGap ? gap : MaxCountedGap;
        }
        last = s;
        lastAt = now;

        if (history.IsRecordingPaused) return;

        if (!recorded)
        {
            if (played >= Threshold) Save(s, now);
            return;
        }

        switch (s.State)
        {
            case PlayerState.Ended:
                Save(s with { TimeSeconds = 0 }, now);
                break;
            case PlayerState.Paused:
                Save(s, now);
                break;
            case PlayerState.Playing when now - lastSaved >= SaveEvery:
                Save(s, now);
                break;
        }
    }

    /// <summary>Saves the last known position of the current video (call when leaving the player or closing).</summary>
    public void Flush()
    {
        if (recorded && last != null && !history.IsRecordingPaused)
        {
            var s = last.State == PlayerState.Ended ? last with { TimeSeconds = 0 } : last;
            Save(s, clock.GetUtcNow());
        }
        videoId = null;
        last = null;
        recorded = false;
        played = TimeSpan.Zero;
    }

    void Save(PlayerStateMessage s, DateTimeOffset now)
    {
        history.Record(new WatchUpdate(s.VideoId, s.Title, s.Author, s.DurationSeconds, s.TimeSeconds));
        recorded = true;
        lastSaved = now;
    }
}
