# Project Specifications: Native Upcoming Movies & Recommendations Plugin

---

# 1. Project Overview

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
