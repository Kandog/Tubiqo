using H2o.Core.History;
using H2o.Core.Storage;

namespace H2o.Core.Tests.History;

public sealed class WatchHistoryTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "h2o-tests-" + Guid.NewGuid().ToString("N"));
    readonly ManualClock clock = new();
    readonly H2oDatabase db;
    readonly WatchHistory history;

    public WatchHistoryTests()
    {
        Directory.CreateDirectory(dir);
        db = H2oDatabase.Open(Path.Combine(dir, "h2o.db"));
        history = new WatchHistory(db, clock);
    }

    public void Dispose()
    {
        db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(dir, recursive: true);
    }

    static WatchUpdate Update(string id, string title = "Title", string channel = "Channel", double resume = 0, double duration = 600) =>
        new(id, title, channel, duration, resume);

    const string A = "aaaaaaaaaaa", B = "bbbbbbbbbbb", C = "ccccccccccc";

    [Fact]
    public void Recording_a_video_creates_one_entry()
    {
        history.Record(Update(A, "Big Buck Bunny", "Blender", resume: 42, duration: 634));

        var e = Assert.Single(history.Search(""));
        Assert.Equal(new WatchEntry(A, "Big Buck Bunny", "Blender", 634, 42, clock.GetUtcNow()), e);
    }

    [Fact]
    public void Re_watching_updates_the_same_entry_instead_of_adding_one()
    {
        history.Record(Update(A, resume: 10));
        clock.Advance(TimeSpan.FromDays(1));
        history.Record(Update(A, resume: 99));

        var e = Assert.Single(history.Search(""));
        Assert.Equal(99, e.ResumeSeconds);
        Assert.Equal(clock.GetUtcNow(), e.LastWatchedUtc);
    }

    [Fact]
    public void An_empty_title_or_channel_does_not_overwrite_a_known_one()
    {
        history.Record(Update(A, "Known title", "Known channel"));
        history.Record(Update(A, "", ""));

        var e = history.Get(A)!;
        Assert.Equal("Known title", e.Title);
        Assert.Equal("Known channel", e.Channel);
    }

    [Fact]
    public void Entries_are_listed_most_recent_first()
    {
        history.Record(Update(A)); clock.AdvanceSeconds(60);
        history.Record(Update(B)); clock.AdvanceSeconds(60);
        history.Record(Update(C));

        Assert.Equal([C, B, A], history.Search("").Select(e => e.VideoId));
    }

    [Fact]
    public void Search_matches_title_or_channel_ignoring_case()
    {
        history.Record(Update(A, "Big Buck Bunny", "Blender")); clock.AdvanceSeconds(1);
        history.Record(Update(B, "Cooking pasta", "Chef")); clock.AdvanceSeconds(1);
        history.Record(Update(C, "Sintel", "BLENDER Studio"));

        Assert.Equal([A], history.Search("bunny").Select(e => e.VideoId));
        Assert.Equal([C, A], history.Search("blender").Select(e => e.VideoId));
        Assert.Empty(history.Search("nothing"));
    }

    [Fact]
    public void Search_treats_wildcard_characters_literally()
    {
        history.Record(Update(A, "100% fun"));
        history.Record(Update(B, "1000 fun"));
        history.Record(Update(C, "snake_case"));

        Assert.Equal([A], history.Search("100%").Select(e => e.VideoId));
        Assert.Equal([C], history.Search("_").Select(e => e.VideoId));
    }

    [Fact]
    public void Delete_removes_one_entry()
    {
        history.Record(Update(A));
        history.Record(Update(B));

        history.Delete(A);

        Assert.Null(history.Get(A));
        Assert.NotNull(history.Get(B));
    }

    [Fact]
    public void Delete_range_removes_entries_last_watched_inside_it_inclusive()
    {
        var day1 = clock.GetUtcNow();
        history.Record(Update(A));
        clock.Advance(TimeSpan.FromDays(1));
        history.Record(Update(B));
        var day2 = clock.GetUtcNow();
        clock.Advance(TimeSpan.FromDays(1));
        history.Record(Update(C));

        var removed = history.DeleteRange(day1, day2);

        Assert.Equal(2, removed);
        Assert.Equal([C], history.Search("").Select(e => e.VideoId));
    }

    [Fact]
    public void Clear_removes_everything()
    {
        history.Record(Update(A));
        history.Record(Update(B));
        history.Clear();
        Assert.Empty(history.Search(""));
    }

    [Fact]
    public void Recording_is_not_paused_by_default_and_the_setting_persists()
    {
        Assert.False(history.IsRecordingPaused);
        history.IsRecordingPaused = true;

        using var reopened = H2oDatabase.Open(Path.Combine(dir, "h2o.db"));
        Assert.True(new WatchHistory(reopened, clock).IsRecordingPaused);
    }

    [Fact]
    public void Keep_forever_is_the_default_retention()
    {
        Assert.Equal(0, history.RetentionMonths);
        history.Record(Update(A));
        clock.Advance(TimeSpan.FromDays(3650));
        Assert.Equal(0, history.ApplyRetention());
        Assert.NotNull(history.Get(A));
    }

    [Fact]
    public void Retention_removes_entries_older_than_the_chosen_months()
    {
        history.RetentionMonths = 3;
        history.Record(Update(A));                 // Nov 21: about 4 months old at the end
        clock.Advance(TimeSpan.FromDays(40));
        history.Record(Update(B));                 // Dec 31: under 3 months old at the end, kept
        clock.Advance(TimeSpan.FromDays(82));      // now Mar 23; cutoff Dec 23

        Assert.Equal(1, history.ApplyRetention());
        Assert.Null(history.Get(A));
        Assert.NotNull(history.Get(B));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(12)]
    public void Supported_retention_choices(int months)
    {
        history.RetentionMonths = months;
        Assert.Equal(months, history.RetentionMonths);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(24)]
    public void Other_retention_choices_are_rejected(int months)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => history.RetentionMonths = months);
    }

    [Fact]
    public void Invalid_video_ids_are_refused()
    {
        Assert.Throws<ArgumentException>(() => history.Record(Update("not-an-id")));
    }

    [Fact]
    public void Resume_position_for_an_entry_restarts_near_the_end()
    {
        history.Record(Update(A, resume: 300, duration: 600));
        history.Record(Update(B, resume: 595, duration: 600));
        history.Record(Update(C, resume: 5, duration: 0)); // duration unknown

        Assert.Equal(300, history.Get(A)!.StartFromSeconds);
        Assert.Equal(0, history.Get(B)!.StartFromSeconds);
        Assert.Equal(5, history.Get(C)!.StartFromSeconds);
    }
}
