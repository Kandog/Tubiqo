# Tubiqo

A small, locked-down Windows app for watching YouTube. It uses much less CPU and memory than a full browser: about **300 MB and under 2% CPU at 1080p**, and nearly nothing when minimized and paused.

You can sign in to your Google account right inside Tubiqo; it never sends you to your default browser. Tubiqo never saves or fills in passwords.

## Install

1. Download `Tubiqo-Setup-<version>.exe` from the [latest release](https://github.com/Kandog/Tubiqo/releases/latest).
2. Run it. It installs for your Windows user only, so it needs no admin rights.
3. Windows SmartScreen will warn about an unknown publisher, because the installer isn't code-signed. Choose **More info → Run anyway**.

Requirements: Windows 10 or 11 (x64) with the Microsoft Edge WebView2 Runtime, which Windows normally includes. If it's missing, Tubiqo offers to open Microsoft's download page.

To update, open **Settings → Check for updates**. Tubiqo never checks on its own, and it only installs releases signed with the maintainer's key.

## Features

- **Lean Player:** videos play in YouTube's embeddable player on its own, without the watch page around it. AV1 is avoided and quality is capped at 1080p (you can change the cap in Settings) to keep decoding cheap. Videos keep playing when the window is minimized, or can pause instead.
- **Browse Pages:** YouTube's home, search and channel pages, slimmed by **Trim Rules**. Ads, Shorts, hover previews, comments, live chat, related videos, autoplay-next, the home feed and links to Google Account pages can each be switched on or off.
- **Watch history:** one entry per video with resume position, search, delete, and auto-delete after a chosen time. It's stored only on your PC.
- **Sign-in inside the app:** YouTube's Sign in button opens Google's sign-in page in Tubiqo, and you stay signed in after closing it. **Settings → Sign out and clear site data** signs you out and deletes everything YouTube stored. Other non-YouTube links open in your default browser only after you confirm.
- **Hardened:**
  - Every site permission is denied, and downloads are blocked.
  - DevTools, autofill and password saving are off.
  - Plain HTTP is blocked.
  - WebView2 override variables are ignored in release builds.

### Keyboard shortcuts

| Keys | Action |
|---|---|
| Ctrl+L | Open a YouTube link or video ID |
| Ctrl+H | Show or hide watch history |
| F11 / Esc | Full screen / leave full screen |
| Alt+← / Alt+→ | Back / forward |
| F5 or Ctrl+R | Reload |
| Alt+Home | YouTube home |

### Known limits

- Age-restricted videos need you to be signed in, and play on YouTube's full watch page.
- Playlists play only their first video.
- There's no live chat in the Lean Player.
- Videos that can't be embedded open on YouTube's full watch page (the "Watch Page Fallback"), which uses about 490 MB.
- Windows only.

## Building from source

You need the .NET 10 SDK. Inno Setup 6 is only needed for the installer.

```powershell
dotnet build Tubiqo.sln
dotnet test tests\Tubiqo.Core.Tests
dotnet run --project src\Tubiqo.App
```

| Folder | Contents |
|---|---|
| `src/Tubiqo.Core` | Domain rules, with no WPF or WebView2: navigation guard, Lean Player routing, Playback Policy, watch history, Trim Rules and Rule Packs, signed updates, SQLite storage. Built test-first. |
| `src/Tubiqo.App` | WPF app (Fluent theme, follows Windows light/dark), the WebView2 host, the Lean Player page (`Player/`) and injected scripts (`Scripts/`). |
| `tests/Tubiqo.Core.Tests` | xUnit tests for Core. |
| `tools/Tubiqo.Sign` | Maintainer tool: creates the signing key, and signs and verifies releases. |
| `tools/build-release.ps1` | Publishes the app, builds the installer and signs the release. |
| `tools/measure.ps1` | Measures CPU and memory per scenario against the targets. |
| `installer/` | Inno Setup script for the per-user installer. |

### Cutting a release (maintainer)

1. Bump `<Version>` in `src/Tubiqo.App/Tubiqo.App.csproj`.
2. Run `.\tools\build-release.ps1 -Sign`. It needs the private signing key at `%APPDATA%\Tubiqo-signing`, which is never committed.
3. Attach every file in `artifacts\release` to a new GitHub release.

To fix Trim Rules without a new app version, add `-RulePack <file>` with a higher pack version. Tubiqo offers only what is strictly newer, so installed copies pick up just the new Rule Pack.

## Design documents

- [CONTEXT.md](CONTEXT.md): the domain language (Lean Player, Trim Rule, Rule Pack, Site Data, Watch Entry…).
- [docs/adr](docs/adr): architecture decisions. They cover WebView2 Evergreen on Windows only, browsing YouTube's own pages with Trim Rules, signed data-only Rule Packs, playing in the Lean Player, and signing in inside Tubiqo.
- [docs/PLAN.md](docs/PLAN.md): milestones, measurements and open questions.

## License

[MIT](LICENSE) © 2026 Tubiqo contributors.

## Note

Tubiqo hides ads on YouTube's pages, which goes against YouTube's Terms of Service. It's made for personal use with friends and family, not as a public product. It isn't affiliated with YouTube or Google.
