using Tubiqo.Core.Playback;

namespace Tubiqo.Core.Tests.Playback;

public class LeanPlayerRouteTests
{
    const string Id = "aqz-KE-bpKQ";

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=aqz-KE-bpKQ", 0)]
    [InlineData("https://youtube.com/watch?feature=share&v=aqz-KE-bpKQ&list=PL123", 0)]
    [InlineData("https://m.youtube.com/watch?v=aqz-KE-bpKQ&t=90", 90)]
    [InlineData("https://www.youtube.com/watch?v=aqz-KE-bpKQ&t=90s", 90)]
    [InlineData("https://www.youtube.com/watch?v=aqz-KE-bpKQ&t=1m30s", 90)]
    [InlineData("https://www.youtube.com/watch?v=aqz-KE-bpKQ&t=1h2m3s", 3723)]
    [InlineData("https://www.youtube.com/watch?v=aqz-KE-bpKQ&start=42", 42)]
    [InlineData("https://youtu.be/aqz-KE-bpKQ", 0)]
    [InlineData("https://youtu.be/aqz-KE-bpKQ?t=15", 15)]
    [InlineData("https://www.youtube.com/shorts/aqz-KE-bpKQ", 0)]
    [InlineData("https://www.youtube.com/live/aqz-KE-bpKQ?si=abc", 0)]
    [InlineData("https://www.youtube.com/embed/aqz-KE-bpKQ?start=5", 5)]
    public void Video_links_go_to_the_lean_player(string url, int start)
    {
        Assert.True(LeanPlayerRoute.TryGetVideo(url, out var video));
        Assert.Equal(new VideoRequest(Id, start), video);
    }

    [Theory]
    [InlineData("https://www.youtube.com/")]
    [InlineData("https://www.youtube.com/results?search_query=cats")]
    [InlineData("https://www.youtube.com/@Blender")]
    [InlineData("https://www.youtube.com/playlist?list=PL123")]
    [InlineData("https://www.youtube.com/watch")]
    [InlineData("https://www.youtube.com/watch?v=short")]
    [InlineData("https://www.youtube.com/watch?v=aqz-KE-bpKQ<script>")]
    [InlineData("https://www.youtube.com/shorts/")]
    [InlineData("https://example.com/watch?v=aqz-KE-bpKQ")]
    [InlineData("https://player.tubiqo.test/player.html?v=aqz-KE-bpKQ")]
    [InlineData("not a url")]
    public void Other_pages_are_browse_pages(string url)
    {
        Assert.False(LeanPlayerRoute.TryGetVideo(url, out _));
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=aqz-KE-bpKQ&t=abc")]
    [InlineData("https://www.youtube.com/watch?v=aqz-KE-bpKQ&t=-5")]
    [InlineData("https://www.youtube.com/watch?v=aqz-KE-bpKQ&t=99999999999999")]
    public void Unreadable_start_times_start_from_the_beginning(string url)
    {
        Assert.True(LeanPlayerRoute.TryGetVideo(url, out var video));
        Assert.Equal(0, video.StartSeconds);
    }

    [Fact]
    public void Player_url_round_trips()
    {
        var url = LeanPlayerRoute.PlayerUrl(new VideoRequest(Id, 90));
        Assert.Equal("https://player.tubiqo.test/player.html?v=aqz-KE-bpKQ&t=90", url);
        Assert.True(LeanPlayerRoute.TryParsePlayerUrl(url, out var back));
        Assert.Equal(new VideoRequest(Id, 90), back);
    }

    [Fact]
    public void Player_url_without_start_omits_it()
    {
        Assert.Equal("https://player.tubiqo.test/player.html?v=aqz-KE-bpKQ", LeanPlayerRoute.PlayerUrl(new VideoRequest(Id, 0)));
    }

    [Theory]
    [InlineData("https://player.tubiqo.test/player.html")]
    [InlineData("https://player.tubiqo.test/player.html?v=bad")]
    [InlineData("https://player.tubiqo.test/other.html?v=aqz-KE-bpKQ")]
    [InlineData("https://player.tubiqo.test/player.js")]
    [InlineData("https://www.youtube.com/player.html?v=aqz-KE-bpKQ")]
    public void Only_a_well_formed_player_page_is_a_player_url(string url)
    {
        Assert.False(LeanPlayerRoute.TryParsePlayerUrl(url, out _));
    }

    [Fact]
    public void Fallback_url_is_a_marked_watch_page_that_is_not_rerouted()
    {
        var url = LeanPlayerRoute.FallbackUrl(new VideoRequest(Id, 30));
        Assert.Equal("https://www.youtube.com/watch?v=aqz-KE-bpKQ&t=30s&tubiqo=fallback", url);
        Assert.False(LeanPlayerRoute.TryGetVideo(url, out _));
        Assert.True(LeanPlayerRoute.IsFallback(url, out var id));
        Assert.Equal(Id, id);
    }

    [Fact]
    public void Ordinary_watch_page_is_not_a_fallback()
    {
        Assert.False(LeanPlayerRoute.IsFallback("https://www.youtube.com/watch?v=aqz-KE-bpKQ", out _));
    }

    [Theory]
    [InlineData(101, true)]
    [InlineData(150, true)]
    [InlineData(153, true)]
    [InlineData(100, false)] // video removed or private: the watch page can't play it either
    [InlineData(2, false)]
    [InlineData(5, false)]
    public void Only_embedding_refusals_fall_back_to_the_watch_page(int code, bool expected)
    {
        Assert.Equal(expected, LeanPlayerRoute.NeedsWatchPageFallback(code));
    }
}
