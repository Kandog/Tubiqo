# Play videos in the Lean Player, not on the watch page

Amends ADR 0002. Users still browse YouTube's own Browse Pages, but opening a video takes them to the **Lean Player**: YouTube's embeddable player (`youtube-nocookie.com/embed/<id>`) shown alone in an iframe on a local Tubiqo page (`https://player.tubiqo.test/`, a WebView2 virtual host), with the Browse Pages unloaded. Measured on the target PC (i3-8100, UHD 630), this used about 250 MB and under 2% CPU at 1080p VP9. The full watch page used about 650 MB and 5% CPU, and a fresh Chrome profile about 500 MB and 7%. The full watch page alone can't meet the 400 MB target, no matter which Trim Rules are on (see `prototype/memtest/RESULTS.md` on the `prototype/memtest` branch).

## Considered Options

- **Full watch page with Trim Rules** (original ADR 0002): keeps the description, comments and recommendations, but measured at 450–770 MB, and one test stalled during ad blocking.
- **Load the embed URL directly as the top-level page**: YouTube refuses embeds without a Referer (error 153), so Tubiqo needs its own local page around the iframe.

## Consequences

- The watch page's recommendations sidebar, description and comments are gone during normal playback. The user finds the next video by going back to the Browse Pages or Watch History. Trim Rules for comments and the recommendations sidebar now matter only for the Watch Page Fallback.
- Videos that can't be embedded (owner disabled it, or age-restricted) fall back to the full watch page automatically. That path costs more memory.
- Tubiqo's own local page joins the Allowed Domains. It must be locked down like any other page: no access to Tubiqo Data, and only the embed iframe allowed.
- Resume Position, Watch History and the Playback Policy hook into the Lean Player through YouTube's embed player API.
- Not yet verified: live streams, long viewing sessions, and how often YouTube shows ads in embeds.
