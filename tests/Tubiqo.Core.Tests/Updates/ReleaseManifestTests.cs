using Tubiqo.Core.Updates;

namespace Tubiqo.Core.Tests.Updates;

public class ReleaseManifestTests
{
    const string Sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    static string Json(string app = $$"""{ "version": "0.6.0", "file": "Tubiqo-Setup-0.6.0.exe", "size": 1000, "sha256": "{{Sha}}" }""",
                       string rulePack = $$"""{ "version": "2026.11.22.1", "file": "rulepack.json", "size": 500, "sha256": "{{Sha}}" }""") =>
        $$"""{ "format": 1, "app": {{app}}, "rulePack": {{rulePack}} }""";

    [Fact]
    public void A_valid_manifest_is_parsed()
    {
        var m = ReleaseManifest.Parse(Json());
        Assert.Equal(new Version(0, 6, 0), m.App!.Version);
        Assert.Equal("Tubiqo-Setup-0.6.0.exe", m.App.File);
        Assert.Equal(1000, m.App.Size);
        Assert.Equal(Sha, m.App.Sha256);
        Assert.Equal(new Version(2026, 11, 22, 1), m.RulePack!.Version);
        Assert.Equal("rulepack.json", m.RulePack.File);
    }

    [Fact]
    public void A_leading_byte_order_mark_is_ignored()
    {
        Assert.NotNull(ReleaseManifest.Parse("﻿" + Json()).App);
    }

    [Fact]
    public void A_release_may_carry_only_a_rule_pack_or_only_the_app()
    {
        Assert.Null(ReleaseManifest.Parse(Json(app: "null")).App);
        Assert.Null(ReleaseManifest.Parse(Json(rulePack: "null")).RulePack);
    }

    [Theory]
    [InlineData("""{ "format": 2 }""")]
    [InlineData("""{ "format": 1 }""")]
    [InlineData("""{ "format": 1, "app": null, "rulePack": null }""")]
    [InlineData("[]")]
    [InlineData("nope")]
    public void Unusable_manifests_are_refused(string json)
    {
        Assert.Throws<UpdateException>(() => ReleaseManifest.Parse(json));
    }

    [Theory]
    [InlineData("""{ "version": "soon", "file": "a.exe", "size": 1, "sha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef" }""")]
    [InlineData("""{ "version": "1.0.0", "file": "../evil.exe", "size": 1, "sha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef" }""")]
    [InlineData("""{ "version": "1.0.0", "file": "C:\\evil.exe", "size": 1, "sha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef" }""")]
    [InlineData("""{ "version": "1.0.0", "file": "a.exe", "size": 0, "sha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef" }""")]
    [InlineData("""{ "version": "1.0.0", "file": "a.exe", "size": 1, "sha256": "abc" }""")]
    [InlineData("""{ "version": "1.0.0", "file": "a.exe", "size": 1 }""")]
    public void Bad_asset_entries_are_refused(string asset)
    {
        Assert.Throws<UpdateException>(() => ReleaseManifest.Parse(Json(app: asset)));
    }

    [Fact]
    public void Assets_over_their_size_limit_are_refused()
    {
        Assert.Throws<UpdateException>(() => ReleaseManifest.Parse(Json(
            rulePack: $$"""{ "version": "2026.11.22.1", "file": "rulepack.json", "size": 2000000, "sha256": "{{Sha}}" }""")));
        Assert.Throws<UpdateException>(() => ReleaseManifest.Parse(Json(
            app: $$"""{ "version": "0.6.0", "file": "a.exe", "size": 600000000, "sha256": "{{Sha}}" }""")));
    }
}

public class UpdatePlannerTests
{
    const string Sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    static ReleaseManifest Manifest(string? app, string? pack) => new(
        app == null ? null : new ReleaseAsset(Version.Parse(app), "Tubiqo-Setup.exe", 10, Sha),
        pack == null ? null : new ReleaseAsset(Version.Parse(pack), "rulepack.json", 10, Sha));

    [Fact]
    public void Newer_app_and_rule_pack_are_both_offered()
    {
        var plan = UpdatePlanner.Plan(Manifest("0.6.0", "2026.11.22.1"), new Version(0, 5, 0), new Version(2026, 11, 21, 0));
        Assert.True(plan.AppUpdate);
        Assert.True(plan.RulePackUpdate);
    }

    [Theory]
    [InlineData("0.5.0", "2026.11.21.0")] // same
    [InlineData("0.4.9", "2026.11.20.9")] // older: never downgrade, even if correctly signed
    public void Same_or_older_versions_are_never_offered(string app, string pack)
    {
        var plan = UpdatePlanner.Plan(Manifest(app, pack), new Version(0, 5, 0), new Version(2026, 11, 21, 0));
        Assert.False(plan.AppUpdate);
        Assert.False(plan.RulePackUpdate);
        Assert.False(plan.Any);
    }

    [Fact]
    public void Missing_parts_are_simply_not_offered()
    {
        var plan = UpdatePlanner.Plan(Manifest(null, "2026.12.1.0"), new Version(0, 5, 0), new Version(2026, 11, 21, 0));
        Assert.False(plan.AppUpdate);
        Assert.True(plan.RulePackUpdate);
    }
}
