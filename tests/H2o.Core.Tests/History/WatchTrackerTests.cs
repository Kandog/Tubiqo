using H2o.Core.History;
using H2o.Core.Playback;
using H2o.Core.Storage;

namespace H2o.Core.Tests.History;

public sealed class WatchTrackerTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "h2o-tests-" + Guid.NewGuid().ToString("N"));
    readonly ManualClock clock = new();
    readonly H2oDatabase db;
    readonly WatchHistory history;
    readonly WatchTracker tracker;

    const string A = "aaaaaaaaaaa", B = "bbbbbbbbbbb";

    public WatchTrackerTests()
    {
        Directory.CreateDirectory(dir);
        db = H2oDatabase.Open(Path.Combine(dir, "h2o.db"));
        history = new WatchHistory(db, clock);
        tracker = new WatchTracker(history, clock);
    }

    public void Dispose()
    {
        db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(dir, recursive: true);
    }

    double videoTime;

    /// <summary>Simulates the player reporting once a second for <paramref name="seconds"/>.</summary>
    void Play(string id, int seconds, string title = "Title")
    {
        for (var i = 0; i < seconds; i++)
        {
            tracker.OnState(new PlayerStateMessage(id, title, "Channel", videoTime, 600, PlayerState.Playing));
            clock.AdvanceSeconds(1);
            videoTime += 1;
        }
    }

    void Report(string id, PlayerState state) =>
        tracker.OnState(new PlayerStateMessage(id, "Title", "Channel", videoTime, 600, state));

    [Fact]
    public void Nothing_is_recorded_before_about_10_seconds_of_playback()
    {
        Play(A, 10); // 10 reports = 9 s of measured playback
        Assert.Null(history.Get(A));
    }

    [Fact]
    public void A_watch_entry_appears_after_10_seconds_of_playback()
    {
        Play(A, 12);
        var e = history.Get(A);
        Assert.NotNull(e);
        Assert.InRange(e!.ResumeSeconds, 10, 12);
    }

    [Fact]
    public void Paused_or_buffering_time_does_not_count()
    {
        Play(A, 6);
        for (var i = 0; i < 30; i++) { Report(A, PlayerState.Paused); clock.AdvanceSeconds(1); }
        for (var i = 0; i < 30; i++) { Report(A, PlayerState.Buffering); clock.AdvanceSeconds(1); }
        Assert.Null(history.Get(A));
    }

    [Fact]
    public void A_long_gap_between_reports_counts_only_a_little()
    {
        Play(A, 3);
        clock.Advance(TimeSpan.FromMinutes(10)); // PC slept while "playing"
        Play(A, 2);
        Assert.Null(history.Get(A));
    }

    [Fact]
    public void Resume_position_is_saved_about_every_5_seconds_while_playing()
    {
        Play(A, 12);
        var first = history.Get(A)!.ResumeSeconds;
        Play(A, 3);
        Assert.Equal(first, history.Get(A)!.ResumeSeconds);
        Play(A, 3);
        Assert.True(history.Get(A)!.ResumeSeconds > first);
    }

    [Fact]
    public void Pausing_saves_the_position_immediately()
    {
        Play(A, 12);
        videoTime = 200;
        Report(A, PlayerState.Paused);
        Assert.Equal(200, history.Get(A)!.ResumeSeconds);
    }

    [Fact]
    public void Finishing_a_video_resets_its_resume_position()
    {
        Play(A, 12);
        videoTime = 600;
        Report(A, PlayerState.Ended);
        Assert.Equal(0, history.Get(A)!.ResumeSeconds);
    }

    [Fact]
    public void Flush_saves_the_last_known_position_when_leaving_the_player()
    {
        Play(A, 12);
        Play(A, 2);
        var expected = videoTime - 1;
        tracker.Flush();
        Assert.Equal(expected, history.Get(A)!.ResumeSeconds);
    }

    [Fact]
    public void Flush_before_the_threshold_records_nothing()
    {
        Play(A, 5);
        tracker.Flush();
        Assert.Null(history.Get(A));
    }

    [Fact]
    public void Switching_video_starts_counting_again()
    {
        Play(A, 12);
        videoTime = 0;
        Play(B, 5);
        Assert.NotNull(history.Get(A));
        Assert.Null(history.Get(B));
    }

    [Fact]
    public void Nothing_is_recorded_while_recording_is_paused()
    {
        history.IsRecordingPaused = true;
        Play(A, 30);
        Report(A, PlayerState.Paused);
        tracker.Flush();
        Assert.Null(history.Get(A));
    }

    [Fact]
    public void Re_watching_an_existing_entry_updates_it_only_after_the_threshold()
    {
        Play(A, 12);
        tracker.Flush();
        var firstSeen = history.Get(A)!.LastWatchedUtc;
        clock.Advance(TimeSpan.FromDays(2));

        videoTime = 0;
        Play(A, 5);
        Assert.Equal(firstSeen, history.Get(A)!.LastWatchedUtc);
        Play(A, 7);
        Assert.True(history.Get(A)!.LastWatchedUtc > firstSeen);
    }
}
