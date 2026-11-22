namespace Tubiqo.Core.Storage;

/// <summary>
/// Owns the folder that holds Site Data (everything YouTube and Google sign-in store inside Tubiqo).
/// It is one profile folder, <c>&lt;root&gt;\site\profile</c>, kept between launches so a sign-in
/// survives a restart. Tubiqo Data lives elsewhere under <c>&lt;root&gt;</c> and is never touched.
/// </summary>
public sealed class SiteDataStore
{
    const string ProfileName = "profile";
    readonly string siteRoot;

    public SiteDataStore(string dataRoot)
    {
        siteRoot = Path.Combine(dataRoot, "site");
    }

    /// <summary>The WebView2 user data folder, created if needed.</summary>
    public string ProfileFolder()
    {
        var path = Path.Combine(siteRoot, ProfileName);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Deletes the per-launch folders older Tubiqo versions created (they wiped Site Data on every exit).
    /// Returns the ones that could not be deleted.
    /// </summary>
    public IReadOnlyList<string> WipeOldSessions() => Wipe(keepProfile: true);

    /// <summary>Deletes all Site Data, the profile included. Returns the folders that could not be deleted (for example, still in use).</summary>
    public IReadOnlyList<string> WipeAll() => Wipe(keepProfile: false);

    IReadOnlyList<string> Wipe(bool keepProfile)
    {
        var failures = new List<string>();
        if (!Directory.Exists(siteRoot)) return failures;

        foreach (var dir in Directory.EnumerateDirectories(siteRoot))
        {
            if (keepProfile && string.Equals(Path.GetFileName(dir), ProfileName, StringComparison.OrdinalIgnoreCase)) continue;
            try { Directory.Delete(dir, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { failures.Add(dir); }
        }
        return failures;
    }
}
