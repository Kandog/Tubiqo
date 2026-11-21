# Tubiqo implementation plan

Builds the design in [CONTEXT.md](../CONTEXT.md) and [ADRs 0001–0004](adr/). Each milestone is a vertical slice that ends in something you can run, and the performance targets are re-measured as we go, not only at the end.

**Targets (on the i3-8100 / UHD 630 PC):** under 10% CPU and under 400 MB while playing 1080p; about 0% CPU and under 250 MB when minimized and not playing. The prototype measured about 250 MB and under 2% CPU in the Lean Player.

## Solution layout

```
Tubiqo.sln
src/Tubiqo.Core/          # no WPF, no WebView2: domain rules, fully unit-tested
  Navigation/          # URL classification: Allowed Domains, video IDs, Lean Player vs Watch Page Fallback
  History/             # Watch History, Watch Entry rules, Resume Position, auto-delete
  Playback/            # Playback Policy, Quality Cap
  Rules/               # Trim Rules, Rule Pack format and signature verification
  Storage/             # SQLite for Tubiqo Data
src/Tubiqo.App/           # WPF (.NET 10, Fluent theme), WebView2 host, injected scripts, UI
  Player/player.html   # the Lean Player's local page, embedded as a resource
  Scripts/             # av1.js, probe/bridge.js, trim-*.js
tests/Tubiqo.Core.Tests/  # xUnit, written test-first
tools/measure.ps1      # promoted from the prototype: CPU/memory gate
tools/sign-rulepack/   # maintainer CLI: signs Rule Packs and releases
installer/             # Inno Setup script
```

Packages: `Microsoft.Web.WebView2`, `Microsoft.Data.Sqlite`, xUnit. Signing uses .NET's built-in ECDSA P-256, so no third-party crypto library is needed.

## Milestones

