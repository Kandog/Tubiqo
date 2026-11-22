# Sign in inside Tubiqo, and keep Site Data between launches

Supersedes the "view-only" rule. YouTube's Sign in button used to leave Tubiqo, because `accounts.google.com` wasn't an Allowed Domain, so the sign-in opened in the user's default browser and never reached Tubiqo. Users want to sign in, and they want it to stay inside the app.

`accounts.google.com` (that exact host) is now an Allowed Domain, and `www.google.com/recaptcha/*` is allowed as a frame only, for the challenge Google sometimes shows. The rest of google.com still opens outside after confirmation. Sign-in returns through `accounts.youtube.com`, which was already allowed. Google also sets the session cookie on the user's country domain (for example `accounts.google.ca/accounts/SetSID`), so `accounts.google.<country>` is allowed for its `/accounts/` pages only.

Site Data now lives in one profile folder, `site\profile`, kept between launches so the sign-in survives a restart. **Settings → Sign out and clear site data** clears it at once. Password saving and autofill stay off.

## Considered options

- **Sign in, but wipe on close as before**: keeps the "nothing remembered" model, but the user would have to sign in again on every launch.
- **Sign in through the system browser (OAuth)**: that's what the user doesn't want, and YouTube's pages need Google's own cookies anyway, which an OAuth token doesn't give them.

## Consequences

- YouTube's visitor ID and other cookies now persist between launches, even when the user isn't signed in.
- Google may refuse sign-in in embedded browsers ("This browser or app may not be secure"). Tubiqo uses WebView2's standard Edge user agent and doesn't work around such a refusal.
- The Lean Player embeds from `youtube-nocookie.com`, which doesn't use the signed-in session. Signed-in features (subscriptions, age-restricted videos via the Watch Page Fallback) work on YouTube's own pages.
