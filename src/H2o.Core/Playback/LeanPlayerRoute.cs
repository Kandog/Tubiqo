using System.Text.RegularExpressions;
using H2o.Core.Navigation;

namespace H2o.Core.Playback;

/// <summary>A video to play, and where to start (seconds).</summary>
public sealed record VideoRequest(string VideoId, int StartSeconds);

/// <summary>
/// Decides which URLs are videos (played in the Lean Player) and builds the Lean Player
/// and Watch Page Fallback URLs. Everything else on the Allowed Domains is a Browse Page.
/// </summary>
public static partial class LeanPlayerRoute
{
    public const string PageUrl = "https://" + AllowedDomains.PlayerHost + "/player.html";

    const string FallbackMarker = "h2o";
    const string FallbackValue = "fallback";

    [GeneratedRegex("^[A-Za-z0-9_-]{11}$")]
    private static partial Regex VideoIdPattern();

    [GeneratedRegex("^(?:(\\d+)h)?(?:(\\d+)m)?(?:(\\d+)s)?$")]
    private static partial Regex DurationPattern();

    public static bool IsValidVideoId(string? id) => id != null && VideoIdPattern().IsMatch(id);

    /// <summary>True for YouTube video links (watch, youtu.be, Shorts, live, embed), except Watch Page Fallback pages.</summary>
    public static bool TryGetVideo(string url, out VideoRequest video)
    {
        video = null!;
        if (!TryParseVideoLink(url, out var uri, out var id)) return false;
        if (QueryString.Get(uri, FallbackMarker) == FallbackValue) return false;
        video = new VideoRequest(id, StartFrom(uri));
        return true;
    }

    /// <summary>True for a watch page h2o opened itself because the video can't be embedded.</summary>
    public static bool IsFallback(string url, out string videoId)
    {
        videoId = "";
        if (!TryParseVideoLink(url, out var uri, out var id)) return false;
        if (QueryString.Get(uri, FallbackMarker) != FallbackValue) return false;
        videoId = id;
        return true;
    }

    public static string PlayerUrl(VideoRequest video) =>
        $"{PageUrl}?v={video.VideoId}" + (video.StartSeconds > 0 ? $"&t={video.StartSeconds}" : "");

    public static string FallbackUrl(VideoRequest video) =>
        $"https://www.youtube.com/watch?v={video.VideoId}" +
        (video.StartSeconds > 0 ? $"&t={video.StartSeconds}s" : "") +
        $"&{FallbackMarker}={FallbackValue}";

    /// <summary>True only for h2o's own player page with a valid video ID.</summary>
    public static bool TryParsePlayerUrl(string url, out VideoRequest video)
    {
        video = null!;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort) return false;
        if (!string.Equals(uri.IdnHost, AllowedDomains.PlayerHost, StringComparison.OrdinalIgnoreCase)) return false;
        if (uri.AbsolutePath != "/player.html") return false;
        var id = QueryString.Get(uri, "v");
        if (!IsValidVideoId(id)) return false;
        video = new VideoRequest(id!, ParseSeconds(QueryString.Get(uri, "t")));
        return true;
    }

    /// <summary>Embed errors meaning "this video can't be embedded" (owner refused, age gate, missing referrer).</summary>
    public static bool NeedsWatchPageFallback(int embedErrorCode) => embedErrorCode is 101 or 150 or 153;

    static bool TryParseVideoLink(string url, out Uri uri, out string id)
    {
        id = "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out uri!)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return false;

        var host = uri.IdnHost.ToLowerInvariant();
        if (!AllowedDomains.Contains(host) || host == AllowedDomains.PlayerHost) return false;

        var segments = uri.AbsolutePath.Trim('/').Split('/');
        string? candidate;
        if (host == "youtu.be" || host.EndsWith(".youtu.be"))
            candidate = segments.Length == 1 ? segments[0] : null;
        else if (segments is ["watch"])
            candidate = QueryString.Get(uri, "v");
        else if (segments is ["shorts" or "live" or "embed", var s])
            candidate = s;
        else
            candidate = null;

        if (!IsValidVideoId(candidate)) return false;
        id = candidate!;
        return true;
    }

    static int StartFrom(Uri uri) => ParseSeconds(QueryString.Get(uri, "t") ?? QueryString.Get(uri, "start"));

    static int ParseSeconds(string? value)
    {
        if (string.IsNullOrEmpty(value)) return 0;
        if (int.TryParse(value, System.Globalization.NumberStyles.None, null, out var plain)) return plain;

        var m = DurationPattern().Match(value);
        if (!m.Success) return 0;
        try
        {
            checked
            {
                long Part(int g) => m.Groups[g].Success ? long.Parse(m.Groups[g].Value) : 0;
                var total = Part(1) * 3600 + Part(2) * 60 + Part(3);
                return total <= int.MaxValue ? (int)total : 0;
            }
        }
        catch (OverflowException) { return 0; }
    }
}
