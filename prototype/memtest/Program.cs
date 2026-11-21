// PROTOTYPE — throwaway. Answers one question: does a bare WebView2 host with AV1 excluded
// play YouTube at 1080p under ~400 MB / ~10% CPU on this PC, versus Chrome?
// Flags: --allow-av1   (don't exclude AV1, to isolate its effect)
//        --force-1080  (ask YouTube's player for 1080p)
//        --log <file>  (append probe state every 2 s)
//        <url>         (video to play; default Big Buck Bunny)
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

public static class Program
{
    [STAThread]
    public static void Main(string[] args) => new Application().Run(new ProtoWindow(args));
}

class ProtoWindow : Window
{
    readonly WebView2 wv = new();
    readonly bool blockAv1, force1080, embed;
    readonly string url, logFile;
    string dataDir;
    int blocked;

    // Ad/tracker URL patterns. Filtering only these (not "*") keeps video segment
    // requests off the host-process round trip.
    static readonly string[] BlockPatterns = {
        "*doubleclick.net/*", "*googlesyndication.com/*", "*googleadservices.com/*",
        "*google-analytics.com/*", "*youtube.com/pagead/*", "*youtube.com/api/stats/ads*",
        "*youtube.com/ptracking*", "*youtube.com/get_midroll_*",
    };

    const string Av1Script = """
        (() => {
          const bad = t => /av01|av1/i.test(String(t || ''));
          for (const MS of [window.MediaSource, window.ManagedMediaSource]) {
            if (!MS) continue;
            const o = MS.isTypeSupported.bind(MS);
            MS.isTypeSupported = t => bad(t) ? false : o(t);
          }
          const cp = HTMLMediaElement.prototype.canPlayType;
          HTMLMediaElement.prototype.canPlayType = function (t) { return bad(t) ? '' : cp.call(this, t); };
          const mc = navigator.mediaCapabilities;
          if (mc && mc.decodingInfo) {
            const d = mc.decodingInfo.bind(mc);
            mc.decodingInfo = c => bad(c && c.video && c.video.contentType)
              ? Promise.resolve({ supported: false, smooth: false, powerEfficient: false }) : d(c);
          }
        })();
        """;

    const string ProbeScript = """
        (() => {
          const s = window.__h2o = { codecs: [] };
          const MS = window.MediaSource;
          if (MS) {
            const a = MS.prototype.addSourceBuffer;
            MS.prototype.addSourceBuffer = function (m) {
              s.codecs.push(m); if (s.codecs.length > 4) s.codecs.shift();
              return a.call(this, m);
            };
          }
          setInterval(() => {
            if (window.top !== window) window.top.postMessage({ h2oProbe: s }, '*');
            const v = document.querySelector('video');
            if (v) {
              s.res = v.videoWidth + 'x' + v.videoHeight;
              s.playing = !v.paused;
              const q = v.getVideoPlaybackQuality && v.getVideoPlaybackQuality();
              if (q) { s.dropped = q.droppedVideoFrames; s.frames = q.totalVideoFrames; }
            }
            if (window.__h2oForce1080 && !s.forced) {
              const p = document.getElementById('movie_player');
              if (p && p.setPlaybackQualityRange) {
                try { p.setPlaybackQualityRange('hd1080', 'hd1080'); s.forced = true; } catch (e) {}
              }
            }
          }, 1000);
        })();
        """;

    public ProtoWindow(string[] args)
    {
        blockAv1 = !args.Contains("--allow-av1");
        force1080 = args.Contains("--force-1080");
        embed = args.Contains("--embed");
        var li = Array.IndexOf(args, "--log");
        logFile = li >= 0 && li + 1 < args.Length ? args[li + 1] : null;
        url = args.FirstOrDefault(a => a.StartsWith("http")) ?? "https://www.youtube.com/watch?v=aqz-KE-bpKQ";

        Title = "h2o PROTOTYPE";
        Width = 1600; Height = 950;
        WindowState = WindowState.Maximized;
        Content = wv;
        Loaded += async (_, _) => await Init();
        Closed += (_, _) =>
        {
            try { wv.Dispose(); Thread.Sleep(800); Directory.Delete(dataDir, true); } catch { }
        };
    }

    async Task Init()
    {
        // Fresh, throwaway profile each run = "Site Data wiped".
        dataDir = Path.Combine(Path.GetTempPath(), "h2o-PROTOTYPE-wipe-me-" + Guid.NewGuid().ToString("N"));
        var opts = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
        var env = await CoreWebView2Environment.CreateAsync(null, dataDir, opts);
        await wv.EnsureCoreWebView2Async(env);
        var core = wv.CoreWebView2;

        if (blockAv1) await core.AddScriptToExecuteOnDocumentCreatedAsync(Av1Script);
        if (force1080) await core.AddScriptToExecuteOnDocumentCreatedAsync("window.__h2oForce1080 = true;");
        await core.AddScriptToExecuteOnDocumentCreatedAsync(ProbeScript);

        foreach (var p in BlockPatterns)
            core.AddWebResourceRequestedFilter(p, CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) =>
        {
            e.Response = core.Environment.CreateWebResourceResponse(null, 403, "Blocked", "");
            blocked++;
        };
        core.NewWindowRequested += (_, e) => e.Handled = true;

        if (embed)
        {
            // Embeds need a real Referer (YouTube error 153 otherwise), so host a tiny local page
            // on a virtual HTTPS origin and put the embed player in an iframe.
            var id = url.Contains("v=") ? url.Split("v=")[1].Split('&')[0] : url.TrimEnd('/').Split('/').Last();
            var pageDir = Path.Combine(dataDir, "page");
            Directory.CreateDirectory(pageDir);
            File.WriteAllText(Path.Combine(pageDir, "player.html"), $$"""
                <!doctype html><html><head><meta charset="utf-8">
                <meta name="referrer" content="strict-origin-when-cross-origin">
                <style>html,body{margin:0;height:100%;background:#000;overflow:hidden}iframe{border:0;width:100%;height:100%}</style>
                </head><body>
                <iframe src="https://www.youtube-nocookie.com/embed/{{id}}?autoplay=1&rel=0&playsinline=1"
                  allow="autoplay; encrypted-media; fullscreen" allowfullscreen></iframe>
                <script>addEventListener('message', e => { if (e.data && e.data.h2oProbe) window.__h2o = e.data.h2oProbe; });</script>
                </body></html>
                """);
            core.SetVirtualHostNameToFolderMapping("player.h2o.test", pageDir, CoreWebView2HostResourceAccessKind.Deny);
            core.Navigate("https://player.h2o.test/player.html");
        }
        else core.Navigate(url);

        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        t.Tick += async (_, _) =>
        {
            string state;
            try { state = await core.ExecuteScriptAsync("JSON.stringify(window.__h2o || {})"); }
            catch { return; }
            state = System.Text.Json.JsonSerializer.Deserialize<string>(state) ?? "{}";
            var line = $"{DateTime.Now:HH:mm:ss} embed={embed} av1Blocked={blockAv1} force1080={force1080} adReqBlocked={blocked} {state}";
            Title = "h2o PROTOTYPE | " + line;
            if (logFile != null) File.AppendAllText(logFile, line + Environment.NewLine);
        };
        t.Start();
    }
}
