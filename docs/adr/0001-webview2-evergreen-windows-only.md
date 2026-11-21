# WebView2 Evergreen, Windows only

h2o is built in C# (.NET 10, WPF) on Microsoft's WebView2 runtime in Evergreen mode, and runs on Windows only. Security was the deciding factor: Microsoft patches Evergreen WebView2 automatically alongside Edge, so h2o never falls behind on Chromium security fixes. Bundling our own engine would make every Chrome CVE our release emergency. As a bonus, the app stays a few MB because the engine binaries are shared with Windows.

## Considered Options

- **CEF (Chromium Embedded Framework)**: cross-platform and gives finer control of Chromium flags, but ships 150–250 MB of Chromium that we would have to rebuild for every security fix. Rejected because an un-patched bundled Chromium is less secure than Chrome.
- **Qt WebEngine**: same problems as CEF, heavier, and patches arrive more slowly.
- **A new engine**: not feasible, and would be less secure, not more.
- **Rust or C++ shell**: would save about 30–60 MB over .NET, but WebView2 dominates memory use (150–350 MB), so C#'s memory safety and development speed won.

## Consequences

- No macOS or Linux version without changing engines.
- h2o refuses to start if the WebView2 runtime is missing or older than the minimum supported version.
- Some Chromium tuning flags are unavailable; resource savings must come from Trim Rules and the Playback Policy instead.
