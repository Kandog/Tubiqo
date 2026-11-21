namespace H2o.Core.Navigation;

public enum NavigationTarget { TopLevel, Frame }

public enum NavigationAction
{
    /// <summary>Let the navigation proceed inside h2o.</summary>
    Allow,
    /// <summary>Cancel, then navigate to <see cref="NavigationDecision.Url"/> (the https version).</summary>
    UpgradeToHttps,
    /// <summary>Cancel, then offer <see cref="NavigationDecision.Url"/> to the user's default browser after confirmation.</summary>
    OpenExternally,
    /// <summary>Cancel silently.</summary>
    Block,
}

public sealed record NavigationDecision(NavigationAction Action, string Url)
{
    public static readonly NavigationDecision Blocked = new(NavigationAction.Block, "");
}

/// <summary>Decides what h2o does with every navigation, based on the Allowed Domains.</summary>
public static class NavigationGuard
{
    public static NavigationDecision Classify(string url, NavigationTarget target) => Classify(url, target, depth: 0);

    static NavigationDecision Classify(string url, NavigationTarget target, int depth)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return NavigationDecision.Blocked;

        if (uri.Scheme == "about")
        {
            // Empty frames are harmless and YouTube uses them; a blank top-level page is not a destination.
            var blankFrame = target == NavigationTarget.Frame && (uri.AbsolutePath is "blank" or "srcdoc");
            return blankFrame ? new(NavigationAction.Allow, uri.OriginalString) : NavigationDecision.Blocked;
        }

        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return NavigationDecision.Blocked;

        var allowedHost = AllowedDomains.Contains(uri.IdnHost);

        if (!allowedHost)
        {
            // A frame from elsewhere (ads, sign-in helpers) is never worth opening in the user's browser.
            return target == NavigationTarget.TopLevel
                ? new(NavigationAction.OpenExternally, uri.AbsoluteUri)
                : NavigationDecision.Blocked;
        }

        if (!uri.IsDefaultPort) return NavigationDecision.Blocked;

        if (uri.Scheme == Uri.UriSchemeHttp)
        {
            var https = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = -1 }.Uri;
            return new(NavigationAction.UpgradeToHttps, https.AbsoluteUri);
        }

        // youtube.com/redirect?q=<target> wraps outside links; judge the real destination.
        if (depth == 0 && uri.AbsolutePath == "/redirect" && QueryString.Get(uri, "q") is { Length: > 0 } inner)
        {
            return Classify(inner, target, depth + 1);
        }

        return new(NavigationAction.Allow, uri.AbsoluteUri);
    }
}
