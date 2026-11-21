using H2o.Core.Playback;
using H2o.Core.Rules;
using H2o.Core.Storage;

namespace H2o.Core.Tests.Rules;

public sealed class TrimPlanTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "h2o-tests-" + Guid.NewGuid().ToString("N"));
    readonly H2oDatabase db;

    readonly RulePack pack = RulePack.Parse("""
        { "format": 1, "version": "1", "rules": [
          { "id": "ads", "title": "Ads", "default": true, "hide": ["#ad1", ".ad2"], "block": ["*doubleclick.net/*", "*youtube.com/pagead/*"] },
          { "id": "chat", "title": "Chat", "default": true, "hide": ["#chat"], "block": ["*youtube.com/live_chat*", "*doubleclick.net/*"] },
          { "id": "related", "title": "Related", "default": false, "hide": ["#related"] },
          { "id": "hover-previews", "title": "Hover", "default": true }
        ] }
        """);

    public TrimPlanTests()
    {
        Directory.CreateDirectory(dir);
        db = H2oDatabase.Open(Path.Combine(dir, "h2o.db"));
    }

    public void Dispose()
    {
        db.Dispose();
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Rules_follow_their_pack_default_until_the_user_changes_them()
    {
        var settings = new TrimRuleSettings(db);
        Assert.True(settings.IsEnabled(pack.Rules[0]));
        Assert.False(settings.IsEnabled(pack.Rules[2]));

        settings.SetEnabled("ads", false);
        settings.SetEnabled("related", true);

        var reopened = new TrimRuleSettings(db);
        Assert.False(reopened.IsEnabled(pack.Rules[0]));
        Assert.True(reopened.IsEnabled(pack.Rules[2]));
    }

    [Fact]
    public void Plan_hides_each_enabled_selector_in_its_own_css_rule()
    {
        var plan = TrimPlan.From(pack, new TrimRuleSettings(db));

        // One rule per selector, so one selector the browser doesn't understand can't disable the rest.
        Assert.Equal(
            "#ad1{display:none!important}\n.ad2{display:none!important}\n#chat{display:none!important}\n",
            plan.HideCss);
    }

    [Fact]
    public void Plan_blocks_enabled_patterns_once_each()
    {
        var plan = TrimPlan.From(pack, new TrimRuleSettings(db));
        Assert.Equal(["*doubleclick.net/*", "*youtube.com/pagead/*", "*youtube.com/live_chat*"], plan.BlockPatterns);
    }

    [Fact]
    public void Disabled_rules_contribute_nothing()
    {
        var settings = new TrimRuleSettings(db);
        settings.SetEnabled("ads", false);
        settings.SetEnabled("chat", false);
        settings.SetEnabled("hover-previews", false);

        var plan = TrimPlan.From(pack, settings);

        Assert.Equal("", plan.HideCss);
        Assert.Empty(plan.BlockPatterns);
        Assert.Empty(plan.EnabledRuleIds);
    }

    [Fact]
    public void Plan_lists_enabled_rule_ids_for_rules_the_app_implements_in_script()
    {
        var plan = TrimPlan.From(pack, new TrimRuleSettings(db));
        Assert.Equal(["ads", "chat", "hover-previews"], plan.EnabledRuleIds);
        Assert.Equal("""{"enabled":["ads","chat","hover-previews"],"hideCss":"#ad1{display:none!important}\n.ad2{display:none!important}\n#chat{display:none!important}\n"}""", plan.ToScriptConfig());
    }

    [Fact]
    public void Settings_for_rules_no_longer_in_the_pack_are_harmless()
    {
        new TrimRuleSettings(db).SetEnabled("removed-rule", true);
        Assert.Equal(["ads", "chat", "hover-previews"], TrimPlan.From(pack, new TrimRuleSettings(db)).EnabledRuleIds);
    }
}

public sealed class PlaybackSettingsTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "h2o-tests-" + Guid.NewGuid().ToString("N"));
    readonly H2oDatabase db;

    public PlaybackSettingsTests()
    {
        Directory.CreateDirectory(dir);
        db = H2oDatabase.Open(Path.Combine(dir, "h2o.db"));
    }

    public void Dispose()
    {
        db.Dispose();
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Default_policy_until_changed_then_the_saved_cap_persists()
    {
        var settings = new PlaybackSettings(db);
        Assert.Equal(PlaybackPolicy.Default, settings.Policy);

        settings.Policy = PlaybackPolicy.Default.WithQualityCap(720);

        Assert.Equal(720, new PlaybackSettings(db).Policy.QualityCap);
        Assert.False(new PlaybackSettings(db).Policy.AllowAv1);
    }

    [Fact]
    public void A_corrupt_stored_cap_falls_back_to_the_default()
    {
        db.SetSetting("playback.qualityCap", "999");
        Assert.Equal(PlaybackPolicy.Default, new PlaybackSettings(db).Policy);
    }
}
