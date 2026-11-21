---
status: accepted, amended by ADR 0004 (playback moved to the Lean Player)
---

# Browse YouTube's own pages, slimmed by Trim Rules

Users browse YouTube's real pages (home, search, channels, watch) inside Tubiqo, and Tubiqo makes them lighter with user-toggleable Trim Rules plus a Playback Policy that excludes AV1. We chose this over a native start screen because the user wants YouTube's familiar navigation and discovery, and accepts the ongoing cost of keeping Trim Rules working as YouTube changes its pages.

## Considered Options

- **Native start screen, loading YouTube only for playback** (search, pasted URLs and Watch History in Tubiqo's own UI, with the watch or embed page as the only YouTube page loaded): lightest option, but needs scraping YouTube search or a Data API key with a daily quota, and loses YouTube's own browsing.
- **Hybrid** (search and channel pages allowed, home feed blocked): rejected in favour of keeping the full YouTube experience.

## Consequences

- Resource use depends on Trim Rules staying effective. When YouTube changes its pages, rules break, which is why ADR 0003 makes them updatable without an app release.
- Ad and tracker blocking puts Tubiqo in a cat-and-mouse game with YouTube's anti-ad-block measures.
- The main CPU win comes from excluding AV1, which the target GPU (Intel UHD 630) can't decode in hardware. That rule lives in the app, not the Rule Pack.
