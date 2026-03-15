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

---

# 10. Current Status

**Latest Release: v1.0.19** — Phase 13 completed (UI Transparency, Button Colors, Sidebar Secondary Menus).

**To install:**
1. Dashboard → Plugins → Repositories → add manifest URL above
2. Catalog → Upcoming Movies & Recommendations → Install v1.0.12
3. Restart Jellyfin

**Pending user actions:**
- Verify TMDB API key is saved in plugin settings
- Verify Jellyseerr URL + API key are saved
- Test Request modal (Destination Server / Quality Profile / Root Folder dropdowns)