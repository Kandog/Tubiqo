using Tubiqo.Core.Navigation;

namespace Tubiqo.Core.Tests.Navigation;

public class NavigationGuardTests
{
    static NavigationDecision Top(string url) => NavigationGuard.Classify(url, NavigationTarget.TopLevel);
    static NavigationDecision Frame(string url) => NavigationGuard.Classify(url, NavigationTarget.Frame);

    [Theory]
    [InlineData("https://www.youtube.com/")]
    [InlineData("https://youtube.com/watch?v=aqz-KE-bpKQ")]
    [InlineData("https://m.youtube.com/results?search_query=cats")]
    [InlineData("https://consent.youtube.com/m?continue=x")]
    [InlineData("https://youtu.be/aqz-KE-bpKQ")]
    [InlineData("https://www.youtube-nocookie.com/embed/aqz-KE-bpKQ")]
    [InlineData("https://player.tubiqo.test/player.html")]
    [InlineData("HTTPS://WWW.YOUTUBE.COM/")]
    [InlineData("https://accounts.google.com/ServiceLogin?service=youtube")] // Google sign-in stays inside Tubiqo
    [InlineData("https://accounts.youtube.com/accounts/SetSID")]
    [InlineData("https://accounts.google.ca/accounts/SetSID?ssdc=1")] // Google sets the session cookie on the country domain too
    [InlineData("https://accounts.google.co.uk/accounts/SetSID")]
    [InlineData("https://accounts.google.com.au/accounts/SetSID")]
    [InlineData("https://accounts.google.de/accounts/SetSID")]
    public void Allowed_domains_over_https_are_allowed(string url)
    {
        Assert.Equal(NavigationAction.Allow, Top(url).Action);
    }

    [Theory]
    [InlineData("https://evilyoutube.com/")]
    [InlineData("https://youtube.com.evil.com/")]
    [InlineData("https://youtube.com@evil.com/")]
    [InlineData("https://www.google.com/")]
    [InlineData("https://www.google.com/recaptcha/api2/anchor")] // reCAPTCHA is only ever a frame
    [InlineData("https://myaccount.google.com/")]
    [InlineData("https://x.accounts.google.com/")]
    [InlineData("https://accounts.google.ca/")] // country domains only for their /accounts/ pages
    [InlineData("https://accounts.google.ca/ServiceLogin")]
    [InlineData("https://accounts.google.evil.com/accounts/SetSID")]
    [InlineData("https://accounts.google.ca.evil.com/accounts/SetSID")]
    [InlineData("https://www.google.ca/accounts/SetSID")]
    public void Other_hosts_at_top_level_are_offered_to_the_default_browser(string url)
    {
        var d = Top(url);
        Assert.Equal(NavigationAction.OpenExternally, d.Action);
        Assert.Equal(new Uri(url).AbsoluteUri, d.Url);
    }

    [Theory]
    [InlineData("https://www.youtube.com:8443/")]
    [InlineData("https://player.tubiqo.test:444/player.html")]
    public void Allowed_hosts_on_non_default_ports_are_blocked(string url)
    {
        Assert.Equal(NavigationAction.Block, Top(url).Action);
    }

    [Fact]
    public void Plain_http_to_an_allowed_domain_is_upgraded_to_https()
    {
        var d = Top("http://www.youtube.com/watch?v=abc");
        Assert.Equal(NavigationAction.UpgradeToHttps, d.Action);
        Assert.Equal("https://www.youtube.com/watch?v=abc", d.Url);
    }

    [Fact]
    public void Plain_http_to_another_host_is_offered_to_the_default_browser()
    {
        Assert.Equal(NavigationAction.OpenExternally, Top("http://example.com/").Action);
    }

    [Fact]
    public void Youtube_redirect_links_are_unwrapped_to_their_target()
    {
        var d = Top("https://www.youtube.com/redirect?event=video_description&q=https%3A%2F%2Fexample.com%2Fpage%3Fa%3D1");
        Assert.Equal(NavigationAction.OpenExternally, d.Action);
        Assert.Equal("https://example.com/page?a=1", d.Url);
    }

    [Fact]
    public void Youtube_redirect_to_an_allowed_domain_is_allowed()
    {
        var d = Top("https://www.youtube.com/redirect?q=https%3A%2F%2Fwww.youtube.com%2Fwatch%3Fv%3Dabc");
        Assert.Equal(NavigationAction.Allow, d.Action);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("data:text/html,<b>hi</b>")]
    [InlineData("ms-settings:privacy")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("not a url")]
    [InlineData("")]
    public void Non_web_and_malformed_urls_are_blocked(string url)
    {
        Assert.Equal(NavigationAction.Block, Top(url).Action);
        Assert.Equal(NavigationAction.Block, Frame(url).Action);
    }

    [Theory]
    [InlineData("https://googleads.g.doubleclick.net/pagead/ads")]
    [InlineData("https://www.google.com/search?q=recaptcha")]
    [InlineData("https://www.google.com/recaptchafake/")]
    public void Frames_from_other_hosts_are_blocked_not_opened_externally(string url)
    {
        Assert.Equal(NavigationAction.Block, Frame(url).Action);
    }

    [Theory]
    [InlineData("https://accounts.google.com/RotateCookiesPage")] // keeps a signed-in session alive
    [InlineData("https://www.google.com/recaptcha/api2/anchor")] // challenge shown during sign-in
    [InlineData("https://www.google.com/recaptcha/enterprise/anchor")]
    public void Sign_in_frames_are_allowed(string url)
    {
        Assert.Equal(NavigationAction.Allow, Frame(url).Action);
    }

    [Theory]
    [InlineData("about:blank")]
    [InlineData("about:srcdoc")]
    public void Blank_frames_are_allowed(string url)
    {
        Assert.Equal(NavigationAction.Allow, Frame(url).Action);
    }

    [Fact]
    public void Blank_page_is_not_a_valid_top_level_destination()
    {
        Assert.Equal(NavigationAction.Block, Top("about:blank").Action);
    }

    [Theory]
    [InlineData("www.youtube.com", true)]
    [InlineData("youtube.com", true)]
    [InlineData("player.tubiqo.test", true)]
    [InlineData("notyoutube.com", false)]
    [InlineData("accounts.google.com", true)]
    [InlineData("google.com", false)]
    [InlineData("accounts.google.com.evil.com", false)]
    [InlineData("i.ytimg.com", false)] // image host: loaded by pages, never navigated to
    public void Allowed_domains_match_whole_labels_only(string host, bool expected)
    {
        Assert.Equal(expected, AllowedDomains.Contains(host));
    }
}
