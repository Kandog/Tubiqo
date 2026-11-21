using System.Net;
using System.Security.Cryptography;
using System.Text;
using Tubiqo.Core.Updates;

namespace Tubiqo.Core.Tests.Updates;

public class GitHubReleaseClientTests
{
    sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    static HttpResponseMessage Ok(byte[] body) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
    static HttpResponseMessage Redirect(string to) => new(HttpStatusCode.Found) { Headers = { Location = new Uri(to) } };

    const string Api = "https://api.github.com/repos/Kandog/Tubiqo/releases/latest";
    const string ManifestUrl = "https://github.com/Kandog/Tubiqo/releases/download/v0.6.0/tubiqo-release.json";
    const string SigUrl = "https://github.com/Kandog/Tubiqo/releases/download/v0.6.0/tubiqo-release.json.sig";
    const string Cdn = "https://objects.githubusercontent.com/github-production-release-asset/";

    static string LatestJson(string manifestUrl = ManifestUrl) => $$"""
        { "tag_name": "v0.6.0", "assets": [
          { "name": "tubiqo-release.json", "browser_download_url": "{{manifestUrl}}" },
          { "name": "tubiqo-release.json.sig", "browser_download_url": "{{SigUrl}}" },
          { "name": "rulepack.json", "browser_download_url": "https://github.com/Kandog/Tubiqo/releases/download/v0.6.0/rulepack.json" }
        ] }
        """;

    static GitHubReleaseClient Client(FakeHandler h) => new(h, GitHubReleaseClient.DefaultApiBase, "Kandog/Tubiqo");

    [Fact]
    public async Task Latest_release_manifest_and_signature_are_fetched_following_github_redirects()
    {
        var h = new FakeHandler(r => r.RequestUri!.AbsoluteUri switch
        {
            Api => Ok(LatestJson()),
            ManifestUrl => Redirect(Cdn + "1"),
            SigUrl => Redirect(Cdn + "2"),
            Cdn + "1" => Ok("""{"format":1}"""),
            Cdn + "2" => Ok("c2ln"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });

        var latest = await Client(h).FetchLatestAsync(CancellationToken.None);

        Assert.NotNull(latest);
        Assert.Equal("v0.6.0", latest!.Tag);
        Assert.Equal("""{"format":1}""", Encoding.UTF8.GetString(latest.Manifest));
        Assert.Equal("c2ln", latest.Signature);
        Assert.True(latest.Assets.ContainsKey("rulepack.json"));
        Assert.All(h.Requests, r => Assert.Equal("Tubiqo-Updater", r.Headers.UserAgent.ToString()));
        Assert.All(h.Requests, r => Assert.False(r.Headers.Contains("Cookie")));
    }

    [Fact]
    public async Task Release_information_with_a_byte_order_mark_is_still_read()
    {
        var withBom = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(LatestJson())).ToArray();
        var h = new FakeHandler(r => r.RequestUri!.AbsoluteUri switch
        {
            Api => Ok(withBom),
            _ => Ok("x"),
        });
        Assert.Equal("v0.6.0", (await Client(h).FetchLatestAsync(CancellationToken.None))!.Tag);
    }

    [Fact]
    public async Task No_published_release_yet_is_not_an_error()
    {
        var h = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.Null(await Client(h).FetchLatestAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_release_without_a_manifest_is_reported()
    {
        var h = new FakeHandler(_ => Ok("""{ "tag_name": "v1", "assets": [] }"""));
        var e = await Assert.ThrowsAsync<UpdateException>(() => Client(h).FetchLatestAsync(CancellationToken.None));
        Assert.Contains("tubiqo-release.json", e.Message);
    }

    [Theory]
    [InlineData("https://evil.example/x")]
    [InlineData("http://objects.githubusercontent.com/x")]
    public async Task Redirects_to_other_hosts_or_plain_http_are_refused(string target)
    {
        var h = new FakeHandler(r => r.RequestUri!.AbsoluteUri == Api ? Ok(LatestJson()) : Redirect(target));
        await Assert.ThrowsAsync<UpdateException>(() => Client(h).FetchLatestAsync(CancellationToken.None));
        Assert.DoesNotContain(h.Requests, r => r.RequestUri!.Host == "evil.example" || r.RequestUri!.Scheme == "http");
    }

    [Fact]
    public async Task Asset_links_pointing_off_github_are_refused()
    {
        var h = new FakeHandler(r => r.RequestUri!.AbsoluteUri == Api ? Ok(LatestJson("https://evil.example/m.json")) : Ok("x"));
        await Assert.ThrowsAsync<UpdateException>(() => Client(h).FetchLatestAsync(CancellationToken.None));
    }

    [Fact]
    public async Task An_oversized_manifest_is_refused()
    {
        var h = new FakeHandler(r => r.RequestUri!.AbsoluteUri switch
        {
            Api => Ok(LatestJson()),
            _ => Ok(new byte[ReleaseManifest.MaxManifestSize + 1]),
        });
        await Assert.ThrowsAsync<UpdateException>(() => Client(h).FetchLatestAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Downloads_must_match_the_signed_size_and_hash()
    {
        var body = Encoding.UTF8.GetBytes("rule pack bytes");
        var sha = Convert.ToHexStringLower(SHA256.HashData(body));
        var url = new Uri("https://github.com/Kandog/Tubiqo/releases/download/v0.6.0/rulepack.json");
        var h = new FakeHandler(_ => Ok(body));
        var client = Client(h);

        Assert.Equal(body, await client.DownloadAsync(url, new ReleaseAsset(new Version(1, 0), "rulepack.json", body.Length, sha), CancellationToken.None));
        await Assert.ThrowsAsync<UpdateException>(() => client.DownloadAsync(url, new ReleaseAsset(new Version(1, 0), "rulepack.json", body.Length, new string('0', 64)), CancellationToken.None));
        await Assert.ThrowsAsync<UpdateException>(() => client.DownloadAsync(url, new ReleaseAsset(new Version(1, 0), "rulepack.json", body.Length - 1, sha), CancellationToken.None));
    }

    [Fact]
    public async Task Downloads_to_a_file_are_deleted_when_the_hash_is_wrong()
    {
        var body = new byte[10_000];
        var url = new Uri("https://github.com/Kandog/Tubiqo/releases/download/v0.6.0/Tubiqo-Setup.exe");
        var file = Path.Combine(Path.GetTempPath(), "tubiqo-tests-" + Guid.NewGuid().ToString("N") + ".exe");
        var client = Client(new FakeHandler(_ => Ok(body)));

        await Assert.ThrowsAsync<UpdateException>(() =>
            client.DownloadToFileAsync(url, new ReleaseAsset(new Version(1, 0), "Tubiqo-Setup.exe", body.Length, new string('0', 64)), file, CancellationToken.None));
        Assert.False(File.Exists(file));

        var sha = Convert.ToHexStringLower(SHA256.HashData(body));
        await client.DownloadToFileAsync(url, new ReleaseAsset(new Version(1, 0), "Tubiqo-Setup.exe", body.Length, sha), file, CancellationToken.None);
        Assert.Equal(body.Length, new FileInfo(file).Length);
        File.Delete(file);
    }
}
