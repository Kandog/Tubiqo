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
    public void Site_data_lives_in_one_profile_folder_that_is_kept_between_launches()
    {
        var first = new SiteDataStore(root).ProfileFolder();
        File.WriteAllText(Path.Combine(first, "Cookies"), "signed in");

        var second = new SiteDataStore(root);
        second.WipeOldSessions();

        Assert.Equal(first, second.ProfileFolder());
        Assert.Equal(Path.Combine(root, "site"), Path.GetDirectoryName(first));
        Assert.True(File.Exists(Path.Combine(first, "Cookies")));
    }

    [Fact]
    public void Old_per_launch_session_folders_are_wiped_but_the_profile_is_kept()
    {
        var store = new SiteDataStore(root);
        var profile = store.ProfileFolder();
        var leftover = Path.Combine(root, "site", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(leftover, "EBWebView", "Default"));

        Assert.Empty(store.WipeOldSessions());

        Assert.False(Directory.Exists(leftover));
        Assert.True(Directory.Exists(profile));
    }

    [Fact]
    public void Wipe_all_removes_the_profile_too()
    {
        var store = new SiteDataStore(root);
        var profile = store.ProfileFolder();
        File.WriteAllText(Path.Combine(profile, "Cookies"), "signed in");

        Assert.Empty(store.WipeAll());

        Assert.False(Directory.Exists(profile));
    }

    [Fact]
    public void Wipe_never_touches_tubiqo_data_next_to_the_site_folder()
    {
        var store = new SiteDataStore(root);
        store.ProfileFolder();
        var dbFile = Path.Combine(root, "tubiqo.db");
        File.WriteAllText(dbFile, "watch history");

        store.WipeAll();

        Assert.True(File.Exists(dbFile));
    }

    [Fact]
    public void Wipe_reports_folders_it_could_not_delete_instead_of_throwing()
    {
        var store = new SiteDataStore(root);
        var session = store.ProfileFolder();
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
