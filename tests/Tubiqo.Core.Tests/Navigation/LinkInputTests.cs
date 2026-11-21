using Tubiqo.Core.Navigation;

namespace Tubiqo.Core.Tests.Navigation;

public class LinkInputTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=aqz-KE-bpKQ", "https://player.tubiqo.test/player.html?v=aqz-KE-bpKQ")]
    [InlineData("  https://youtu.be/aqz-KE-bpKQ?t=90  ", "https://player.tubiqo.test/player.html?v=aqz-KE-bpKQ&t=90")]
    [InlineData("youtu.be/aqz-KE-bpKQ", "https://player.tubiqo.test/player.html?v=aqz-KE-bpKQ")]
    [InlineData("www.youtube.com/watch?v=aqz-KE-bpKQ", "https://player.tubiqo.test/player.html?v=aqz-KE-bpKQ")]
    [InlineData("http://youtube.com/shorts/aqz-KE-bpKQ", "https://player.tubiqo.test/player.html?v=aqz-KE-bpKQ")]
    [InlineData("aqz-KE-bpKQ", "https://player.tubiqo.test/player.html?v=aqz-KE-bpKQ")]
    public void Video_links_and_ids_open_in_the_lean_player(string input, string expected)
    {
        var r = LinkInput.Resolve(input);
        Assert.True(r.Ok);
        Assert.Equal(expected, r.Url);
    }

    [Theory]
    [InlineData("https://www.youtube.com/@Blender", "https://www.youtube.com/@Blender")]
    [InlineData("youtube.com/results?search_query=cats", "https://youtube.com/results?search_query=cats")]
    [InlineData("http://m.youtube.com/playlist?list=PL1", "https://m.youtube.com/playlist?list=PL1")]
    public void Other_youtube_pages_open_as_browse_pages(string input, string expected)
    {
        var r = LinkInput.Resolve(input);
        Assert.True(r.Ok);
        Assert.Equal(expected, r.Url);
    }

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("https://youtube.com.evil.com/watch?v=aqz-KE-bpKQ")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows")]
    [InlineData("https://player.tubiqo.test/player.html?v=aqz-KE-bpKQ")]
    [InlineData("big buck bunny")]
    [InlineData("")]
    [InlineData("   ")]
    public void Anything_else_is_refused_with_a_reason(string input)
    {
        var r = LinkInput.Resolve(input);
        Assert.False(r.Ok);
        Assert.NotEmpty(r.Error);
    }

    [Fact]
    public void Clipboard_text_is_offered_only_when_it_is_a_youtube_link()
    {
        Assert.True(LinkInput.LooksLikeYouTubeLink("https://youtu.be/aqz-KE-bpKQ"));
        Assert.False(LinkInput.LooksLikeYouTubeLink("some copied sentence"));
        Assert.False(LinkInput.LooksLikeYouTubeLink("https://example.com"));
        Assert.False(LinkInput.LooksLikeYouTubeLink(new string('x', 5000)));
    }
}
