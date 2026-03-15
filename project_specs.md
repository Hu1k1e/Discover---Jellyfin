# Project Specifications: Native Upcoming Movies & Recommendations Plugin

---

> [!IMPORTANT]
> **Read the Deployment Process section below before pushing any release.**

---

# DEPLOYMENT PROCESS — How to Release a New Version

This is the exact procedure to push a change and have it appear in Jellyfin's plugin catalog. **Follow every step.** Past failures came from skipping steps or doing them out of order.

## Step 1 — Make + Commit Your Changes

```powershell
git add <changed files>
git commit -m "fix: description of what changed"
```

## Step 2 — Push the Main Branch

```powershell
git push origin main
```

> If the push is rejected ("non-fast-forward"), pull and rebase first:
> ```powershell
> git pull --rebase origin main
> git push origin main
> ```
> PowerShell may show exit code 1 even on success — check the output for `main -> main` to confirm.

## Step 3 — Create + Push a Version Tag

The GitHub Actions workflow **only triggers on `v*` tags**, NOT on regular commits.

```powershell
git tag v1.0.XX    # replace XX with next version number (e.g. v1.0.48)
git push origin v1.0.XX
```

Confirm success: output should include `* [new tag] v1.0.XX -> v1.0.XX`

## Step 4 — Wait for GitHub Actions (~2 minutes)

The workflow (`.github/workflows/build-release.yml`) will automatically:
1. Build the plugin `.dll` 
2. Create a ZIP: `jellyfin-plugin-upcoming-movies_v1.0.XX.zip`
3. Compute the MD5 checksum
4. **Prepend a new version entry to `manifest.json`** and commit it to `main`
5. Create a GitHub Release with the ZIP as an asset

## Step 5 — Pull the Actions Bot Commit

After the workflow completes, the Actions bot commits an updated `manifest.json` to `main`. Pull it so local is in sync:

```powershell
git pull origin main
```

Verify `manifest.json` starts with `"version": "1.0.XX.0"` — that's the confirmation it worked.

## Step 6 — Verify the Release

Check: `https://api.github.com/repos/Hu1k1e/Discover---Jellyfin/releases?per_page=1`  
Should show the new release with `"tag_name": "v1.0.XX"` and a non-empty `assets` array.

---

## Troubleshooting: Manifest Not Updated (v not appearing in Jellyfin)

**Symptom:** GitHub release exists (ZIP uploaded) but Jellyfin catalog still shows old version.  
**Cause:** The Actions bot tried to push the manifest update but failed (usually due to a rebase conflict on `main`).  
**Fix:** Manually add the entry to `manifest.json`:

```json
{
  "version": "1.0.XX.0",
  "changelog": "Release 1.0.XX.0. See GitHub for details.",
  "targetAbi": "10.11.0.0",
  "sourceUrl": "https://github.com/Hu1k1e/Discover---Jellyfin/releases/download/v1.0.XX/jellyfin-plugin-upcoming-movies_v1.0.XX.zip",
  "checksum": "<MD5 from the GitHub release body>",
  "timestamp": "<timestamp from the GitHub release>"
}
```

