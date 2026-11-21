using Tubiqo.Core.Storage;

namespace Tubiqo.Core.Tests.Storage;

public sealed class SiteDataStoreTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "tubiqo-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Each_launch_gets_a_new_empty_folder_under_site()
    {
        var store = new SiteDataStore(root);

        var a = store.CreateSessionFolder();
        var b = store.CreateSessionFolder();

        Assert.NotEqual(a, b);
        Assert.True(Directory.Exists(a));
        Assert.Empty(Directory.EnumerateFileSystemEntries(a));
        Assert.Equal(Path.Combine(root, "site"), Path.GetDirectoryName(a));
    }

    [Fact]
    public void Wipe_removes_every_session_folder_including_ones_left_by_a_crash()
    {
        var store = new SiteDataStore(root);
        var leftover = store.CreateSessionFolder();
        File.WriteAllText(Path.Combine(leftover, "Cookies"), "visitor-id");
        Directory.CreateDirectory(Path.Combine(leftover, "EBWebView", "Default"));
        var current = store.CreateSessionFolder();

        var failures = store.WipeAll();

        Assert.Empty(failures);
        Assert.False(Directory.Exists(leftover));
        Assert.False(Directory.Exists(current));
    }

    [Fact]
    public void Wipe_never_touches_tubiqo_data_next_to_the_site_folder()
    {
        var store = new SiteDataStore(root);
        store.CreateSessionFolder();
        Directory.CreateDirectory(root);
        var dbFile = Path.Combine(root, "tubiqo.db");
        File.WriteAllText(dbFile, "watch history");

        store.WipeAll();

        Assert.True(File.Exists(dbFile));
    }

    [Fact]
    public void Wipe_reports_folders_it_could_not_delete_instead_of_throwing()
    {
        var store = new SiteDataStore(root);
        var session = store.CreateSessionFolder();
        var locked = Path.Combine(session, "in-use.lock");
        using (new FileStream(locked, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            var failures = store.WipeAll();
            Assert.Contains(session, failures);
        }
    }

    [Fact]
    public void Wipe_with_nothing_to_wipe_is_fine()
    {
        Assert.Empty(new SiteDataStore(root).WipeAll());
    }
}
