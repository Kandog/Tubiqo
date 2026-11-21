using System.Security.Cryptography;
using System.Text;
using Tubiqo.Core.Rules;
using Tubiqo.Core.Updates;

namespace Tubiqo.Core.Tests.Updates;

public sealed class RulePackStoreTests : IDisposable
{
    static readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    static readonly SignatureVerifier Verifier = new(Convert.ToBase64String(Key.ExportSubjectPublicKeyInfo()));

    readonly string root = Path.Combine(Path.GetTempPath(), "tubiqo-tests-" + Guid.NewGuid().ToString("N"));
    readonly RulePackStore store;

    public RulePackStoreTests() => store = new RulePackStore(root, Verifier);

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    static byte[] Pack(string version) => Encoding.UTF8.GetBytes(
        $$"""{ "format": 1, "version": "{{version}}", "rules": [ { "id": "ads", "title": "Ads {{version}}", "default": true, "hide": ["#ad"] } ] }""");

    /// <summary>A correctly signed release for <paramref name="pack"/>, as the signing tool would make it.</summary>
    static (byte[] Manifest, string Signature) Release(byte[] pack, string version, ECDsa? key = null, string? sha = null, long? size = null)
    {
        var hash = sha ?? Convert.ToHexStringLower(SHA256.HashData(pack));
        var manifest = Encoding.UTF8.GetBytes(
            $$"""{ "format": 1, "rulePack": { "version": "{{version}}", "file": "rulepack.json", "size": {{size ?? pack.Length}}, "sha256": "{{hash}}" } }""");
        return (manifest, ReleaseSigner.Sign(manifest, key ?? Key));
    }

    InstalledRulePack Install(string version)
    {
        var pack = Pack(version);
        var (m, s) = Release(pack, version);
        return store.Install(m, s, pack);
    }

    [Fact]
    public void Nothing_is_installed_at_first()
    {
        Assert.Null(store.LoadCurrent());
        Assert.False(store.HasPrevious);
    }

    [Fact]
    public void A_signed_pack_is_installed_and_loads_after_restart()
    {
        Install("2026.11.22.1");

        var loaded = new RulePackStore(root, Verifier).LoadCurrent();
        Assert.NotNull(loaded);
        Assert.Equal(new Version(2026, 11, 22, 1), loaded!.Version);
        Assert.Equal("Ads 2026.11.22.1", loaded.Pack.Rules[0].Title);
    }

    [Fact]
    public void Installing_again_keeps_the_previous_pack_for_rollback()
    {
        Install("2026.11.22.1");
        Install("2026.11.23.1");

        Assert.Equal(new Version(2026, 11, 23, 1), store.LoadCurrent()!.Version);
        Assert.True(store.HasPrevious);
        Assert.True(store.Rollback());
        Assert.Equal(new Version(2026, 11, 22, 1), store.LoadCurrent()!.Version);
    }

    [Fact]
    public void Rollback_without_a_previous_pack_does_nothing()
    {
        Install("2026.11.22.1");
        Assert.False(store.Rollback());
        Assert.Equal(new Version(2026, 11, 22, 1), store.LoadCurrent()!.Version);
    }

    [Fact]
    public void A_pack_signed_with_another_key_is_refused_and_nothing_changes()
    {
        Install("2026.11.22.1");
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pack = Pack("2026.11.23.1");
        var (m, s) = Release(pack, "2026.11.23.1", key: other);

        Assert.Throws<UpdateException>(() => store.Install(m, s, pack));
        Assert.Equal(new Version(2026, 11, 22, 1), store.LoadCurrent()!.Version);
        Assert.False(store.HasPrevious);
    }

    [Fact]
    public void A_pack_that_does_not_match_its_signed_hash_is_refused()
    {
        var pack = Pack("2026.11.22.1");
        var (m, s) = Release(pack, "2026.11.22.1");
        var tampered = Pack("2026.11.22.2"); // same size, different bytes
        Assert.Throws<UpdateException>(() => store.Install(m, s, tampered));
        Assert.Null(store.LoadCurrent());
    }

    [Fact]
    public void A_pack_whose_version_differs_from_the_manifest_is_refused()
    {
        var pack = Pack("2026.11.22.1");
        var (m, s) = Release(pack, "2026.12.1.0");
        Assert.Throws<UpdateException>(() => store.Install(m, s, pack));
    }

    [Fact]
    public void A_signed_pack_that_breaks_the_rule_checks_is_still_refused()
    {
        var pack = Encoding.UTF8.GetBytes("""{ "format": 1, "version": "2026.11.22.1", "rules": [ { "id": "x", "title": "X", "default": true, "block": ["*"] } ] }""");
        var (m, s) = Release(pack, "2026.11.22.1");
        Assert.Throws<UpdateException>(() => store.Install(m, s, pack));
    }

    [Fact]
    public void A_pack_edited_on_disk_after_install_is_not_loaded()
    {
        Install("2026.11.22.1");
        var file = Directory.GetFiles(root, "rulepack.json", SearchOption.AllDirectories).Single();
        File.WriteAllText(file, File.ReadAllText(file).Replace("#ad", "#xx"));

        Assert.Null(new RulePackStore(root, Verifier).LoadCurrent());
    }

    [Fact]
    public void The_newer_of_the_built_in_and_installed_packs_is_used()
    {
        var builtIn = RulePack.Parse(Encoding.UTF8.GetString(Pack("2026.11.21.0")));
        Assert.Equal("2026.11.21.0", RulePackStore.Choose(builtIn, null).Version);

        var installed = Install("2026.11.22.1");
        Assert.Equal("2026.11.22.1", RulePackStore.Choose(builtIn, installed).Version);

        var newerBuiltIn = RulePack.Parse(Encoding.UTF8.GetString(Pack("2026.12.1.0")));
        Assert.Equal("2026.12.1.0", RulePackStore.Choose(newerBuiltIn, installed).Version);
    }
}
