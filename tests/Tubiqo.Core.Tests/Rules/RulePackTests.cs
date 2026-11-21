using Tubiqo.Core.Rules;

namespace Tubiqo.Core.Tests.Rules;

public class RulePackTests
{
    const string Valid = """
        {
          "format": 1,
          "version": "2026.11.21",
          "rules": [
            { "id": "ads", "title": "Block ads and trackers", "default": true,
              "hide": ["ytd-ad-slot-renderer", "#player-ads"],
              "block": ["*doubleclick.net/*", "*youtube.com/pagead/*"] },
            { "id": "related", "title": "Hide recommended videos", "default": false,
              "hide": ["#related"] },
            { "id": "hover-previews", "title": "No hover previews", "default": true }
          ]
        }
        """;

    [Fact]
    public void A_valid_pack_is_parsed()
    {
        var pack = RulePack.Parse(Valid);

        Assert.Equal("2026.11.21", pack.Version);
        Assert.Equal(["ads", "related", "hover-previews"], pack.Rules.Select(r => r.Id));
        var ads = pack.Rules[0];
        Assert.Equal("Block ads and trackers", ads.Title);
        Assert.True(ads.Default);
        Assert.Equal(["ytd-ad-slot-renderer", "#player-ads"], ads.Hide);
        Assert.Equal(["*doubleclick.net/*", "*youtube.com/pagead/*"], ads.Block);
        Assert.Empty(pack.Rules[2].Hide);
        Assert.Empty(pack.Rules[2].Block);
    }

    [Fact]
    public void Unknown_properties_are_ignored_so_nothing_extra_can_be_smuggled_in()
    {
        var pack = RulePack.Parse("""
            { "format": 1, "version": "1", "script": "alert(1)",
              "rules": [ { "id": "x", "title": "X", "default": true, "script": "alert(2)" } ] }
            """);
        Assert.Single(pack.Rules);
    }

    [Theory]
    [InlineData("not json", "JSON")]
    [InlineData("[]", "object")]
    [InlineData("""{ "format": 2, "version": "1", "rules": [] }""", "format")]
    [InlineData("""{ "version": "1", "rules": [] }""", "format")]
    [InlineData("""{ "format": 1, "version": "", "rules": [] }""", "version")]
    [InlineData("""{ "format": 1, "version": "1" }""", "rules")]
    [InlineData("""{ "format": 1, "version": "1", "rules": [ { "title": "X", "default": true } ] }""", "id")]
    [InlineData("""{ "format": 1, "version": "1", "rules": [ { "id": "Bad Id!", "title": "X", "default": true } ] }""", "id")]
    [InlineData("""{ "format": 1, "version": "1", "rules": [ { "id": "x", "default": true } ] }""", "title")]
    [InlineData("""{ "format": 1, "version": "1", "rules": [ { "id": "x", "title": "X" } ] }""", "default")]
    [InlineData("""{ "format": 1, "version": "1", "rules": [ { "id": "x", "title": "X", "default": "yes" } ] }""", "default")]
    [InlineData("""{ "format": 1, "version": "1", "rules": [ { "id": "x", "title": "X", "default": true }, { "id": "x", "title": "Y", "default": true } ] }""", "duplicate")]
    [InlineData("""{ "format": 1, "version": "1", "rules": [ { "id": "x", "title": "X", "default": true, "hide": "#a" } ] }""", "hide")]
    [InlineData("""{ "format": 1, "version": "1", "rules": [ { "id": "x", "title": "X", "default": true, "hide": [42] } ] }""", "hide")]
    public void Malformed_packs_are_refused_with_a_reason(string json, string reasonContains)
    {
        var e = Assert.Throws<RulePackException>(() => RulePack.Parse(json));
        Assert.Contains(reasonContains, e.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("#a { color: red }")]
    [InlineData("#a } body { display: none")]
    [InlineData("#a; @import url(https://evil.example/x.css)")]
    [InlineData("@import 'x'")]
    [InlineData("div[style*='url(https://evil.example/']")]
    [InlineData("#a\\7b")]
    [InlineData("</style><script>alert(1)</script>")]
    [InlineData("")]
    [InlineData("   ")]
    public void Hide_entries_must_be_plain_selectors(string selector)
    {
        var json = $$"""{ "format": 1, "version": "1", "rules": [ { "id": "x", "title": "X", "default": true, "hide": [{{System.Text.Json.JsonSerializer.Serialize(selector)}}] } ] }""";
        var e = Assert.Throws<RulePackException>(() => RulePack.Parse(json));
        Assert.Contains("hide", e.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("**")]
    [InlineData("*youtube.com*")]
    [InlineData("*youtube.com/*")]
    [InlineData("*www.youtube.com/*")]
    [InlineData("*googlevideo.com/*")]
    [InlineData("*googlevideo.com/videoplayback*")]
    [InlineData("*ytimg.com/*")]
    [InlineData("*youtube-nocookie.com/embed/*")]
    [InlineData("*player.tubiqo.test*")]
    [InlineData("*youtubei/v1/player*")]
    [InlineData("*ad*")]
    [InlineData("*double*click.net*")]
    [InlineData("https://doubleclick.net/*")]
    public void Block_patterns_that_are_too_broad_or_hit_playback_are_refused(string pattern)
    {
        var json = $$"""{ "format": 1, "version": "1", "rules": [ { "id": "x", "title": "X", "default": true, "block": [{{System.Text.Json.JsonSerializer.Serialize(pattern)}}] } ] }""";
        var e = Assert.Throws<RulePackException>(() => RulePack.Parse(json));
        Assert.Contains("block", e.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("*doubleclick.net/*")]
    [InlineData("*youtube.com/pagead/*")]
    [InlineData("*youtube.com/api/stats/ads*")]
    [InlineData("*youtube.com/live_chat*")]
    public void Specific_block_patterns_are_accepted(string pattern)
    {
        var json = $$"""{ "format": 1, "version": "1", "rules": [ { "id": "x", "title": "X", "default": true, "block": ["{{pattern}}"] } ] }""";
        Assert.Equal([pattern], RulePack.Parse(json).Rules[0].Block);
    }

    [Fact]
    public void Oversized_packs_are_refused()
    {
        var rules = string.Join(",", Enumerable.Range(0, 101).Select(i => $$"""{ "id": "r{{i}}", "title": "R", "default": true }"""));
        Assert.Throws<RulePackException>(() => RulePack.Parse($$"""{ "format": 1, "version": "1", "rules": [{{rules}}] }"""));

        var selectors = string.Join(",", Enumerable.Range(0, 201).Select(i => $"\"#s{i}\""));
        Assert.Throws<RulePackException>(() => RulePack.Parse(
            $$"""{ "format": 1, "version": "1", "rules": [ { "id": "x", "title": "X", "default": true, "hide": [{{selectors}}] } ] }"""));
    }
}
