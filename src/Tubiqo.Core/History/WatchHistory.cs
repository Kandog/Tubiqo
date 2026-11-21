using Tubiqo.Core.Playback;
using Tubiqo.Core.Storage;
using Microsoft.Data.Sqlite;

namespace Tubiqo.Core.History;

/// <summary>The single record of one video in Watch History.</summary>
public sealed record WatchEntry(
    string VideoId, string Title, string Channel, double DurationSeconds, double ResumeSeconds, DateTimeOffset LastWatchedUtc)
{
    /// <summary>Where reopening this entry starts: the Resume Position, or the beginning if the video was nearly finished.</summary>
    public int StartFromSeconds =>
        DurationSeconds > 0 && ResumeSeconds >= DurationSeconds - 10 ? 0 : (int)Math.Max(0, ResumeSeconds);
}

/// <summary>What the player knows about a video being watched right now.</summary>
public sealed record WatchUpdate(string VideoId, string Title, string Channel, double DurationSeconds, double ResumeSeconds);

/// <summary>
/// Watch History: kept only on this PC, one Watch Entry per video.
/// Also holds the two Watch History settings (pause recording, retention).
/// </summary>
public sealed class WatchHistory(TubiqoDatabase db, TimeProvider clock)
{
    public static readonly IReadOnlyList<int> RetentionChoices = [0, 1, 3, 6, 12]; // 0 = keep forever

    const string PausedKey = "history.paused";
    const string RetentionKey = "history.retentionMonths";
    const int MaxResults = 500;

    public bool IsRecordingPaused
    {
        get => db.GetSetting(PausedKey) == "1";
        set => db.SetSetting(PausedKey, value ? "1" : "0");
    }

    /// <summary>Months to keep entries for; 0 keeps them forever.</summary>
    public int RetentionMonths
    {
        get => int.TryParse(db.GetSetting(RetentionKey), out var m) && RetentionChoices.Contains(m) ? m : 0;
        set
        {
            if (!RetentionChoices.Contains(value)) throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported retention.");
            db.SetSetting(RetentionKey, value.ToString());
        }
    }

    /// <summary>Creates or updates the video's Watch Entry, marking it watched now.</summary>
    public void Record(WatchUpdate update)
    {
        if (!LeanPlayerRoute.IsValidVideoId(update.VideoId)) throw new ArgumentException("Invalid video ID.", nameof(update));
        db.Execute("""
            INSERT INTO watch_entry (video_id, title, channel, duration_s, resume_s, last_watched_ms)
            VALUES ($id, $title, $channel, $duration, $resume, $now)
            ON CONFLICT(video_id) DO UPDATE SET
                title    = CASE WHEN excluded.title   <> '' THEN excluded.title   ELSE title   END,
                channel  = CASE WHEN excluded.channel <> '' THEN excluded.channel ELSE channel END,
                duration_s = CASE WHEN excluded.duration_s > 0 THEN excluded.duration_s ELSE duration_s END,
                resume_s = excluded.resume_s,
                last_watched_ms = excluded.last_watched_ms
            """,
            ("$id", update.VideoId), ("$title", update.Title), ("$channel", update.Channel),
            ("$duration", Math.Max(0, update.DurationSeconds)), ("$resume", Math.Max(0, update.ResumeSeconds)),
            ("$now", clock.GetUtcNow().ToUnixTimeMilliseconds()));
    }

    public WatchEntry? Get(string videoId)
    {
        using var cmd = db.Command($"{Select} WHERE video_id = $id", ("$id", videoId));
        return Read(cmd).FirstOrDefault();
    }

    /// <summary>Entries whose title or channel contains <paramref name="text"/> (all when empty), most recent first.</summary>
    public IReadOnlyList<WatchEntry> Search(string text)
    {
        var pattern = "%" + EscapeLike(text.Trim()) + "%";
        using var cmd = db.Command(
            $"{Select} WHERE title LIKE $p ESCAPE '\\' OR channel LIKE $p ESCAPE '\\' ORDER BY last_watched_ms DESC LIMIT {MaxResults}",
            ("$p", pattern));
        return Read(cmd).ToList();
    }

    public void Delete(string videoId) => db.Execute("DELETE FROM watch_entry WHERE video_id = $id", ("$id", videoId));

    /// <summary>Deletes entries last watched between the two instants, inclusive. Returns how many.</summary>
    public int DeleteRange(DateTimeOffset fromUtc, DateTimeOffset toUtc) =>
        db.Execute("DELETE FROM watch_entry WHERE last_watched_ms BETWEEN $from AND $to",
            ("$from", fromUtc.ToUnixTimeMilliseconds()), ("$to", toUtc.ToUnixTimeMilliseconds()));

    public void Clear() => db.Execute("DELETE FROM watch_entry");

    /// <summary>Removes entries older than the retention setting. Returns how many.</summary>
    public int ApplyRetention()
    {
        var months = RetentionMonths;
        if (months == 0) return 0;
        var cutoff = clock.GetUtcNow().AddMonths(-months).ToUnixTimeMilliseconds();
        return db.Execute("DELETE FROM watch_entry WHERE last_watched_ms < $cutoff", ("$cutoff", cutoff));
    }

    const string Select = "SELECT video_id, title, channel, duration_s, resume_s, last_watched_ms FROM watch_entry";

    static IEnumerable<WatchEntry> Read(SqliteCommand cmd)
    {
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            yield return new WatchEntry(r.GetString(0), r.GetString(1), r.GetString(2), r.GetDouble(3), r.GetDouble(4),
                DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(5)));
        }
    }

    static string EscapeLike(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
