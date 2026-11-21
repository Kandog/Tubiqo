# Signed, data-only Rule Pack, updated separately from the app

Trim Rule definitions ship as a Rule Pack that Tubiqo can update without a new app release, because YouTube page changes break rules far more often than the app changes. To keep this update path from becoming a way to inject code, a Rule Pack is **data only** (CSS selectors to hide and URL patterns to block, never JavaScript). It must also carry a valid signature from the maintainer's private key, checked against a public key built into the app. Tubiqo fetches updates only when the user clicks "Check for updates" (from GitHub releases), and keeps the previous pack so it can roll back.

## Considered Options

- **Rules only change with app releases**: simplest, but every YouTube change would force a full reinstall for everyone.
- **Unsigned, locally editable rules file**: flexible, but anyone who can edit the file can change what Tubiqo does on YouTube pages.
- **Allowing scripts in the Rule Pack**: more powerful, but a compromised pack could then run arbitrary code on YouTube pages. Behaviours that need script (disabling hover previews, excluding AV1, autoplay-next) stay in the signed app instead.

## Consequences

- The maintainer must keep the signing key private; losing it means shipping a new app release with a new public key.
- Some fixes can't be delivered through a Rule Pack because they need script, so they need an app release.
- No automatic background checks: users may run outdated rules until they click "Check for updates".
