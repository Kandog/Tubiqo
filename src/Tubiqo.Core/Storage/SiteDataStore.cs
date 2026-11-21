namespace Tubiqo.Core.Storage;

/// <summary>
/// Owns the folders that hold Site Data (everything YouTube stores inside Tubiqo).
/// Each launch gets a fresh folder under <c>&lt;root&gt;\site\</c>; all of them are wiped
/// on exit and again on the next startup, in case Tubiqo crashed. Tubiqo Data lives
/// elsewhere under <c>&lt;root&gt;</c> and is never touched.
/// </summary>
public sealed class SiteDataStore
{
    readonly string siteRoot;

    public SiteDataStore(string dataRoot)
    {
        siteRoot = Path.Combine(dataRoot, "site");
    }

    public string CreateSessionFolder()
    {
        var path = Path.Combine(siteRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Deletes every session folder. Returns the ones that could not be deleted (for example, still in use).</summary>
    public IReadOnlyList<string> WipeAll()
    {
        var failures = new List<string>();
        if (!Directory.Exists(siteRoot)) return failures;

        foreach (var dir in Directory.EnumerateDirectories(siteRoot))
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { failures.Add(dir); }
        }
        return failures;
    }
}