### M0: Locked-down shell (done 2026-11-21)
- WPF window with the Fluent theme (follows Windows light/dark), slim toolbar placeholder.
- Startup check: WebView2 Evergreen present and at least the minimum version; otherwise show a dialog linking to Microsoft's installer and exit.
- **Site Data:** a new profile folder under `%LOCALAPPDATA%\Tubiqo\site\` each launch. Wipe it on exit, and also on startup (to clean up after a crash).
- Hardening from the design:
  - deny every permission request without prompting
  - cancel all downloads
  - turn off autofill, password saving, DevTools (in Release builds), the browser accelerator keys and the default context menu
  - block plain HTTP
- **Navigation guard** (`Tubiqo.Core.Navigation`): anything outside the Allowed Domains is cancelled. Tubiqo shows a confirm dialog with the URL, then opens it in the default browser. Pop-ups go through the same guard.
- **Done when:** youtube.com loads, external links show the confirm dialog, the profile folder is gone after exit, and the guard's unit tests pass.

### M1: Lean Player (done 2026-11-21)

> Measured: about 275 MB steady while playing (passes the 300 MB gate), but the Browse Page's memory (about 530–630 MB) lingers for about 20 s after the switch before Chromium releases it. The player page drives the embed iframe with the raw postMessage protocol instead of loading YouTube's `iframe_api` script, so no YouTube code runs in the origin that can message the app. Fullscreen (from the player's button) was pulled forward from M6. The Watch Page Fallback has been tested only with a simulated error 150; it still needs a real non-embeddable video.

- Classify navigation: `/watch?v=`, `youtu.be/<id>`, `/shorts/<id>`, and Watch History clicks all go to the Lean Player. Everything else on the Allowed Domains is a Browse Page.
- Serve `player.html` from a virtual host (`player.tubiqo.test`). It holds only that one file, with a strict Content-Security-Policy that allows only the `youtube-nocookie.com` iframe and nothing else.
- Use the YouTube IFrame Player API (`enablejsapi=1`) to bridge player state (state, time, duration, title, errors) to C# through `postMessage` and `chrome.webview`.
- **Watch Page Fallback:** when the embed reports error 101, 150 or 153, load the normal watch page instead.
- **Back** from the Lean Player reloads the Browse Page you came from (see open question 1).
- **Done when:** clicking a video on a Browse Page plays it in the Lean Player, Back returns to the Browse Page, and a video with embedding disabled falls back to the watch page. Gate: `measure.ps1` stays under 300 MB while playing.

### M2: Playback Policy (done 2026-11-21)

> The Quality Cap is enforced the same way as the AV1 exclusion, by refusing formats above the cap when the page asks what the browser can play (short side compared, so portrait Shorts count correctly). YouTube's automatic quality still works below the cap, and no private player API is needed. Measured, maximized on the 1080p screen: the Lean Player chose VP9 1080p60 at 1.4% CPU and about 274 MB; the Watch Page Fallback chose VP9 720p at 2.3% CPU and about 407 MB. With the cap set to 720 (via a Debug-only environment variable), both stayed at 720p. Until M4 adds settings, the defaults (no AV1, 1080p) always apply.

- Inject `av1.js` into every frame so AV1 is excluded and VP9 or H.264 is chosen.
- Apply the Quality Cap through the player API (`setPlaybackQualityRange`). It is a per-user setting, defaulting to 1080p.
- **Done when:** the probe shows `vp09` (or `avc1`) at no more than the cap, in both the Lean Player and the Watch Page Fallback. Gate: under 10% CPU at 1080p.

### M3: Watch History (done 2026-11-21)

> Built as planned: SQLite `tubiqo.db` (Watch History plus the pause and retention settings), and a side panel beside the page (WPF can't draw over WebView2). The panel shows text only, no thumbnails, so it uses no extra memory or network and doesn't tell YouTube what you've watched. Finishing a video resets its Resume Position, and a video nearly finished (within 10 s of the end) reopens from the start. Live-checked: recording after 10 s, the panel, resuming at the saved time, pause recording (including after a restart), search, and ✕ delete. Delete range, clear all and retention are covered by unit tests only; they go through confirm dialogs. **Known gap:** videos played on the Watch Page Fallback aren't recorded, because only the Lean Player page reports playback to the app.

- SQLite database at `%LOCALAPPDATA%\Tubiqo\tubiqo.db`, readable only by your user account.
- Watch Entry rules, written test-first in Core:
  - an entry is created after about 10 s of playback
  - there is one entry per video, and re-watching updates it
  - nothing is recorded while recording is paused
  - auto-delete removes entries older than N months
- Resume Position is saved every 5 s while playing, plus on pause and close. Reopening an entry starts from it (the `start` parameter or `seekTo`).
- Side panel: search by title or channel, delete one, delete by date range, clear all, the pause-recording toggle.
- **Done when:** watching for 10 s or more creates one entry, reopening it resumes, and everything in the panel works.

### M4: Trim Rules and Rule Pack (done 2026-11-21)

> The built-in Rule Pack (`src/Tubiqo.App/Rules/default-rulepack.json`) is compiled into the app and checked by the same validator as downloaded packs (unit test). The validator refuses anything beyond plain selectors and specific URL patterns, including patterns that would cover video segments, thumbnails, the player or YouTube as a whole. Hide selectors go in a constructed stylesheet, one CSS rule per selector, so the page's CSP can't refuse it and one bad selector can't disable the rest. The Settings panel holds the Quality Cap and one toggle per rule. Page changes apply from the next page, with a "Reload page" button; blocking changes apply at once.
>
> Live-checked: ad slots and Shorts actually hidden on search and watch pages, `/pagead` 403 while normal requests pass, hover previews blocked, comments hidden behind a working "Show comments" button, autoplay-next off, and playback steady in both the Lean Player and the Watch Page Fallback (no stalls). Toggles and the cap persist across restarts. Memory: home about 380 MB; search page settles at about 400–470 MB (average 447–484 MB over 45 s, passing the 500 MB gate) but peaks at 530–590 MB for the first seconds after loading. A V8 `--optimize-for-size` flag made no difference and was not adopted.

- Rule Pack format: versioned JSON with `{ id, title, default, hide: [css selectors], block: [url patterns] }`, data only. A default pack is embedded in the app.
- Apply `hide` as one injected `<style>` on Browse Pages and the Watch Page Fallback. Apply `block` through WebView2 request filters registered **per pattern**, never `*`, so video segments never make a round trip through the app.
- Rules that need script (no hover previews, autoplay-next off, limited home feed) live in app code.
- Settings page: one toggle per Trim Rule, with its state saved in Tubiqo Data.
- **Done when:** each toggle visibly changes the Browse Pages and the defaults match the design. Gate: home and search pages stay under 500 MB.

### M5: Updates and installer (done 2026-11-21)

> Built as planned, with one design refinement: each GitHub release carries a single signed manifest, `tubiqo-release.json` plus `.sig`. It lists each file (installer and/or Rule Pack) with its version, size and SHA-256, so one signature covers everything, and an older but validly signed release can never downgrade Tubiqo (only strictly newer versions are offered). Downloaded Rule Packs are re-verified every time they load; one edited on disk is ignored in favour of the built-in pack. The newer of the built-in and downloaded packs is used. The updater only talks https to GitHub's own hosts, checks every redirect hop, and sends a generic User-Agent and no cookies. The maintainer key lives in `%APPDATA%\Tubiqo-signing` (owner-only permissions, never in the repo); `*.pem` is git-ignored as a safety net. The app ships self-contained (no .NET install needed; 45.6 MB installer, 144 MB installed), so .NET security fixes arrive with Tubiqo releases. The SmartScreen "unknown publisher" warning is accepted.
>
> Live-checked against a fake GitHub on localhost: no release yet, a release signed by another key (ignored), a tampered Rule Pack (rejected), a valid Rule Pack (applied, survives restart), up to date, rollback, a tampered installer (rejected, download deleted), and a real installer update (Tubiqo closed, installed per-user and relaunched). The silent uninstaller then removed everything. Not yet done: a real GitHub release, which is your call to publish.
>
> **Cutting a release:** bump `<Version>` in `src/Tubiqo.App/Tubiqo.App.csproj` (and/or the Rule Pack `version`, like `2026.11.22.1`), then run `.\tools\build-release.ps1 -Sign` (add `-RulePack <file>` to ship a pack) and attach every file in `artifacts\release` to a new GitHub release of Kandog/Tubiqo.

- `tools/sign-rulepack` signs Rule Packs and release installers with the maintainer's ECDSA P-256 private key. The matching public key is compiled into the app.
- **Check for updates** (manual only) reads the latest GitHub release of [Kandog/Tubiqo](https://github.com/Kandog/Tubiqo):
  - a newer Rule Pack is downloaded, its signature checked, then installed; the previous one is kept for rollback
  - a newer app version means downloading the installer and its signature, checking it, then running the installer
- Inno Setup installer installs per user, with no admin rights needed.
- **Done when:** a tampered pack or installer is rejected, a valid one is applied, and rollback works.

### M6: Window behaviour and polish
- Toolbar: Back, Forward, Reload, Home, Watch History, Settings.
- Ctrl+L opens "paste a YouTube link", which accepts only Allowed Domains.
- F11 and the player's fullscreen button both hide the toolbar.
- Remember window placement and volume in Tubiqo Data.
- Keep playing when minimized, with an optional "pause when minimized" setting. When minimized and not playing, call `TrySuspendAsync` and `MemoryUsageTargetLevel = Low`.
- **Done when:** the targets table passes, including minimized and not playing.

### M7: Verification
- Run `measure.ps1` across the full matrix: Lean Player 1080p, Watch Page Fallback, Browse Pages, minimized, plus a 1-hour session to check for memory growth.
- Review security against the hardening list in the design.
- Manual checks: a live stream, an age-restricted video, a video with embedding disabled, and a 2-hour playlist.

## Testing approach

- `Tubiqo.Core` is built test-first (red, green, refactor). It covers URL classification, Allowed Domains, Watch Entry rules, auto-delete, Quality Cap mapping, Rule Pack parsing and signature verification.
- App and WebView2 behaviour is checked with manual run scripts plus the `measure.ps1` gates at each milestone.

## Open questions

1. **Back from the Lean Player:** reloading the Browse Page is lightest, but it loses the scroll position. The alternative is keeping it in a second, suspended WebView, which costs memory. Plan: reload first, then measure the alternative in M1 if it feels bad.
2. ~~**SmartScreen:** accept the "unknown publisher" warning without a paid code-signing certificate?~~ Accepted 2026-11-21.
3. **Ads in embeds:** none appeared in testing, but YouTube may show them. Network blocking in the Lean Player needs checking, given the stall seen on the full watch page.
4. **Live streams** in the Lean Player are untested, and live chat isn't available there.
5. ~~**GitHub repo:** which account or repo name will releases use?~~ Answered 2026-11-21: [Kandog/Tubiqo](https://github.com/Kandog/Tubiqo); the app was renamed from h2o to Tubiqo to match.

## Risks

- YouTube changes its pages or the embed player. Rule Packs absorb page changes, but embed or API changes need an app release.
- Blocking ads breaks YouTube's Terms of Service. That's acceptable for sharing with friends and family; a public release would need rethinking.
- Losing the signing key means shipping a new public key in a new app release.
