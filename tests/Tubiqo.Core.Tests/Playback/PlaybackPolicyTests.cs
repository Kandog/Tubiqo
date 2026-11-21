using System.Text.Json;
using Tubiqo.Core.Playback;

namespace Tubiqo.Core.Tests.Playback;

public class PlaybackPolicyTests
{
    [Fact]
    public void Default_excludes_av1_and_caps_at_1080p()
    {
        var p = PlaybackPolicy.Default;
        Assert.False(p.AllowAv1);
        Assert.Equal(1080, p.QualityCap);
    }

    [Theory]
    [InlineData(144)]
    [InlineData(240)]
    [InlineData(360)]
    [InlineData(480)]
    [InlineData(720)]
    [InlineData(1080)]
    [InlineData(1440)]
    [InlineData(2160)]
    public void Standard_youtube_resolutions_are_valid_caps(int cap)
    {
        Assert.Equal(cap, PlaybackPolicy.Default.WithQualityCap(cap).QualityCap);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1000)]
    [InlineData(4320)]
    public void Other_caps_are_rejected(int cap)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PlaybackPolicy.Default.WithQualityCap(cap));
    }

    [Fact]
    public void Script_config_is_plain_json_the_page_script_reads()
    {
        var json = PlaybackPolicy.Default.WithQualityCap(720).ToScriptConfig();
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("allowAv1").GetBoolean());
        Assert.Equal(720, doc.RootElement.GetProperty("maxShortSide").GetInt32());
        Assert.Equal(2, doc.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public void Script_is_built_by_filling_the_policy_placeholder()
    {
        const string template = "(() => { const policy = /*TUBIQO_POLICY*/null; })();";
        var script = PlaybackPolicy.Default.BuildScript(template);
        Assert.Equal("(() => { const policy = {\"allowAv1\":false,\"maxShortSide\":1080}; })();", script);
    }

    [Fact]
    public void A_template_without_the_placeholder_is_refused()
    {
        Assert.Throws<ArgumentException>(() => PlaybackPolicy.Default.BuildScript("(() => {})();"));
    }
}
