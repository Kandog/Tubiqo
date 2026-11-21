using Tubiqo.Core.Playback;

namespace Tubiqo.Core.Tests.Playback;

public class PlayerMessageTests
{
    [Fact]
    public void State_message_is_parsed()
    {
        var m = PlayerMessage.Parse("""
            {"type":"state","videoId":"aqz-KE-bpKQ","title":"Big Buck Bunny","author":"Blender","time":12.5,"duration":634,"state":1}
            """);

        var s = Assert.IsType<PlayerStateMessage>(m);
        Assert.Equal("aqz-KE-bpKQ", s.VideoId);
        Assert.Equal("Big Buck Bunny", s.Title);
        Assert.Equal("Blender", s.Author);
        Assert.Equal(12.5, s.TimeSeconds);
        Assert.Equal(634, s.DurationSeconds);
        Assert.Equal(PlayerState.Playing, s.State);
    }

    [Fact]
    public void Missing_state_fields_get_safe_defaults()
    {
        var s = Assert.IsType<PlayerStateMessage>(PlayerMessage.Parse("""{"type":"state","videoId":"aqz-KE-bpKQ"}"""));
        Assert.Equal("", s.Title);
        Assert.Equal(0, s.TimeSeconds);
        Assert.Equal(PlayerState.Unstarted, s.State);
    }

    [Fact]
    public void Error_message_is_parsed()
    {
        var e = Assert.IsType<PlayerErrorMessage>(PlayerMessage.Parse("""{"type":"error","code":150,"videoId":"aqz-KE-bpKQ"}"""));
        Assert.Equal(150, e.Code);
        Assert.Equal("aqz-KE-bpKQ", e.VideoId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("42")]
    [InlineData("\"a string\"")]
    [InlineData("""{"type":"unknown"}""")]
    [InlineData("""{"type":"state","videoId":"bad id"}""")]
    [InlineData("""{"type":"error","code":"x","videoId":"aqz-KE-bpKQ"}""")]
    [InlineData("""{"type":"state","videoId":"aqz-KE-bpKQ","time":"soon"}""")]
    public void Anything_malformed_is_ignored(string json)
    {
        Assert.Null(PlayerMessage.Parse(json));
    }

    [Fact]
    public void Very_long_titles_are_truncated()
    {
        var title = new string('x', 5000);
        var s = Assert.IsType<PlayerStateMessage>(PlayerMessage.Parse($$"""{"type":"state","videoId":"aqz-KE-bpKQ","title":"{{title}}"}"""));
        Assert.Equal(300, s.Title.Length);
    }
}
