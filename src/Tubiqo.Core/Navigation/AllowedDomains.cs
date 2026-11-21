namespace Tubiqo.Core.Navigation;

/// <summary>
/// The fixed set of domains Tubiqo will navigate to (top-level pages and frames).
/// Pages on these domains may still load subresources from elsewhere (images, video
/// segments); that is governed by Trim Rules, not by this list.
/// </summary>
public static class AllowedDomains
{
    /// <summary>Tubiqo's own local origin that hosts the Lean Player page.</summary>
    public const string PlayerHost = "player.tubiqo.test";

    static readonly string[] Roots =
    {
        "youtube.com",
        "youtu.be",
        "youtube-nocookie.com",
        PlayerHost,
    };

    /// <summary>True when <paramref name="host"/> is a root or a subdomain of one (whole labels only).</summary>
    public static bool Contains(string host)
    {
        if (string.IsNullOrEmpty(host)) return false;
        host = host.TrimEnd('.').ToLowerInvariant();
        foreach (var root in Roots)
        {
            if (host == root || host.EndsWith("." + root, StringComparison.Ordinal)) return true;
        }
        return false;
    }
}
