using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Tubiqo.Core.Updates;

/// <summary>The latest GitHub release: its signed manifest (not yet verified) and the download links of its files.</summary>
public sealed record LatestRelease(string Tag, byte[] Manifest, string Signature, IReadOnlyDictionary<string, Uri> Assets);

/// <summary>
/// Reads releases of the Tubiqo repo from GitHub. Only https to GitHub's own hosts is allowed,
/// redirects are followed by hand so each hop is checked, nothing identifying is sent
/// (generic User-Agent, no cookies), and every download is size-capped and hash-checked.
/// The handler must not follow redirects or keep cookies on its own.
/// </summary>
public sealed class GitHubReleaseClient
{
    public static readonly Uri DefaultApiBase = new("https://api.github.com/");
    const string UserAgent = "Tubiqo-Updater";
    const int MaxRedirects = 5;
    const long MaxApiResponse = 1024 * 1024;

    static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "api.github.com", "github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com",
    };

    readonly HttpClient http;
    readonly Uri latestUrl;
    readonly bool allowLocalhost;

    /// <param name="allowLocalhost">Test builds only: also allow plain-http localhost, for a fake GitHub.</param>
    public GitHubReleaseClient(HttpMessageHandler handler, Uri apiBase, string repo, bool allowLocalhost = false)
    {
        http = new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromMinutes(5) };
        latestUrl = new Uri(apiBase, $"repos/{repo}/releases/latest");
        this.allowLocalhost = allowLocalhost;
    }

    /// <summary>The latest release, or null when the repo has no published release yet.</summary>
    public async Task<LatestRelease?> FetchLatestAsync(CancellationToken ct)
    {
        using var response = await SendAsync(latestUrl, ct, accept: "application/vnd.github+json");
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        EnsureSuccess(response);
        var json = SkipBom(await ReadLimitedAsync(response, MaxApiResponse, ct));

        string tag;
        var assets = new Dictionary<string, Uri>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(json);
            tag = doc.RootElement.TryGetProperty("tag_name", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : "";
            if (doc.RootElement.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in list.EnumerateArray())
                {
                    if (a.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String &&
                        a.TryGetProperty("browser_download_url", out var u) && u.ValueKind == JsonValueKind.String &&
                        Uri.TryCreate(u.GetString(), UriKind.Absolute, out var uri))
                    {
                        assets[n.GetString()!] = uri;
                    }
                }
            }
        }
        catch (JsonException) { throw new UpdateException("GitHub's release information couldn't be read."); }

        if (!assets.TryGetValue(ReleaseManifest.FileName, out var manifestUrl) ||
            !assets.TryGetValue(ReleaseManifest.SignatureFileName, out var signatureUrl))
            throw new UpdateException($"The latest release ({tag}) has no {ReleaseManifest.FileName} and signature.");

        var manifest = await DownloadLimitedAsync(manifestUrl, ReleaseManifest.MaxManifestSize, ct);
        var signature = Encoding.ASCII.GetString(await DownloadLimitedAsync(signatureUrl, ReleaseManifest.MaxSignatureSize, ct));
        return new LatestRelease(tag, manifest, signature, assets);
    }

    /// <summary>Downloads a small asset into memory, checking it against the signed size and hash.</summary>
    public async Task<byte[]> DownloadAsync(Uri url, ReleaseAsset asset, CancellationToken ct)
    {
        var bytes = await DownloadLimitedAsync(url, asset.Size, ct);
        if (bytes.Length != asset.Size || Convert.ToHexStringLower(SHA256.HashData(bytes)) != asset.Sha256)
            throw new UpdateException($"{asset.File} doesn't match its signed size and hash.");
        return bytes;
    }

    /// <summary>Downloads a large asset (the installer) to a file; the file is deleted unless it matches the signed size and hash.</summary>
    public async Task DownloadToFileAsync(Uri url, ReleaseAsset asset, string path, CancellationToken ct)
    {
        var ok = false;
        try
        {
            using var response = await SendAsync(url, ct);
            EnsureSuccess(response);
            if (response.Content.Headers.ContentLength is long length && length != asset.Size)
                throw new UpdateException($"{asset.File} isn't the size the release says.");

            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, ct)) > 0)
                {
                    total += read;
                    if (total > asset.Size) throw new UpdateException($"{asset.File} is larger than the release says.");
                    sha.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                }
                if (total != asset.Size) throw new UpdateException($"{asset.File} is smaller than the release says.");
            }
            if (Convert.ToHexStringLower(sha.GetHashAndReset()) != asset.Sha256)
                throw new UpdateException($"{asset.File} doesn't match its signed hash.");
            ok = true;
        }
        finally
        {
            if (!ok && File.Exists(path)) File.Delete(path);
        }
    }

    async Task<byte[]> DownloadLimitedAsync(Uri url, long max, CancellationToken ct)
    {
        using var response = await SendAsync(url, ct);
        EnsureSuccess(response);
        return await ReadLimitedAsync(response, max, ct);
    }

    async Task<HttpResponseMessage> SendAsync(Uri url, CancellationToken ct, string accept = "application/octet-stream")
    {
        for (var hop = 0; ; hop++)
        {
            CheckUri(url);
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(UserAgent);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));

            HttpResponseMessage response;
            try { response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); }
            catch (HttpRequestException e) { throw new UpdateException("Couldn't reach GitHub: " + e.Message); }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new UpdateException("GitHub didn't answer in time."); }

            var status = (int)response.StatusCode;
            if (status is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                response.Dispose();
                if (hop >= MaxRedirects) throw new UpdateException("Too many redirects from GitHub.");
                url = location.IsAbsoluteUri ? location : new Uri(url, location);
                continue;
            }
            return response;
        }
    }

    void CheckUri(Uri url)
    {
        var localhost = allowLocalhost && url.IsLoopback;
        if (url.Scheme != Uri.UriSchemeHttps && !localhost)
            throw new UpdateException("Refused a non-https download link: " + url.Host);
        if (!AllowedHosts.Contains(url.IdnHost) && !localhost)
            throw new UpdateException("Refused a download from outside GitHub: " + url.Host);
    }

    static ReadOnlyMemory<byte> SkipBom(byte[] bytes) =>
        bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? bytes.AsMemory(Encoding.UTF8.Preamble.Length) : bytes;

    static void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw new UpdateException($"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase}.");
    }

    static async Task<byte[]> ReadLimitedAsync(HttpResponseMessage response, long max, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength is long length && length > max)
            throw new UpdateException("A download was larger than allowed.");
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[16384];
        int read;
        while ((read = await input.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > max) throw new UpdateException("A download was larger than allowed.");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
