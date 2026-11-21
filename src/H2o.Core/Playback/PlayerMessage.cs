using System.Text.Json;

namespace H2o.Core.Playback;

/// <summary>YouTube player states, as reported by the embed player.</summary>
public enum PlayerState { Unstarted = -1, Ended = 0, Playing = 1, Paused = 2, Buffering = 3, Cued = 5 }

/// <summary>A message the Lean Player page sends to h2o. Anything malformed parses to null and is ignored.</summary>
public abstract record PlayerMessage
{
    const int MaxTextLength = 300;

    public static PlayerMessage? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!TryString(root, "type", out var type)) return null;
            if (!TryString(root, "videoId", out var videoId) || !LeanPlayerRoute.IsValidVideoId(videoId)) return null;

            return type switch
            {
                "state" => ParseState(root, videoId),
                "error" => root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var code)
                    ? new PlayerErrorMessage(videoId, code)
                    : null,
                _ => null,
            };
        }
        catch (JsonException) { return null; }
    }

    static PlayerStateMessage? ParseState(JsonElement root, string videoId)
    {
        if (!OptionalString(root, "title", out var title) || !OptionalString(root, "author", out var author)) return null;
        if (!OptionalNumber(root, "time", out var time) || !OptionalNumber(root, "duration", out var duration)) return null;
        if (!OptionalNumber(root, "state", out var stateNumber)) return null;

        var s = (int)stateNumber;
        var state = s == stateNumber && Enum.IsDefined(typeof(PlayerState), s) ? (PlayerState)s : PlayerState.Unstarted;

        return new PlayerStateMessage(videoId, Truncate(title), Truncate(author), Math.Max(0, time), Math.Max(0, duration), state);
    }

    static string Truncate(string s) => s.Length > MaxTextLength ? s[..MaxTextLength] : s;

    static bool TryString(JsonElement o, string name, out string value)
    {
        value = "";
        if (!o.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.String) return false;
        value = p.GetString() ?? "";
        return true;
    }

    static bool OptionalString(JsonElement o, string name, out string value)
    {
        value = "";
        if (!o.TryGetProperty(name, out var p)) return true;
        if (p.ValueKind != JsonValueKind.String) return false;
        value = p.GetString() ?? "";
        return true;
    }

    static bool OptionalNumber(JsonElement o, string name, out double value)
    {
        value = name == "state" ? -1 : 0;
        if (!o.TryGetProperty(name, out var p)) return true;
        if (p.ValueKind != JsonValueKind.Number || !p.TryGetDouble(out value) || !double.IsFinite(value)) return false;
        return true;
    }
}

public sealed record PlayerStateMessage(
    string VideoId, string Title, string Author, double TimeSeconds, double DurationSeconds, PlayerState State) : PlayerMessage;

public sealed record PlayerErrorMessage(string VideoId, int Code) : PlayerMessage;
