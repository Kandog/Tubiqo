namespace H2o.Core.Storage;

/// <summary>
/// Owns the folders that hold Site Data (everything YouTube stores inside h2o).
/// Each launch gets a fresh folder under <c>&lt;root&gt;\site\</c>; all of them are wiped
/// on exit and again on the next startup, in case h2o crashed. h2o Data lives
/// elsewhere under <c>&lt;root&gt;</c> and is never touched.
/// </summary>
public sealed class SiteDataStore
{
    readonly string siteRoot;

    public SiteDataStore(string h2oRoot)
    {
        siteRoot = Path.Combine(h2oRoot, "site");
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