Get the ZIP MD5 from the GitHub release description (it's printed as `**ZIP MD5:** `...``).  
Prepend this object at position `[0].versions[0]` (the very first entry in the array), then:

```powershell
git add manifest.json
git commit -m "chore: manually update manifest for v1.0.XX"
git push origin main
```

---

## Version Numbering

| Convention | Example |
|---|---|
| Git tag | `v1.0.47` |
| manifest.json `"version"` | `"1.0.47.0"` (always append `.0`) |
| ZIP filename | `jellyfin-plugin-upcoming-movies_v1.0.47.zip` |

Always increment the **third** number (patch): `v1.0.46` → `v1.0.47`.  
Never reuse a tag — git will reject it and a duplicate will cause manifest corruption.

---



This project is a custom Jellyfin Plugin introducing a native "Upcoming Movies & Recommendations" Discover page into the Jellyfin Web UI.

**Two sections displayed:**
1. **Upcoming Movies** — TMDB-sourced films strictly in the future (up to 1 year). Request-only (Jellyseerr).
2. **Recommended For You** — Intelligent multi-source recommendations personalised by watch history + favourites. Available movies show a "Play" button linking to Jellyfin; missing movies show a "Request" button.

User's favourites/watchlist feeds the recommendation engine as input signals, not displayed directly.

---

# 2. Section Behaviour

| Section | Source | Cards show | Click behaviour |
|---------|--------|------------|-----------------|
| Upcoming Movies | TMDB `/discover/movie` (next 1 year) | Request button only (purple/green custom) | Opens Jellyseerr quality-profile modal |
| Recommended | Per-movie recs + genre discover + trending fallback | Play or Request button | Play opens Jellyfin detail, Request opens modal |

---

# 3. Card Design (Phase 8)

- **Hover**: dark overlay (rgba 0,0,0,0.52) + centred play circle (native Jellyfin style)
- **Upcoming cards**: no play circle click, no stream button — only Request
- **Action bar** (slides in at bottom on hover):
  - **Request** — Jellyseerr purple `#7B5EA7` / `#b39ddb` text
  - **Stream** — H-TV green `#00C853` / `#69f0ae` text
- **Navigation**: drag-to-scroll (mouse + touch) + hoverable `<` `>` arrow buttons

---

# 4. Jellyseerr Request Modal (Phase 8)

On "Request" click:
1. Fetches `/UpcomingMovies/jellyseerr/radarr` → Jellyseerr `/api/v1/settings/radarr`
2. Shows modal with:
   - Movie title + backdrop header (dark gradient overlay, blue heading)
   - Destination Server dropdown (Radarr instances)
   - Quality Profile dropdown (per-server profiles, default pre-selected)
   - Root Folder dropdown (per-server paths, default pre-selected)
   - Cancel + green Request buttons
3. Submits to `/UpcomingMovies/jellyseerr/request` with `{ tmdbId, mediaType, serverId, profileId, rootFolder }`

---

# 5. Recommendation Algorithm (Phase 8)

**Client-side signal gathering:**
- Watched movies (last 50): contributes 1× genre weight each + TMDB ID as seed
- Favourites (IsFavorite=true): contributes 2× genre weight each + TMDB ID as seed
- Top 5 genres by weight sent to backend
- Top 8 TMDB seed IDs sent to backend

**Backend multi-source pipeline (`/UpcomingMovies/tmdb/recommendations`):**
1. For each seed TMDB ID (up to 5): call TMDB `/movie/{id}/recommendations`
2. Call TMDB `/discover/movie?with_genres=...&sort_by=vote_average.desc` as supplement
3. Fallback: `/trending/movie/week` if no results
4. Deduplicate by TMDB ID; exclude watched/favourited movies
5. Sort by `vote_average` desc; return top 30

---

# 6. Authentication Architecture

| Request | Auth Method |
|---------|-------------|
| Jellyfin APIs (client) | `X-Emby-Token: {accessToken}` |
| Plugin backend endpoints | `X-Emby-Authorization: MediaBrowser Token="..."` |
| Backend controller attribute | `[Authorize]` — plain (named policy crashes Jellyfin 10.11) |

---

# 7. Manifest & Release Pipeline

**Catalog URL:**
```
https://raw.githubusercontent.com/Hu1k1e/Discover---Jellyfin/main/manifest.json
```

**Release tag → GitHub Actions → ZIP + MD5 → manifest.json prepended → GitHub Release**

> **Race condition warning**: if you push local commits while GA is updating manifest, pull first (`git fetch origin && git rebase origin/main`). GA workflow now also rebases before committing manifest.

---

# 8. Implementation History

## Phase 1–5 — Architecture, Scaffolding, CI (2026-03-14) ✅
- Analysed KefinTweaks, designed C# proxy + dual JS injection architecture
- Built PluginConfiguration.cs, configPage.html, TmdbController, JellyseerrController
- Set up GitHub Actions CI with MD5 checksum + manifest prepend

## Phase 6 — Native Script Injection via File Transformation Plugin (2026-03-14) ✅
- Root cause of blank tab: `discoverPage.js` never loaded (no JS Injector)
- Replicated Custom Tabs injection: `StartupService.cs` + `TransformationPatches.cs` + `inject.js`
- First working build: v1.0.8

## Phase 7 — Live Debug & Critical Fixes (2026-03-14) ✅

### Root Causes Found
| Bug | Root Cause | Fix (version) |
|-----|-----------|--------------|
| HTTP 500 on all endpoints | `[Authorize(Policy = "DefaultAuthorization")]` throws before code runs | Changed to plain `[Authorize]` (v1.0.11) |
| `jq` overwrites manifest | `manifest.json[0]` overwritten instead of prepend | Changed to prepend new entry (v1.0.10) |
| Manifest race condition | Local push overwrote GA manifest commit | GA now rebases before committing (v1.0.11) |
| TMDB key "not configured" | 500 error masking key (IsFavorite → IsLiked wrong filter) | Fixed in v1.0.11 |
| Brave browser blank page | Service Worker persistent cache | User action: `brave://serviceworker-internals/` → Unregister |

## Phase 8 — UI Refinements & Recommendations (2026-03-14) ✅

### Changes (v1.0.12)
| Feature | Implementation |
|---------|---------------|
| Row navigation | Drag-to-scroll (mouse + touch) + hover arrow buttons `<` `>` |
| Card colours | Custom UI colours mimicking Jellyfin's standard theme |
| Jellyseerr modal | Modal UI structure designed |
| Intelligent recommendations | Multi-source: per-movie seeds + genre discover + trending fallback, genre weights 2× for favourites |
| Watchlist section removed | Data used as recommendation signal only |
| JellyseerrController fix | Plain `[Authorize]`, IHttpClientFactory |

## Phase 9 — 1-Year Upcoming, Modal Fix, Availability Engine (2026-03-14) ✅

| Bug/Feature | Implementation |
|-------------|----------------|
| 1-Year Upcoming Window | Switched TmdbController from `/movie/upcoming` to `/discover/movie` searching within the next 365 days. |
| Modal Fix | Changed Jellyseerr API path from `/api/v1/radarr` to `/api/v1/settings/radarr` to properly retrieve configuration. |
| Mixed Availability Engine | `discoverPage.js` now maps user's library (`/Items?IncludeItemTypes=Movie`) to TMDB IDs. Renders `Play` if available, `Request` if missing using precise `.jellyseerr-request-button` CSS overrides. |

## Phase 9b — GitHub Actions & Modal Data Fix (2026-03-14) ✅

| Bug/Feature | Implementation |
|-------------|----------------|
| GitHub Actions Race Condition | Fixed `.github/workflows/build-release.yml` rebase conflicts by stashing the modified manifest file during `git pull origin main`. |
| Missing Modal Options | Jellyseerr's base settings API does not expose profiles or root folders. Updated `JellyseerrController.cs` to dynamically loop and fetch `/:id/profiles` for each Radarr instance and inject `activeDirectory` into the response for `discoverPage.js`. |
| C# Compilation Error | Added missing `using System.Collections.Generic;` in `JellyseerrController.cs` to fix GitHub Actions build failure. |

## Phase 10 — Comprehensive Data & Algorithm Upgrades (2026-03-14) ✅

| Bug/Feature | Implementation |
|-------------|----------------|
| Missing Root Folders | The previous `activeDirectory` fix only returned the default folder. Updated `JellyseerrController.cs` to extract all root folders unconditionally by submitting a background `POST` request to the proxy `/api/v1/settings/radarr/test` endpoint on behalf of the user. |
| Empty Recommendations | Fixed `discoverPage.js` algorithm which was separating Top Genres using a comma (TMDB interpretation: AND) instead of a pipe `|` (TMDB interpretation: OR). This caused the algorithm to filter out 99% of movies instead of broadening the net based on Watch History. |
| Obscure Upcoming | Refined the TMDB Upcoming endpoint parameters in `TmdbController.cs` to enforce Hollywood blockbusters: added `with_original_language=en`, `region=US`, and `sort_by=popularity.desc`. |

## Phase 12 — UI Redesign, Infinite Grid, and Native Navigation (2026-03-14) ✅

| Bug/Feature | Implementation |
|-------------|----------------|
| Requested State Checkmark | Updated `discoverPage.js` Jellyseerr modal success handler to locate the corresponding card Request button on the DOM, change its HTML to `✓ Requested`, disable it, and turn the background grey. |
| Native Card Redesign | Completely redesigned `.discover-card`. Added a new `.dc-star-badge` inside the poster. Native Jellyfin items now perfectly match default design (no block buttons below, only a hover play circle). Unavailable items moved the "Request" and "Stream" buttons to cleanly sit below the title text instead of overlaying the poster. |
| Infinite Grid Scroll | Changed the Recommended section to native `display: grid`. Substantially updated `discoverPage.js` and `TmdbController.cs` to accept a `page` parameter. Implemented a "Discover More" button that fetches subsequent pages and seamlessly appends them to the grid. |
| Watched Filter | Modified the Jellyfin `Items` API query in `discoverPage.js` to retrieve `UserData`. The recommendation array is now strictly filtered so `item.UserData.Played === true` items are completely hidden from the user's Discover page. |
| Native Content Navigator | Completely bypassed the buggy "Custom Tabs" plugin injection. `discoverPage.js` now reads `NavPlacement` from settings and natively injects an `emby-button` into either the Sidebar or Header Tabs block directly via Javascript. This cleanly replaces the Jellyfin view without browser tracker/adblocker bugs breaking the Discover load. |

## Phase 13 — UI Refinement & Secondary Menus (2026-03-14) ✅

| Bug/Feature | Implementation |
|-------------|----------------|
| Transparent Glass Cards | `discover-card` was missing its `background` property. Added `rgba(255, 255, 255, 0.04)` and `border-radius: 8px` to ensure the list items render as slightly visible, rounded glass cards on dark backgrounds. |
| Button Colors | Fixed CSS overriding to explicitly set the Request button (`.btn-request`) to purple (`#7B5EA7`) at all times. Forced the Stream and Discover More buttons to Jellyfin green (`#00C853`). The action bar padding was tweaked to cleanly contain the buttons inside the card's width boundaries. |
| Heading Alignment | Removed the `1%` lateral padding from the wrapper grids and rows, shifting the elements to mathematically align 1:1 with the left side of the `<h2>` section headers above them. |
| Secondary Navbar Injector | Ripped out the basic 'Home' appending sidebar code. Injected the user's custom `MutationObserver` template. The script now aggressively hunts for `.customMenuOptions [data-name="watchlist"]` and dynamically appends **Calendar**, **Live Downloads**, and **Discover** directly below it perfectly styled. |

## Phase 14 — Uniform Glass Cards & Overview Modal (2026-03-14) ✅

| Bug/Feature | Implementation |
|-------------|----------------|
| Uniform Card Heights | Action bars are now `display: flex; flex-direction: row;`. Available Jellyfin movies now definitively inject a `btn-play` button into their action bar (matching the height footprint of the `btn-request` and `btn-stream` buttons). Every card now possesses a uniform vertical height regardless of its TMDB vs. Local availability state, creating a flush grid. |
| Glass Touch Buttons | By default, `.btn-request`, `.btn-stream`, and `.btn-play` execute `background: rgba(255, 255, 255, 0.08);` with a `backdrop-filter: blur(4px);`. Hovering the elements injects explicit user colors (Purple for Request, Green for Stream/Play) and a `translateY(-2px)` CSS animation for premium tactile feedback. |
| Dynamic Overview Modal | Added `showOverviewModal()` to `discoverPage.js`. Clicking on any unavailable TMDB Poster generates a fixed fullscreen cinematic modal containing the HD backdrop, vertical poster, h1 title, and the full TMDB Overview synopsis snippet pulled from the `/tmdb/recommendations` parsing stream. The exact Request and Stream buttons inhabit the modal directly. |
| Navigation Rollback | Scaled back the `injectNativeNavigation()` Sidebar code to exclusively inject the `discover-sidebar-tab` after `[data-name="watchlist"]`. Provided the user the multi-link JS snippet for manual configuration. |

## Phase 15 — Adaptive Grid, Jellyseerr Bulk Status, & UI Polish (2026-03-14) ✅

| Bug/Feature | Implementation |
|-------------|----------------|
| Header Alignment | Abandoned manual margins and implemented Jellyfin's native `.padded-left` and `.padded-right` classes on the injected sections and `.discover-row` / `.discover-grid` containers. This mathematically aligns the `<h2>` text edge directly with the left border of the first movie poster card! |
| Refresh Upcoming | Created a floating `<button is="emby-button" class="paper-icon-button-light">` with the Material `refresh` icon explicitly on the right side of the Upcoming Movies title line. Attaching an `onclick` listener to explicitly clear `[data-row="upcoming"]` and invoke `fetchUpcoming()`. |
| Adaptive TMDB Recommendations Buffer | Re-engineered the "Discover More" mechanics. The JS engine now dynamically calculates the total `columnCount` based on the user's explicit window size. When computing, it mathematically ensures exactly `targetCount = cols * 3` items exist in a global `_tmdbRecBuffer`. If the buffer runs short, the engine recursive-fetches `fetchRecommendations(page++)` until it accumulates 3 full rows perfectly (after filtering watched local movies). |
| Jellyseerr Pre-Load O(1) Display | Altered `JellyseerrController.cs` to expose `[HttpGet("requests")]`, proxying Jellyseerr's bulk `/api/v1/request?take=3000`. `discoverPage.js` hits this URL on load, caching the TMDB IDs into `window._jellyseerrRequests = new Set()`. `buildCard()` executes an O(1) hash check, immediately painting the `.btn-request.requested` Checkmark button natively on Grid generation! |
| Immersive Blueprint Blur | Wrapped the `.htv-modal-backdrop` and applied the exact `filter: blur(12px) brightness(50%) saturate(120%);` as specified in user's UI theme. Spliced the authentic Jellyseerr Indigo Purple `#667BC6` into the hover states! |

## Phase 16 — UI Polish & Grid Fixes (2026-03-14) ✅

| Bug/Feature | Implementation |
|-------------|----------------|
| Discover Removed from Header | Removed header-tab injection block from `injectNativeNavigation()`. Plugin no longer appends a Discover `<button>` to `.headerTabs` — user controls sidebar entry via their own manual JS inject. |
| Request Button Purple | Fixed `.btn-request:hover` / `.htv-modal-actions .btn-request:hover` from `#667BC6` (slate-blue) → `#7B5EA7` (genuine Jellyseerr brand purple). |
| Larger Close (×) Button | Increased `.htv-modal-close` from `40×40px / font-size 24px` → `52×52px / font-size 28px` for easier mobile tap targets. |
| Card/Title Alignment | Removed `padding: 0 2%` from `.discover-row-wrap`. The inner row's `padded-left` / `padded-right` provides correct inset — removing the wrapper padding aligns section headings with card edges. |
| Full Grid Rows | Changed `auto-fill` → `auto-fit` in `discover-grid`. `sliceCount` is now rounded down to the nearest multiple of `cols` for both initial load and Discover More, guaranteeing complete rows. |

---

## Phase 17 — Stream Modal, Logo Fix & Personalized Recommendations (2026-03-14) ✅

| Bug/Feature | Implementation |
|-------------|----------------|
| "Discover" text on H-TV logo | Removed 7 lines from `mountNativeDiscoverView()` that overwrote `.pageTitleWithDefaultLogo` / `.pageTitleWithLogo` with the text "Discover". Logo now displays as normal. |
| Stream popup modal | Converted all `<a class="btn-stream">` elements to `<button class="btn-stream" data-stream-url="...">`. Added `showStreamModal()` — glassmorphic overlay with poster, title, and a "Stream Now" link that opens in a new tab. Auto-dismisses when clicking outside or pressing ×. |
| Modal auto-close | Added `closeAnyOpenModal()` helper that removes all `.htv-modal-overlay` and `.dcm-backdrop` overlays before opening any new one. Called at start of `openRequestModal()` and `showStreamModal()`. Clicking stream inside the overview modal closes the overview first, then opens the stream modal. |
| Per-user Personalized Recommendations | Rewrote `fetchRecommendations()` in JS to build a full per-user signal profile from Jellyfin APIs: (a) Watch history — last 100 movies with `People` field; directors get 2× genre weight, actors get 1×; recency bonus (idx<20 = 3×, else 1×). (b) Favorites — 50 movies at 5× weight. Sends `tmdbIds` (8 seeds), `genreIds` (5), `directorIds` (3 top directors), `actorIds` (3 top actors) to backend. |
| Backend Recommendation Engine | `TmdbController.GetRecommendations` adds `directorIds`/`actorIds` params. New data sources: `/movie/{id}/similar` for top 3 seeds; `/discover/movie?with_people={directors\|actors}` for people-based discovery. Seeds expanded 5→8. Results expanded Take(30)→Take(40). |

## Phase 18 — Request Modal Fix & Requested State (2026-03-14) ✅

| Bug/Feature | Implementation |
|-------------|----------------|
| Request modal broken/behind cards | Root cause: CSS comment at line 269 was missing its opening `/*` — the line read `REQUEST MODAL...` followed by `*/` with no matching open. This caused the CSS parser to treat the `.dcm-backdrop`, `.dcm-box`, `.dcm-header`, `.dcm-footer`, and all sibling rules as invalid, so the modal rendered with no styling. Fixed by adding `/*` to open the comment block. |
| Request modal z-index | Bumped `.dcm-backdrop` `z-index` from `9998` → `10000` so it always renders above card `transform` stacking contexts that CSS creates for `.discover-card`. |
| Requested button state | `buildCard()` now checks `window._jellyseerrRequests.has(String(tmdbId))` before rendering the Request button. Already-requested movies render a disabled `✓ Requested` button with `opacity:0.65` and `pointer-events:none` on both recommendation cards and upcoming cards. |
| CSS: requested button disabled | Added global `.btn-request.requested` rule: `opacity:0.65 !important; cursor:default !important; pointer-events:none !important;` so the button is visually and functionally disabled everywhere. |

---

## Phase 19 — Performance, Requested State & Radarr Pre-cache (2026-03-15) ✅

| Bug/Feature | Implementation |
|-------------|----------------|
| **10s card loading lag** | `fetchRecommendations` no longer fetches `People` field during initial render. Was fetching 50–100 movies × 50+ cast members per film = massive payload. Removed `People` from `IncludeItemTypes` query. |
| **Background People enrichment** | `_enrichPeopleAsync()` runs 200ms after render in a `setTimeout`. Fetches only 30 movies with People field. Stores director/actor weights in `sessionStorage` via `_pGet()` / `_pSave()`. Used on next page load for better recommendations. |
| **SessionStorage profile cache** | `htv_uprofile` key in sessionStorage caches director/actor signal weights (`dW`, `aW`) across page loads. Signals immediately available with zero fetch cost on repeat visits. |
| **10s request modal lag** | Radarr instances now pre-cached via `_fetchRadarrCached()` (called 50ms after container init). Request modal opens instantly (no blocking fetch). |
| **Already-requested cards still show Request** | `fetchAndCacheJellyseerrRequests()` fetches `/UpcomingMovies/jellyseerr/requests` in background on page init (100ms delay). After load, DOM-refreshes all `.btn-request:not(.requested)` buttons matching the Set. Handles the backend's integer array format. |
| **Requested state not persisting** | After successful request submission, TMDB ID immediately added to `window._jellyseerrRequests` Set. Future card renders on same page session also show Requested state. |

# 10. Current Status

---

## Phase 20 — Restore Movie Detail Popup on Card Click (2026-03-15) ✅

| Bug | Root Cause | Fix |
|-----|------------|-----|
| Movie detail popup not appearing on card click | `showOverviewModal` crashed immediately with `ReferenceError: tmdbId is not defined` — the function referenced bare `tmdbId` instead of `opts.tmdbId` on line 760. This silently killed the entire modal. | Changed to `window._jellyseerrRequests.has(String(opts.tmdbId))` |
| Request button inside popup did nothing | No click handler was wired for `.btn-request` inside the overview modal HTML. | Added `modalReqBtn.addEventListener('click', ...)` that closes the overview modal and opens `openRequestModal()` correctly. |

---

## Phase 21 — Intelligent Per-User Recommendation Engine (2026-03-15) ✅

### Architecture
Event-driven profile system with no polling. Fully server-side — profiles are never accessible from the browser.

```
[User watches a movie in Jellyfin]
       ↓
UserDataSavedConsumer fires automatically (IServerEntryPoint + IUserDataManager.UserDataSaved)
       ↓
Fetches director/actor TMDB person IDs (from Jellyfin metadata or TMDB credits API fallback)
       ↓
UserProfileService.UpdateWithWatch() applies exponential decay (×0.92) to all existing weights,
then adds new signal: genre ×1, language ×1, actor ×1, director ×2 (recency-weighted)
       ↓
Profile saved as {userId}.json in Jellyfin data folder (server-side only, named by GUID)
       ↓
[User opens Discover page]
       ↓
Frontend sends userId → GET /UpcomingMovies/tmdb/recommendations?userId=...
       ↓
TmdbController loads profile, runs 6 TMDB sources in parallel, scores all candidates
       ↓
Returns top 60, sorted by score (highest first)
```

### New Files
| File | Purpose |
|------|---------|
| `Model/UserProfileData.cs` | Profile schema: genre/director/actor/language weights + watch history (last 200 entries) |
| `Services/UserProfileService.cs` | Read/write JSON profiles; exponential decay on update; top-N helpers |
| `Services/UserDataSavedConsumer.cs` | Jellyfin event consumer — auto-updates profile on movie play/mark-watched |
| `PluginServiceRegistrator.cs` | Registers UserProfileService as DI singleton, UserDataSavedConsumer as IServerEntryPoint |

### Scoring Formula (applied per TMDB candidate)
| Factor | Points |
|--------|--------|
| Movie sourced from favourite director discover | +50 source bonus |
| Movie sourced from favourite actor discover | +40 source bonus |
| Movie sourced from /recommendations of seed | +30 source bonus |
| Movie sourced from /similar of seed | +15 source bonus |
| Genre match (per matching genre × profile weight) | `weight × 1.5` |
| Language affinity | `weight × 2.0` |
| Vote average (0–10) | `va × 5` |
| Popularity (capped at 100) | `pop × 0.3` |
| Release date ≤ 2 years ago | +20 |
| Release date > 10 years ago | −10 |

### Frontend Change
`fetchRecommendations` reduced from ~80 lines (3 Jellyfin API calls + sessionStorage juggling) to 12 lines — sends `userId` only, gets back pre-scored sorted list.

### Debug Endpoint
`GET /UpcomingMovies/tmdb/profile?userId=xxx` — returns current weights, top genres/directors/actors/languages for admin inspection.

---

## Phase 21.1 — Build Fix + Genre Scoring Dominance (2026-03-15) ✅

| Issue | Fix |
|-------|-----|
| Build error: `IServerEntryPoint` not found | Added `using MediaBrowser.Controller.Plugins;` to `UserDataSavedConsumer.cs` and `PluginServiceRegistrator.cs` |
| Build error: `IPluginServiceRegistrator` not found | Same fix — both types live in `MediaBrowser.Controller.Plugins` namespace |
| Genre not highest scoring factor | Genre multiplier raised from `×1.5` → `×8.0` — now clearly the dominant signal |
| Scoring rebalance | Vote average `×5` → `×4`, recency bonus `+20` → `+15`, penalty `-10` → `-8` so genre always wins |

### Updated Scoring Table
| Factor | Points |
|--------|--------|
| **Genre match** (per genre × profile weight) | **`weight × 8.0`** ← highest |
| Director source bonus | +50 |
| Actor source bonus | +40 |
| Seed /recommendations | +30 |
| Seed /similar | +15 |
| Language affinity | `weight × 2.0` |
| Vote average | `× 4.0` |
| Popularity (capped 100) | `× 0.3` |
| Released ≤ 2 years | +15 |
| Released > 10 years | −8 |

**Latest Release: v1.0.29** — Phase 21.1 hotfix (build fix + genre scoring dominance).

**To install:**
1. Dashboard → Plugins → Repositories → add manifest URL above
---

## Phase 21.2 — IHostedService Fix + Balanced Scoring (2026-03-15) ✅

| Issue | Fix |
|-------|-----|
| Build error: `IServerEntryPoint` not found | `IServerEntryPoint` was **removed from Jellyfin 10.10+**. Replaced with `IHostedService` (`StartAsync`/`StopAsync`) in `UserDataSavedConsumer.cs` |
| Registration | `PluginServiceRegistrator` updated to use `serviceCollection.AddHostedService<UserDataSavedConsumer>()` |
| Genre too dominant (×8) | Reduced to ×2.0 for a healthy mix |

### Final Balanced Scoring Table
| Factor | Points |
|--------|--------|
| Director source bonus | +25 |
| Actor source bonus | +20 |
| Seed /recommendations | +30 |
| Seed /similar | +15 |
| Genre match (per genre × weight) | `weight × 2.0` |
| Language affinity | `weight × 2.0` |
| Vote average (0–10) | `× 6.0` → up to 60 pts |
| Popularity (capped 100) | `× 0.5` → up to 50 pts |
| Released ≤ 2 years | +12 |
| Released > 10 years | −8 |

**Latest Release: v1.0.30** — Phase 21.2 (IHostedService fix + balanced scoring).

**To install:**
1. Dashboard → Plugins → Repositories → add manifest URL above



**Pending user actions:**
- Verify TMDB API key is saved in plugin settings
- Verify Jellyseerr URL + API key are saved

---

## Phase 21.3 — Definitive Build Fix (2026-03-15) ✅

**Root Cause:** `IPluginServiceRegistrator` lives in `Jellyfin.Common` (not `Jellyfin.Controller`). Since the project only references `Jellyfin.Controller`, the interface is unavailable in CI. All previous using-directive fixes were wrong — the package itself was missing.

**Solution — eliminated DI registration entirely:**

---

## Phase 21 — Intelligent Per-User Recommendation Engine ✅ (Final: v1.0.33)

### Architecture — Event-Driven, Fully Server-Side

```
[User finishes watching a movie in Jellyfin]
        ↓
IUserDataManager.UserDataSaved event fires (wired in Plugin constructor)
        ↓
UserDataSavedConsumer checks e.UserData.Played == true + item is Movie
        ↓
Fetches genre IDs from BaseItem.Genres + calls TMDB /movie/{id}/credits
  for director and top-5 actor TMDB person IDs
        ↓
UserProfileService.UpdateWithWatch() applies exponential decay (×0.92)
  to all existing weights, then adds new genre/director/actor signals
        ↓
Profile saved as {userId}.json in plugin data folder (server-side, GUID-named)
        ↓
[User opens Discover page]
        ↓
Frontend sends userId → GET /UpcomingMovies/tmdb/recommendations?userId=...
        ↓
TmdbController loads profile, fetches 6 TMDB sources in parallel,
  scores all candidates, returns top 60 sorted by score
```

### Files

| File | Role |
|------|------|
| `Model/UserProfileData.cs` | Profile schema: genre/director/actor/language weights, watch history (last 200) |
| `Services/UserProfileService.cs` | Read/write JSON profiles with exponential decay; top-N helpers |
| `Services/UserDataSavedConsumer.cs` | Plain class wired via Plugin constructor; `OnUserDataSaved` updates profile on watch |
| `Plugin.cs` | Owns `UserProfileService` (static `Plugin.ProfileService`), wires event consumer |
| `Api/TmdbController.cs` | `GetRecommendations` endpoint with 6-source fetch + scoring; `/profile` debug endpoint |
| `Web/discoverPage.js` | `fetchRecommendations` simplified to single server call with `userId` |

### Profile Signals Collected Per Watch

| Signal | Source | Status |
|--------|--------|--------|
| Genre IDs | `BaseItem.Genres` → mapped to TMDB IDs | ✅ Active |
| Director TMDB person IDs | TMDB `/movie/{id}/credits` (crew, job=Director) | ✅ Active |
| Actor TMDB person IDs | TMDB `/movie/{id}/credits` (cast, first 5) | ✅ Active |
| Language | Defaults to `"en"` (Jellyfin API changed in 10.11) | ⚠️ Simplified |

### Scoring Formula (per TMDB candidate)

| Factor | Points | Notes |
|--------|--------|-------|
| Director source bonus | +25 | Movie came from `/discover?with_people=topDirs` |
| Actor source bonus | +20 | Movie came from `/discover?with_people=topActors` |
| Seed /recommendations | +30 | Movie came from `/movie/{watchedSeed}/recommendations` |
| Seed /similar | +15 | Movie came from `/movie/{watchedSeed}/similar` |
| Trending fallback | +5 | New users with 0 watched movies only |
| Genre match | `weight × 2.0` | Per matching genre, using profile weight |
| Language affinity | `weight × 2.0` | Preferred language from profile |
| Vote average (0–10) | `× 6.0` → max ~60 pts | Quality signal |
| Popularity (capped 100) | `× 0.5` → max 50 pts | Cultural relevance |
| Released ≤ 2 years | +12 | Recency bonus |
| Released > 10 years | −8 | Age penalty (classics still surface via quality) |
| Already watched | **Excluded** | Filtered before scoring |

### Types Removed for Jellyfin 10.11.6 Compatibility

The following Jellyfin types moved to `Jellyfin.Data.Enums` or were removed from `Jellyfin.Controller` in 10.10+:

| Type | Reason Removed |
|------|---------------|
| `IPluginServiceRegistrator` | Lives in `Jellyfin.Common`, not `Jellyfin.Controller` — deleted `PluginServiceRegistrator.cs` |
| `IServerEntryPoint` | Removed from Jellyfin in 10.10+ — replaced with Plugin constructor wiring |
| `IHostedService` | Same issue — not needed with Plugin constructor pattern |
| `ILibraryManager.GetPeople()` / `InternalPeopleQuery` | API changed — replaced with TMDB credits API |
| `PersonKind` | Moved to `Jellyfin.Data.Enums` — removed Jellyfin people lookup entirely |
| `BaseItem.OriginalLanguage` | Not a property on Movie in 10.11 — defaults to `"en"` |
| `UserDataSaveReason` | Moved to `Jellyfin.Data.Enums` — replaced with `e.UserData.Played` check |

### Debug Endpoint
`GET /UpcomingMovies/tmdb/profile?userId={jellyfinUserId}` — returns top genres, directors, actors, languages, and recent watch history. Requires Jellyfin auth token.

**Latest Release: v1.0.33** — Phase 21 complete (intelligent per-user recommendation engine, build stable).

---

**To install:**
1. Dashboard → Plugins → Repositories → add manifest URL above
2. Catalog → Upcoming Movies & Recommendations → Install latest
3. Restart Jellyfin

**Pending user actions:**
- Verify TMDB API key is saved in plugin settings
- Verify Jellyseerr URL + API key are saved
- Test Request modal (Destination Server / Quality Profile / Root Folder dropdowns)

---

## Phase 22 — Card UI Fixes + Recommendation Quality (2026-03-15) ✅

**Latest Release: v1.0.35**

### Changes

| Area | Issue | Fix |
|------|-------|-----|
| Card buttons | "✓ Requested" text overflowed card width | `font-size:12px`, `padding:7px 4px`, `min-width:0`, `white-space:nowrap; overflow:hidden; text-overflow:ellipsis` on `.dc-action-bar button` |
| Play button | Static white SVG circle on available movies | Replaced with `<button is="emby-button">` + `<span class="material-icons">play_circle</span>` — gets Jellyfin's native ripple animation. Hover: `scale(1.12)`, Active: `scale(0.95)` |
| Recommendation scoring | New movies dominated (recency +12/-8) | Reduced to `+4` for ≤2yr old, `-3` for >10yr old — older classics now surface via quality (vote_avg ×6.0) |
| Duplicate cards | Same movie could appear twice from parallel TMDB sources | Frontend `Set`-based dedup in `renderTmdbCards` — filters already-seen TMDB IDs before rendering |
| Bug: `ObjectDisposedException` | `JsonDocument` disposed while scoring LINQ accessed elements | `movie.Clone()` in `AddCandidate()` — independent copy survives `using var doc` scope (v1.0.34) |

---

## Phase 23 — Modal Ratings + Card Polish (2026-03-15) ✅

**Latest Release: v1.0.36**

### Changes

| Area | Issue | Fix |
|------|-------|-----|
| Movie detail modal | No ratings shown | Added 🍅 RT / ⭐ IMDB / 🎬 Jellyfin badges. Jellyfin score instant (from `voteAverage`), IMDB + RT async-loaded via new `/UpcomingMovies/tmdb/ratings` endpoint |
| Ratings endpoint | N/A | New `GET /UpcomingMovies/tmdb/ratings?tmdbId={id}` — fetches TMDB details for `imdb_id`, then OMDB for IMDB rating + RT % if `OmdbApiKey` configured |
| Plugin config | N/A | Added optional `OmdbApiKey` field (PluginConfiguration.cs + configPage.html). Free key at omdbapi.com/apikey.aspx |
| Play button | Circle/border around icon | Removed `border` and `background` from `.dc-jellyfin-play-btn` — now just the play icon with drop-shadow on hover |
| Card `Requested` text | Still overflowing at 12px | Added `font-size: 10px !important` specifically for `.btn-request.requested` state |
| Close button (X) | Small — hard to hit | Increased X font-size + touch target in `.htv-modal-close` (`font-size: 28px`, `width/height: 44px`) |

### Architecture Notes

- IMDB badge is clickable → opens `imdb.com/title/{imdbId}/` in new tab when IMDB ID is available
- Ratings fetch is fire-and-forget (`.catch(() => {})`) — failure is silent, badges show `—` as fallback
- `OmdbApiKey` stored server-side; never exposed to frontend

---

## Phase 24 — Language Affinity Scoring Boost (2026-03-15) ✅

**Latest Release: v1.0.37**

### Scoring Table (updated)

| Factor | Formula | Notes |
|--------|---------|-------|
| Source bonus | Direct | Director source +25, Actor source +20, Seed /rec +30, Seed /similar +15, Trending +5 |
| Genre affinity | `profile.GenreWeights[gid] × 2.0` | Per matching genre |
| **Language affinity** | **`profile.LanguageWeights[lang] × 4.0`** | **Was × 2.0 — doubled to strongly prefer watched-language films** |
| Quality | `vote_average × 6.0` | Max ~60 pts |
| Popularity | `min(pop, 100) × 0.5` | Max 50 pts, capped |
| Recency | `+4` if ≤2yr, `−3` if >10yr | Gentle nudge only |

### Rationale
With typical `LanguageWeights["en"] ≈ 10–15` for a heavy English watcher:
- At **× 2.0**: language adds ~20–30 pts → a 9.0/10 Spanish film (54 pts) easily wins
- At **× 4.0**: language adds **~40–60 pts** → preferred-language films now match or beat quality of unpreferred-language films

A sub-par English film (6.0/10 = 36 quality + 50 language ≈ 86) will now outrank a great Spanish film (9.0/10 = 54 + 0 language = 54) for a heavy English watcher. Foreign language films can still appear when the user **has** watched some of that language.

---

## Phase 25 — Requested Button Scope + Dedup Confirmation (2026-03-15) ✅

**Latest Release: v1.0.38**

### Changes

| Area | Fix |
|------|-----|
| Requested text size | `font-size: 10px` now scoped to `.discover-card:not(.upcoming-card) .btn-request.requested` — only applies when Request + Stream sit side-by-side in Recommended. Upcoming cards keep normal inherited font size |
| Deduplication | Confirmed already per-section: `seenIds = new Set()` is local to each `renderTmdbCards()` call — duplicates between Upcoming and Recommended are allowed, duplicates within the same section are prevented |

---

## Phase 26 — Discover More Dedup + Ratings Cache (2026-03-15) ✅

**Latest Release: v1.0.39**

| Area | Problem | Fix |
|------|---------|-----|
| Discover More duplicates | Backend re-runs scoring fresh per page → overlapping candidates | `_renderedRecIds = new Set()` scoped to rec session; both initial render and every Discover More append filter + register against it |
| OMDB ratings quota | Every modal open triggered a fresh OMDB call even for same movie | `window._ratingsCache = {}` by tmdbId — repeat opens read from cache, no extra API call |
| Project Hail Mary | Disappeared from Upcoming, appeared in Recommended | Confirmed correct: PHM release date was 2026-02-21 (past today 2026-03-15). TMDB correctly removes released films from upcoming list |
| Ratings showing "—" | Some movies have no RT/IMDB data in OMDB | Expected for brand-new releases not yet scraped by OMDB. 🎬 Jellyfin badge always shows (direct TMDB source) |

---

## Phase 27 — Recommendation Language Allowlist (2026-03-15) ✅

**Latest Release: v1.0.40**

Added a hard language allowlist in `AddCandidate()` in `TmdbController.cs`. Any movie whose `original_language` is not in the list is rejected before scoring.

| Code | Language |
|------|----------|
| `en` | English / Hollywood |
| `hi` | Hindi |
| `ta` | Tamil |
| `ml` | Malayalam |
| `te` | Telugu |
| `ko` | Korean |
| `ja` | Japanese / Anime |

All other languages (Spanish, French, Chinese, etc.) are silently excluded from the recommendation candidate pool.

---

## Phase 28 — Balanced Multi-Genre Recommendation Engine (2026-03-15) ✅

**Latest Release: v1.0.41**

### Problem Solved
Watching 2–3 movies in the same genre caused that genre to dominate all 60 recommendation slots (linear weight accumulation snowballed). Now 2 animated movies watched → animation is preferred, but secondary genres, director/actor signals, and quality/popular films still appear.

### Changes

| Area | Implementation |
|------|---------------|
| **Log-scale genre weights** | `UserProfileService.NormalizedWeight(w) = log10(1+w) × 11.6` — compresses raw weights onto a curve where 10 watches of one genre scores ~2.6× the user who watched 1 (not 10×). Exposed as `static` for use in scoring. |
| **`BaseWatchWeight` raised 3→5** | Larger per-watch signal so the log curve has room to work; decay (×0.92) unchanged. |
| **NW() alias in TmdbController** | `private static double NW(double w) => UserProfileService.NormalizedWeight(w)` — applied to both genre and language scoring. |
| **Source 7: Popular high-rated (always-on)** | `GET /discover/movie?sort_by=popularity.desc&vote_average.gte=7.0&vote_count.gte=200` with +8 source bonus. Runs for ALL users (not just new), guaranteeing a broad quality pool for the wildcard tier. |
| **3-tier diversity slot allocation** | Final 60 results split: 30 top-scored (any genre) + 20 secondary-genre (movies without the #1 genre tag) + 10 wildcard (va≥7.0, pop≥40). Backfill on each tier prevents empty slots. |
| **Interleaved output** | Tier 1/2/3 are interleaved T1,T2,T3,T1,T2,T3… so the grid looks visually diverse rather than showing all animated movies first. |
| **Rebalanced weights** | Vote average ×7 (was ×6), Popularity ×0.6 (was ×0.5), Recency +10/−6 (was +12/−8). |

### Updated Scoring Formula

| Factor | Formula | Notes |
|--------|---------|-------|
| Genre match | `NW(weight) × 2.0` per genre | Log-normalised — no snowball |
| Language affinity | `NW(weight) × 6.0` | Log-normalised |
| Director source | +25 | Unchanged |
| Actor source | +20 | Unchanged |
| Seed /recommendations | +28 | Slight trim from +30 |
| Seed /similar | +15 | Unchanged |
| Popular source (new) | +8 | Always-on wildcard feeder |
| Trending (new users) | +5 | Unchanged |
| Vote average | `× 7.0` → max 70 pts | |
| Popularity (cap 100) | `× 0.6` → max 60 pts | |
| Released ≤2 years | +10 | |
| Released >10 years | −6 | |

---

## Phase 29 — Watchlist Integration (2026-03-15) ✅

**Release: v1.0.42**

### Features

#### 1. Manual Watchlist Banner
A green bookmark icon (`▲`) appears top-left of the poster on every **available** movie card (movies that exist in the Jellyfin library). Clicking toggles the movie in/out of the Jellyfin native watchlist.

- **Only appears** on available cards (those with a Jellyfin ID). Never on upcoming or TMDB-only unavailable cards.
- **Toggle behaviour:** outline icon = not watchlisted, filled green = watchlisted. State is read from `UserData.Likes` when the library map is fetched on page load (matches KefinTweaks).
- **API:** `POST /Users/{userId}/LikedItems/{jellyfinId}` to add, `DELETE /Users/{userId}/LikedItems/{jellyfinId}` to remove — maps to `UserData.Likes = true`, which is KefinTweaks' watchlist field.

#### 2. Auto-Watchlist on Request Fulfillment
When a user clicks **Request** on an unavailable movie, the server records a `(userId, tmdbId)` pending entry. When Radarr downloads the movie and Jellyfin adds it to the library, `ILibraryManager.ItemAdded` fires — the plugin matches the TMDB ID against the pending list and calls `POST /Users/{userId}/LikedItems/{itemId}` for each waiting user.

- **Pending store:** `upcomingmovies_profiles/watchlist_pending.json` in Jellyfin data folder.
- **Auth:** Requires a Jellyfin Admin API key in plugin settings (Dashboard → API Keys).

### New/Modified Files

| File | Change |
|------|--------|
| `Model/WatchlistPendingEntry.cs` | [NEW] Data model: `{UserId, TmdbId, RequestedAt}` |
| `Services/WatchlistPendingService.cs` | [NEW] Thread-safe JSON store for pending entries |
| `Services/LibraryItemAddedConsumer.cs` | [NEW] Handles `ILibraryManager.ItemAdded` event; fulfills watchlist |
| `Plugin.cs` | Added `ILibraryManager` DI, wires `LibraryItemAddedConsumer.OnItemAdded`, exposes `WatchlistService` static |
| `Configuration/PluginConfiguration.cs` | Added `JellyfinLocalUrl`, `JellyfinLocalApiKey` |
| `Configuration/configPage.html` | Added "Auto-Watchlist" section with both new fields |
| `Api/JellyseerrController.cs` | After successful request → `WatchlistService.AddPending(userId, tmdbId)` |
| `Web/discoverPage.js` | `.dc-watchlist-btn` CSS; bookmark HTML in `buildCard()`; `addToWatchlist()`/`removeFromWatchlist()` helpers; `isWatchlisted` reads `UserData.Likes` in tmdbMap |

### Configuration Required for Auto-Watchlist
In **Dashboard → Plugins → Upcoming Movies → Auto-Watchlist**:
1. **Jellyfin Local URL** — e.g. `http://localhost:8096`
2. **Jellyfin Admin API Key** — generate in Dashboard → API Keys

---

## Phase 30 — Watchlist Auth Fix Attempt (2026-03-15) ⚠️ Incomplete

**Release: v1.0.46**

### What Was Fixed
Fixed the auth header from `X-Emby-Token` (raw token) to `X-Emby-Authorization` with the full `MediaBrowser` format. Also fixed `client._serverAddress` (private) → `client.serverAddress()` (method). However, **the endpoint `/LikedItems/` was still wrong** — movie still did not appear in KefinTweaks watchlist.

---

## Phase 31 — Watchlist Definitive Fix (2026-03-15) ✅

**Release: v1.0.47**

### Root Cause (Final)

Analysed two reference implementations:
1. **swiparr** (`src/lib/providers/jellyfin/index.ts`) — `toggleWatchlist` implementation
2. **KefinTweaks** `apiHelper.js` — `getAuthHeader()`, `getData()`, and `getWatchlistItems()`

The **`/LikedItems/` endpoint does not exist** in modern Jellyfin. The correct endpoint confirmed from swiparr source:

```
POST /Users/{userId}/Items/{itemId}/Rating?Likes=true   → add to watchlist
POST /Users/{userId}/Items/{itemId}/Rating?Likes=false  → remove from watchlist
```

The Likes query parameter sets `UserData.Likes`, which is what KefinTweaks reads to populate its watchlist.

### Auth Header Fix

| | Phase 30 (still wrong) | Phase 31 (correct) |
|---|---|---|
| Header key | `X-Emby-Authorization` | `Authorization` |
| Header value | `MediaBrowser Token="...", Client="...", ...` | `MediaBrowser Token="<token>"` |
| Method (remove) | `DELETE` | `POST` (with `?Likes=false`) |
| Endpoint | `/Users/{uid}/LikedItems/{id}` | `/Users/{uid}/Items/{id}/Rating?Likes=` |

swiparr's `getAuthenticatedHeaders()` uses `'Authorization': 'MediaBrowser Token="..."'` — **the key is `Authorization` (not `X-Emby-Authorization`)**.

### Confirmed API Contract (from swiparr + KefinTweaks)

| Action | Method | Endpoint | Header |
|--------|--------|----------|--------|
| Add to watchlist | `POST` | `/Users/{uid}/Items/{id}/Rating?Likes=true` | `Authorization: MediaBrowser Token="<token>"` |
| Remove from watchlist | `POST` | `/Users/{uid}/Items/{id}/Rating?Likes=false` | `Authorization: MediaBrowser Token="<token>"` |
| Read watchlist state | — | `UserData.Likes` field in Items response | — |

### Files Modified

| File | Change |
|------|--------|
| `Web/discoverPage.js` | `addToWatchlist()`: endpoint changed to `POST /Rating?Likes=true`, header to `Authorization: MediaBrowser Token="<token>"` |
| `Web/discoverPage.js` | `removeFromWatchlist()`: endpoint changed to `POST /Rating?Likes=false` (was `DELETE /LikedItems/`) |

---

## Phase 32 — Watchlist State Persistence + Auto-Watchlist Fix (2026-03-15) ✅

**Release: v1.0.48**

### Bug 1: Watchlist Icon Not Persisting on Reload

**Root cause:** `renderTmdbCards()` never forwarded `movie.isWatchlisted` to `buildCard()`. The data was correctly fetched from `UserData.Likes` and set onto the movie object, but the card was always built with `isWatchlisted = false` because the property wasn't passed.

**Fix:** Added `isWatchlisted: !!movie.isWatchlisted` to the `buildCard()` call inside `renderTmdbCards()`.

```js
// discoverPage.js — renderTmdbCards()
containerEl.appendChild(buildCard({
    ...
    isWatchlisted: !!movie.isWatchlisted   // ← was missing
}));
```

### Bug 2: Auto-Watchlist on Request Fulfillment Not Working

**Root cause:** `LibraryItemAddedConsumer.cs` was calling a non-existent endpoint `/UserWatchlistItems/{id}?userId={uid}` with an `X-Emby-Token` header (raw token). Neither existed in the Jellyfin API.

**Fix:** Changed to the correct endpoint and header format (same as the frontend fix confirmed in Phase 31):

```csharp
// Before (broken)
var url = $"{localUrl}/UserWatchlistItems/{jellyfinItemId}?userId={userId}";
req.Headers.Add("X-Emby-Token", apiKey);

// After (correct)
var url = $"{localUrl}/Users/{userId}/Items/{jellyfinItemId}/Rating?Likes=true";
req.Headers.TryAddWithoutValidation("Authorization", $"MediaBrowser Token=\"{apiKey}\"");
```

### Files Modified

| File | Change |
|------|--------|
| `Web/discoverPage.js` | `renderTmdbCards()`: added `isWatchlisted: !!movie.isWatchlisted` to `buildCard()` call |
| `Services/LibraryItemAddedConsumer.cs` | Fixed endpoint from `/UserWatchlistItems/` → `/Users/{uid}/Items/{id}/Rating?Likes=true`; fixed header from `X-Emby-Token` → `Authorization: MediaBrowser Token="<key>"` |

---

## Phase 33 — Watchlist Signals in Recommendation Engine (2026-03-15) ✅

**Release: v1.0.49**

### Goal
Use each user's watchlist (items where `UserData.Likes = true`) as additional input signals for both **profile building** (genre/director/actor weights) and **candidate seeding** in the recommendation engine.

### Design Decisions

| Signal | Weight | Decay | Rationale |
|--------|--------|-------|-----------|
| Watched (Played=true) | 1.0× BaseWatchWeight (5.0) | Yes (0.92 factor) | Strongest signal — user completed the film |
| Watchlisted (Likes=true) | 0.5× BaseWatchWeight (2.5) | No | Intent signal — user wants to watch; weaker but meaningful |

Watchlist events are **additive only** (no decay) so they don't erode existing taste weights from watch history.

### TMDB Recommendation Seeds

| Source | Seed Set | Bonus | N |
|--------|----------|-------|---|
| Source 1 | Recent watched IDs → `/recommendations` | +30 | 8 |
| **Source 8** | **Recent watchlist IDs → `/recommendations`** | **+22** | **5** |
| Source 2 | Top 3 watched IDs → `/similar` | +15 | 3 |

Watchlist seeds get +22 bonus (between +30 and +15) because watchlist intent is confident but unconfirmed.

### Files Modified

| File | Change |
|------|--------|
| `Model/UserProfileData.cs` | Added `WatchlistTmdbIds: List<int>` — stores up to 100 watchlisted TMDB IDs |
| `Services/UserProfileService.cs` | Added `UpdateWithWatchlist()` (0.5× weight, no decay) and `GetWatchlistSeedIds()` (returns unwatched watchlist items) |
| `Services/UserDataSavedConsumer.cs` | Rewrote `OnUserDataSaved` to handle both `Played=true` (full signal) and `Likes=true` (watchlist signal); both paths share the same TMDB credits fetch |
| `Api/TmdbController.cs` | Added `wlSeedIds` extraction; added Source 8 (watchlist `/recommendations` at +22); updated `hasProfile` to also be `true` when user has watchlist items (unlocks personalised sources for new users who only watchlisted) |

---

## Phase 34 — Discover More Fix + Language-Affinity Scoring (2026-03-15) ✅

**Release: v1.0.50**

### Bug 1: Discover More Only Loading 2–3 Movies After Several Clicks

**Root causes:**
1. Backend sources 1, 2, 3 always fetched TMDB `page=1` regardless of the backend `page` param, so each call returned the same pool → deduplication wiped everything after page 2.
2. Backend output returned all 60 scored movies per page; frontend `ensureRecommendationsBuffer` filled up fast but dedup removed most of them → tiny slices.
3. `ensureRecommendationsBuffer` only checked `_tmdbRecBuffer.length >= targetCount` before dedup, so after dedup the chunk was tiny.

**Fixes:**
- Sources 1/2/3 now cycle TMDB pages based on the backend `page` param (page rotations: 1→2→3, 1→2→3→4→5, 1→2→...→8).
- Backend now returns **20 movies per page** (paginated slice from scored pool) with `total_pages = 50` (virtual). Keeps pool small and varied.
- `ensureRecommendationsBuffer` now fetches **3× targetCount raw** and deduplicates inside the buffer loop (not after), so the buffer always has enough unique items.

### Feature: Language-Affinity Scoring and Discover Source

**Language scoring (existing but underutilised):** Language weights were accumulated at `LanguageWeights[lang] += weight` and scored with `NW(weight) × 6.0`. This was correct but no dedicated source surfaced regional content.

**Source 9 — Language-Affinity Discover (+18 bonus):**
- Detects the user's top non-English language from `LanguageWeights` (e.g. `hi`, `ta`, `ko`).
- Fetches `TMDB /discover/movie?with_original_language={topLang}` filtered by user's top genres.
- Only activates if the user's top language weight ≥ 2.5 (at least 1 watchlist or 0.5× watch signals).
- Movies from this source bypass the standard language allowlist filter (they've already been found by lang filter).
- Cycles TMDB pages 1–10 so each backend call surfaces different regional films.

### Files Modified

| File | Change |
|------|--------|
| `Api/TmdbController.cs` | Sources 1/2/3 cycle TMDB pages per `page` param; `AddCandidate` gains `bypassLangFilter` param; Source 9 language-affinity discover (+18); backend output paginated to 20/page from scored pool |
| `Web/discoverPage.js` | `ensureRecommendationsBuffer` fetches 3× targetCount; deduplication against `_renderedRecIds` moved into buffer fill loop (not after splice) |

---

## Phase 35 — Infinite Scroll + Language Profile Bug Fix (2026-03-15) ✅

**Release: v1.0.51**

### Critical Bug: Regional Languages Not Recommended (e.g., Malayalam)

**Root cause:** `UserDataSavedConsumer.FetchCreditsAndUpdateAsync` had `language` hardcoded to `"en"` for every movie. This meant:
- A user who watches 100 Malayalam films accumulated `LanguageWeights["en"] += 500` but `LanguageWeights["ml"] = 0`
- Source 9 (language-affinity discover) never activated because `ml` weight was always 0
- The scoring `NW(LanguageWeights.GetValueOrDefault("ml")) × 6.0` always added 0 pts for Malayalam films

**Fix:** `UserDataSavedConsumer` now fetches `/movie/{tmdbId}` from TMDB to get `original_language` before updating the profile. The real language (`ml`, `hi`, `ta`, etc.) is now correctly recorded.

### Fix: Discover More Disappearing After 4–5 Clicks

**Root cause:** When `_tmdbRecPage > _tmdbRecTotalPages` (50 virtual pages = ~1000 items), the button was hidden with `style.display='none'` even though the engine can always generate new pages.

**Fix (Infinite Scroll):** When the page cycle exhausts:
1. `_tmdbRecPage` resets to 2 (page 1 was the initial load)
2. `_renderedRecIds` is cleared (fresh dedup cycle)
3. Button is **never hidden** — the engine keeps cycling backend pages indefinitely

### Source 9 Improvements

| Before | After |
|--------|-------|
| Single language only | Up to **2 top non-English languages** in parallel |
| Threshold: weight ≥ 2.5 (never triggered) | Threshold: weight **≥ 0.5** (any single watch) |
| Min vote_count: 50 | Min vote_count: **20** (more regional content) |
| No fallback if genre filter returns nothing | **Genre fallback**: if <5 results with genre filter, also fetches without genre restriction at +15 |

### Files Modified

| File | Change |
|------|--------|
| `Services/UserDataSavedConsumer.cs` | Added `/movie/{tmdbId}` TMDB fetch to get actual `original_language`; renamed method to `FetchDetailsAndUpdateAsync` |
| `Api/TmdbController.cs` | Source 9: multi-language (top 2), threshold 0.5, vote_count 20, genre fallback at +15 |
| `Web/discoverPage.js` | Infinite scroll: page cycle wraps instead of hiding button; button never hides |

---

## Phase 36 — Time-Decay Recency Scoring (2026-03-15) ✅

**Release: v1.0.52**

### Feature: Watch Recency Influences Recommendations

**Goal:** Movies the user watched recently should carry more influence in the recommendation score than films watched months or years ago. The effect should be a gentle nudge, not dominant.

**Implementation — `GetRecentInterestWeights()` in `UserProfileService`:**
- Iterates over `RecentWatches` (last 200 events, stored with timestamps)
- For each watch, computes `decayFactor = exp(-k × daysSince)` where `k = ln(2) / 90` (half-life = 90 days)
  - Watched yesterday → factor = 1.0 (full contribution)
  - Watched 90 days ago → factor = 0.5 (50%)
  - Watched 180 days ago → factor = 0.25 (25%)
  - Watched 1 year ago → factor ≈ 0.08 (8%)
- Sums decayed factors per genre ID and language code
- Returns `(genreRecency, langRecency)` dictionaries — independent from the main accumulated weights

**Scoring injection in `TmdbController.GetRecommendations`:**
- Called once before the `allScored` LINQ, stored as two dictionaries
- Each dictionary is normalised by its max value so the effect is always relative to the user's most-recent interest
- **Genre recency bonus:** `(recentGenre[gid] / maxGenreRecency) × 8.0` → up to +8 pts per matching genre
- **Language recency bonus:** `(recentLang[lang] / maxLangRecency) × 5.0` → up to +5 pts per matching language

**Scale context** (why it's gentle):
- Vote average (quality): up to 70 pts
- Source bonus (seed recs): up to 30 pts
- Genre recency nudge: up to 8 pts per genre
- Language recency nudge: up to 5 pts

A film in a recently-watched genre can shift ~1–4 positions relative to a film in an old-watch genre of similar quality.

### Files Modified

| File | Change |
|------|--------|
| `Services/UserProfileService.cs` | Added `GetRecentInterestWeights()` — exp(-k×days) decay over RecentWatches |
| `Api/TmdbController.cs` | Calls `ProfileService.GetRecentInterestWeights(profile)` before scoring; adds recency bonus to genre (×8) and language (×5) scoring |

---

## Phase 37 — Manifest Fix + CI Workflow Fix (2026-03-15) ✅

**Root cause of versions not appearing in Jellyfin plugin catalog:**  
The GitHub Actions `build-release.yml` manifest-update step was using `git pull --rebase` before pushing the updated `manifest.json`. When multiple tags are pushed in quick succession, by the time CI checks out `main` and tries to push, `main` has already moved ahead (from the previous build's manifest commit). This caused a non-fast-forward rejection and the `manifest.json` never got updated.

**What was fixed:**
- Changed `git pull origin main --rebase` → `git fetch origin main && git reset --hard origin/main` — always starts from the exact remote HEAD, no rebase conflicts
- Changed `git push origin main` → `git push --force-with-lease origin main` — can push even if local fell behind, but rejects if someone else pushed concurrently (safe)

**Versions manually backfilled in manifest.json:** v1.0.48, v1.0.49, v1.0.50, v1.0.51, v1.0.52 (checksums taken from GitHub release body MD5 fields via API).

---

## ⚠️ Release Process — REQUIRED READING FOR ALL FUTURE AGENTS ⚠️

### How a Jellyfin Plugin Release Works

1. **Commit code changes** to the local `main` branch
2. **Tag the release** — the tag name determines the version: `git tag v1.0.XX`
3. **Push both** — `git push origin main && git push origin v1.0.XX`

When the tag is pushed, GitHub Actions (`build-release.yml`) automatically:
- Builds the `.dll` in Release mode
- Packages it as `jellyfin-plugin-upcoming-movies_v1.0.XX.zip`
- Computes MD5 checksum
- **Prepends a new entry to `manifest.json`** on the `main` branch
- Creates a GitHub Release with the ZIP as an asset

Jellyfin reads **`manifest.json` on the `main` branch** to list available versions in the plugin catalog. If `manifest.json` is not updated, the version **will not appear** even if the release ZIP exists.

### When manifest.json Falls Out of Sync (Recovery Procedure)

If Jellyfin does not show a new version, check:
1. Did the CI run succeed? Look at the Actions tab on GitHub
2. Does `manifest.json` on `main` have the new version? Check `https://raw.githubusercontent.com/Hu1k1e/Discover---Jellyfin/main/manifest.json`
3. Does the GitHub Release exist with the ZIP asset?

**If the manifest is missing versions:**
- Get the MD5 checksum from the release body text on GitHub (it says `**ZIP MD5:** \`...\``)
- OR call `https://api.github.com/repos/Hu1k1e/Discover---Jellyfin/releases` to get release metadata
- Manually prepend missing versions to `manifest.json` (newest first), commit, and push `main`

### What NOT to do
- ❌ Do NOT retag and re-push without fixing the manifest — the CI workflow will create a duplicate release
- ❌ Do NOT push tags without committing code first — the CI builds from tagged HEAD
- ❌ Do NOT edit manifest.json without the correct MD5 checksum from the actual built ZIP

### Version Numbering Convention
Current version: **1.0.54**. Next release: **1.0.55**. Always increment the third part by 1.

---

## Phase 39 — Regional Language Profile Repair (2026-03-15) ✅

**Release: v1.0.54**

### Bug: Hindi/Malayalam Not in Recommendations Despite Being Watched

**Root cause:** All profiles built before Phase 35 (v1.0.51) have corrupted `LanguageWeights` — every movie was recorded as `language = "en"` regardless of its actual language. A user who watched 50 Hindi movies accumulated `LanguageWeights["en"] += 250` but `LanguageWeights["hi"] = 0`. Source 9 (language-affinity) never fired for Hindi/Malayalam because their weights were exactly 0 (below the 0.5 threshold).

Korean showed up because the user watched Korean movies **after** v1.0.51 was deployed, correctly accumulating `LanguageWeights["ko"]`.

### Fix: `RepairLanguageWeightsIfNeededAsync` in `UserDataSavedConsumer`

**Trigger:** Fires as a background task after each watch/watchlist event.

**Guard:** Only runs when `sum(non-English LanguageWeights) < 5.0` (profile looks corrupted) AND user has ≥3 watched movies. Once repaired, never runs again.

**Process:**
1. Takes the last 30 `WatchedTmdbIds`
2. Fetches their TMDB records in parallel (max 5 concurrent, rate-limited by `SemaphoreSlim`)
3. Counts how many movies belong to each non-English language
4. Sets `LanguageWeights[lang] = count × 5.0 × 0.4` (conservative, avoids decay overshoot)
5. Saves the repaired profile

**Example:** User watched 20 Hindi movies → `LanguageWeights["hi"] = 20 × 5.0 × 0.4 = 40.0` → NW(40) × 6.0 = big language score → Source 9 fires immediately.

### Files Modified

| File | Change |
|------|--------|
| `Services/UserDataSavedConsumer.cs` | Added `RepairLanguageWeightsIfNeededAsync` — lazy profile repair from TMDB history |