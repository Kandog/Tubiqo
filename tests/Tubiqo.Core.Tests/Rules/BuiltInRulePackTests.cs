using Tubiqo.Core.Rules;

namespace Tubiqo.Core.Tests.Rules;

/// <summary>The Rule Pack shipped inside the app must always pass the same checks as a downloaded one.</summary>
public class BuiltInRulePackTests
{
    static RulePack Load() => RulePack.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "default-rulepack.json")));

    [Fact]
    public void Built_in_pack_is_valid()
    {
        Assert.NotEmpty(Load().Rules);
    }

    [Fact]
    public void Built_in_pack_version_can_be_compared_with_downloaded_packs()
    {
        Assert.True(Version.TryParse(Load().Version, out _));
    }

    [Fact]
    public void Built_in_pack_has_every_rule_from_the_design_with_its_agreed_default()
    {
        var defaults = Load().Rules.ToDictionary(r => r.Id, r => r.Default);
        Assert.Equal(new Dictionary<string, bool>
        {
            ["ads"] = true, ["hover-previews"] = true, ["shorts"] = true, ["comments"] = true,
            ["live-chat"] = true, ["related"] = false, ["autoplay-next"] = true, ["home-feed"] = true,
            ["google-account-links"] = true,
        }, defaults);
    }

    [Fact]
    public void Template_without_the_trim_placeholder_is_refused()
    {
        var plan = new TrimPlan("", [], []);
        Assert.Throws<ArgumentException>(() => plan.BuildScript("(() => {})();"));
        Assert.Equal("""const c = {"enabled":[],"hideCss":""};""", plan.BuildScript("const c = /*TUBIQO_TRIM*/null;"));
    }
}
