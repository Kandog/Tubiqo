# PROTOTYPE results — memory/CPU probe (2026-11-21)

**Question:** Does a bare WebView2 host with AV1 excluded play YouTube under 400 MB and 10% CPU on this PC (i3-8100, UHD 630, 7.8 GB RAM), versus Chrome?

Test: Big Buck Bunny (`aqz-KE-bpKQ`), maximized window, fresh profile each run, 30 s warmup, then 120 s sampled every 5 s across the whole process tree. Memory = private working set (what Task Manager shows). Single run each, so treat these as rough numbers.

| Run | Codec / res | Avg CPU | Max CPU | Avg memory | Max memory |
|---|---|---|---|---|---|
| h2o (AV1 excluded) | VP9 720p | 4.6% | 15.9% | 527 MB | 731 MB |
| h2o forced 1080p | VP9 1080p | 5.1% | 14.8% | 651 MB | 773 MB |
| h2o AV1 allowed | AV1 720p | 6.9% | 11.5% | 451 MB | 552 MB |
| Chrome, fresh profile | auto (not probed) | 6.8% | 12.2% | 502 MB | 531 MB |

## Verdict

- **CPU target (<10%) is met** with room to spare, even at 1080p. Excluding AV1 cut CPU by about a third at 720p (6.9% → 4.6%).
- **Memory target (<400 MB) is NOT met.** WebView2 with YouTube's full watch page uses 450–770 MB, no better than a fresh Chrome profile. The engine is the same; the weight is YouTube's page.
- **The user's 1 GB+ Chrome comes from their profile** (extensions, other tabs, accumulated data), not from Chrome itself: a clean Chrome uses about 500 MB for the same video.
- **Open issue:** in the forced-1080p run, playback stopped about 75 s in and the video element reset (res 0x0) while ad requests were being blocked. This may be naive ad blocking stalling an ad break. Not yet diagnosed.
- Trim Rules (hiding comments, recommendations etc.) were not part of this prototype, so their memory effect is still unmeasured.

## Follow-up: embed-only player (same day)

`--embed` loads only `youtube-nocookie.com/embed/<id>` in an iframe on a local virtual-host page (`https://player.h2o.test/`), which supplies the Referer that embeds require.

| Run | Codec / res | Avg CPU | Max CPU | Avg memory | Max memory |
|---|---|---|---|---|---|
| embed run 1 | VP9 1080p | 1.6% | 2.1% | 252 MB | 267 MB |
| embed run 2 | VP9 1080p | 1.8% | 4.2% | 254 MB | 277 MB |

**Verdict:** playing in the embed player meets both targets with a wide margin: about 250 MB (versus 650 MB for the full watch page at 1080p, and 400 MB target) and under 2% CPU at 1080p. It played continuously in both runs (no stalls, about 5–9 dropped frames out of 8,500), and no ad requests were seen. It auto-selected 1080p in a full-screen-width window, so no quality forcing was needed.

Not tested: videos whose owners disable embedding (YouTube errors 101/150), age-restricted videos, live streams, and how long-running sessions behave.
