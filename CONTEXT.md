# h2o

A lightweight, security-focused desktop browser whose main job is watching YouTube videos with as little CPU and memory as possible.

## Language

**View-only**:
The browser never signs in to a Google account and never sends user actions back to YouTube (no likes, comments, subscriptions or uploads).
_Avoid_: Read-only, guest mode

**Allowed Domains**:
The fixed set of YouTube-owned domains h2o is willing to load, plus h2o's own local page that hosts the Lean Player. Any other link is handed to the user's default browser instead of opening in h2o.
_Avoid_: Whitelist, permitted sites

**Single Player**:
h2o's one-window, one-video model: there are no tabs, and opening a new video replaces the current one.
_Avoid_: Tab, session

**Browse Pages**:
YouTube's own pages where the user finds videos: home, search, channels and playlists. They are shown as YouTube serves them, slimmed down by Trim Rules.
_Avoid_: Feed, YouTube site

**Lean Player**:
How h2o plays a video by default: YouTube's embeddable player shown on its own, without the watch page around it (no description, comments or recommendations). Opening a video from the Browse Pages or Watch History takes the user to the Lean Player, and the Browse Pages are unloaded while it plays.
_Avoid_: Embed, mini player, watch page

**Watch Page Fallback**:
Playing a video on YouTube's full watch page instead of the Lean Player, used when the video can't be embedded (the owner disabled embedding, or it's age-restricted). Trim Rules apply to it.
_Avoid_: Full mode, legacy player

**Trim Rule**:
A single, user-toggleable rule that hides or blocks one heavy part of YouTube (ads and trackers, hover previews, Shorts, comments, live chat, autoplay-next, endless home feed). Each has a default state, and the user can switch it on or off.
_Avoid_: Filter, ad-block rule, tweak

**Rule Pack**:
The signed, data-only bundle that carries the definitions of the Trim Rules (what to hide and what to block, never code). It is updated separately from the app, only when the user asks h2o to check for updates, and only accepted if its signature is valid.
_Avoid_: Filter list, rules file, config

**Playback Policy**:
The user's rules for which video formats h2o accepts from YouTube: which codecs are allowed (AV1 is excluded so the GPU can decode) and the **Quality Cap**, the highest resolution h2o will play (default 1080p).
_Avoid_: Video settings, quality preset

**Site Data**:
Everything YouTube itself stores inside h2o (cookies, visitor ID, local storage, cache). It is wiped every time h2o closes.
_Avoid_: Browser data, cache

**h2o Data**:
Everything h2o owns and keeps across launches: Watch History, Trim Rule states, Playback Policy and h2o's own preferences such as volume. It is never shared with YouTube.
_Avoid_: Profile, user data

**Watch History**:
The record, kept only on the user's own machine, of the videos the user has watched in h2o. It holds one **Watch Entry** per video. YouTube's account-side history is not part of it.
_Avoid_: YouTube history, browsing history

**Watch Entry**:
The single record of one video in Watch History: what the video is, when it was last watched, and its **Resume Position**. Re-watching updates the existing entry instead of adding a new one. A video becomes a Watch Entry only after about 10 seconds of playback.
_Avoid_: History item, view, record

**Resume Position**:
How far into a video the user got last time. Reopening the video from Watch History continues from there.
_Avoid_: Timestamp, progress
