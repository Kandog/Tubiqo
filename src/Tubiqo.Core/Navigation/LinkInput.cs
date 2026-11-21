using Tubiqo.Core.Playback;

namespace Tubiqo.Core.Navigation;

/// <summary>Where a pasted link goes: a Lean Player or Browse Page URL, or why it can't be opened.</summary>
public sealed record LinkResult(bool Ok, string Url, string Error)
{
    public static LinkResult Go(string url) => new(true, url, "");
    public static LinkResult Refuse(string error) => new(false, "", error);
}

/// <summary>Reads what the user types or pastes into the "Open a YouTube link" box (Ctrl+L).</summary>
public static class LinkInput
{
    const int MaxLength = 2000;
    const string OnlyYouTube = "Only YouTube links can be opened in Tubiqo.";

    public static LinkResult Resolve(string text)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return LinkResult.Refuse("Paste a YouTube link or video ID.");
        if (text.Length > MaxLength) return LinkResult.Refuse("That's too long to be a YouTube link.");

        if (LeanPlayerRoute.IsValidVideoId(text))
            return LinkResult.Go(LeanPlayerRoute.PlayerUrl(new VideoRequest(text, 0)));

        var candidate = text.Contains("://", StringComparison.Ordinal) ? text : "https://" + text;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return LinkResult.Refuse(OnlyYouTube);
        if (string.Equals(uri.IdnHost, AllowedDomains.PlayerHost, StringComparison.OrdinalIgnoreCase))
            return LinkResult.Refuse(OnlyYouTube);

        var decision = NavigationGuard.Classify(uri.AbsoluteUri, NavigationTarget.TopLevel);
        if (decision.Action is not (NavigationAction.Allow or NavigationAction.UpgradeToHttps)) return LinkResult.Refuse(OnlyYouTube);

        return LeanPlayerRoute.TryGetVideo(decision.Url, out var video)
            ? LinkResult.Go(LeanPlayerRoute.PlayerUrl(video))
            : LinkResult.Go(decision.Url);
    }

    /// <summary>True when clipboard text is worth pre-filling: an actual YouTube link, not any 11-letter word.</summary>
    public static bool LooksLikeYouTubeLink(string? text) =>
        text is { Length: > 0 and <= MaxLength } &&
        text.Contains("youtu", StringComparison.OrdinalIgnoreCase) &&
        Resolve(text).Ok;
}
