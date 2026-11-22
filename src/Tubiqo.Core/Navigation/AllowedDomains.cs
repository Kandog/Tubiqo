using System.Text.RegularExpressions;

namespace Tubiqo.Core.Navigation;

/// <summary>
/// The fixed set of domains Tubiqo will navigate to (top-level pages and frames).
/// Pages on these domains may still load subresources from elsewhere (images, video
/// segments); that is governed by Trim Rules, not by this list.
/// </summary>
public static partial class AllowedDomains
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

    // Google account sign-in, so it happens inside Tubiqo instead of the default browser.
    // Exact hosts only: the rest of google.com still opens outside.
    static readonly string[] SignInHosts =
    {
        "accounts.google.com",
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
        return Array.IndexOf(SignInHosts, host) >= 0;
    }

    // Country domains such as accounts.google.ca or accounts.google.co.uk, where Google also sets the session cookie.
    [GeneratedRegex(@"^accounts\.google\.(?:[a-z]{2}|co\.[a-z]{2}|com\.[a-z]{2})$")]
    private static partial Regex CountrySignInHost();

    /// <summary>
    /// Google sign-in pages on a country domain (<c>accounts.google.ca/accounts/SetSID</c> and similar),
    /// limited to their <c>/accounts/</c> path. They are part of the sign-in redirect chain.
    /// </summary>
    public static bool IsCountrySignInPage(Uri uri) =>
        CountrySignInHost().IsMatch(uri.IdnHost.TrimEnd('.').ToLowerInvariant()) &&
        uri.AbsolutePath.StartsWith("/accounts/", StringComparison.Ordinal);

    /// <summary>
    /// Frames from outside the Allowed Domains that sign-in needs: Google's reCAPTCHA challenge.
    /// Never a top-level page.
    /// </summary>
    public static bool IsSignInFrame(Uri uri) =>
        string.Equals(uri.IdnHost, "www.google.com", StringComparison.OrdinalIgnoreCase) &&
        uri.AbsolutePath.StartsWith("/recaptcha/", StringComparison.Ordinal);
}
