# Project Specifications: Native Upcoming Movies & Recommendations Plugin

---

# 1. Project Overview

This project is a custom Jellyfin Plugin that introduces a native "Upcoming Movies & Recommendations" page directly into the Jellyfin Web UI.

The system aggregates data from TMDB and the specific user's Jellyfin watch history/watchlist to display:
1. General Upcoming Movies (from TMDB `/movie/upcoming`)
2. Recommended Watches (from TMDB `/discover/movie` filtered by user's genre history)
3. The user's Favourites/Watchlist (Jellyfin native `IsFavorite=true` — same as KefinTweaks)

Layout mirrors the KefinTweaks watchlist: horizontal scroll rows per section.

---

# 2. Initial Discovery & Analysis Mandate

Before writing plugin code, the agent must perform the following analysis:
1. **Download and Analyze:** Review `https://github.com/ranaldsgift/KefinTweaks/` to understand exactly how the Watchlist is natively integrated as a page.
2. **Jellyfin Docs:** Deeply analyze the official Jellyfin Plugin documentation to determine the best method for deploying this specific UI injection and backend data routing.

---

# 3. System Components & UI Interactions

## Frontend Layout
The page is injected into the Jellyfin client, rendering three distinct sections (Upcoming, Recommended, Watchlist). Each section is a horizontally scrollable row of cards styled to match native Jellyfin cards.

## Card Hover Design
All cards use the **native Jellyfin play-button hover style**:
- Semi-dark overlay on hover (`rgba(0,0,0,0.52)`)
- Centered translucent circle with SVG play icon (matches `cardOverlayButton-hover`)
- Bottom action bar with small coloured buttons:
  - **Watchlist cards**: Green "▶ Open" (navigates to `#/details?id=ITEM_ID`) + Red "Stream"
  - **TMDB cards**: Blue "Request" (Jellyseerr) + Red "Stream"
- Entire card is clickable — for Watchlist cards navigates to Jellyfin detail page

## Plugin Settings Page
Configuration via the Jellyfin Dashboard (Dashboard → Plugins → Upcoming Movies & Recommendations):
- TMDB API Key (required for Upcoming and Recommended sections)
- Jellyseerr URL + API Key (for request submission)
- Stream Base URL (TMDB ID appended for direct streaming)
- Navigation Placement (Header tab or Sidebar link)
- Toggles for showing/hiding each section (Upcoming, Recommended, Watchlist)

---

# 4. Design System

- Native Jellyfin CSS variable-based styling (`--theme-primary-color`, etc.)
- Horizontal scroll rows matching KefinTweaks layout
- Play-button hover identical to native Jellyfin card overlay
- Responsive card width: 150px × 225px poster
- CSS injected at runtime by `discoverPage.js` (no separate CSS file needed)

---

# 5. Data & API Requirements

1. **TMDB API (server-side proxy):** All TMDB calls go through `TmdbController.cs`. The API key NEVER leaves the server.
2. **Jellyfin Core API (client-side):** Watchlist (`IsFavorite=true`), watch history (for genre-based recommendations), user authentication.
3. **Jellyseerr API (server-side proxy):** Media request submission via `JellyseerrController.cs`.

---

# 6. Authentication Architecture

| Request | Auth Method | Notes |
|---------|-------------|-------|
| Jellyfin APIs | `X-Emby-Token: {accessToken}` | client-side, from `ApiClient.accessToken()` |
| Plugin backend endpoints | `X-Emby-Authorization: MediaBrowser Token="..."` | header built by `getJellyfinAuthHeader()` |
| Backend auth attribute | `[Authorize]` (plain, no named policy) | Using named policy `DefaultAuthorization` caused HTTP 500 in Jellyfin 10.11 BEFORE code ran |

---

# 7. Manifest & Release Pipeline

## Repository URL for Jellyfin Catalog
```
https://raw.githubusercontent.com/Hu1k1e/Discover---Jellyfin/main/manifest.json
```

## Release Flow
1. Changes committed to `main`
2. Git tag pushed: `git tag vX.Y.Z && git push origin vX.Y.Z`
3. GitHub Actions `build-release.yml` triggers on tag `v*`:
   - Builds .NET Release DLL
   - Creates ZIP with just the `.dll`
   - Computes MD5 checksum
   - **Prepends** new version to `manifest.json` using `jq` (all history preserved)
   - Pulls latest `origin/main` before committing to avoid race condition
   - Pushes manifest update back to `main`
   - Creates GitHub Release with ZIP artifact

## Known Issue: Local Push Race Condition
When we push local commits to `main` at the same time GitHub Actions is updating `manifest.json`, the local push may overwrite the GA manifest commit. **Fix**: always `git pull origin main` before pushing local commits, and the GA workflow now does `git fetch + rebase` before its own manifest commit.

## Manifest Checksum Note
Jellyfin requires **MD5** checksums (not SHA256). The GA workflow uses `md5sum` to generate this.

---

# 8. Implementation History & Changelog

## Phase 1 — Discovery & Architecture Planning (2026-03-14) ✅
- Analyzed KefinTweaks: JS injection, Watchlist = native Jellyfin Favourites, horizontal scroll rows.
- Analyzed `jellyfin-plugin-custom-tabs`: `data-index` swapping on `.emby-tabs-slider`.
- Confirmed Jellyfin.Controller/Model 10.11.6 NuGet packages exist.
- Proposed C# proxy + dual JS injection architecture.

## Phase 2 — Plugin Scaffolded (2026-03-14) ✅
- Created `.csproj` targeting net9.0 and Jellyfin 10.11.6.
- Added `PluginConfiguration.cs` with TMDB, Jellyseerr, Stream URL, and Nav Placement settings.
- Wrote `configPage.html` using native Jellyfin components.
- Set up C# API proxies `TmdbController` and `JellyseerrController`.

## Phase 3 — GitHub CI & Build Pipeline (2026-03-14) ✅
- Created `build-release.yml` GitHub Actions workflow.
- Fixed manifest checksum: SHA256 → MD5 (Jellyfin requirement).
- `manifest.json` auto-updated on each tagged release.

## Phase 4 — Native UI Migration (2026-03-14) ✅
- Migrated from fragile DOM-hacking to native Jellyfin mechanisms.
- Sidebar: Custom Menu Links JSON (`/UpcomingMovies/UI/Discover` endpoint).
- Header: KefinTweaks Custom Tabs integration via `discoverPage.js` auto-detection.
- File Transformation Plugin integration to auto-inject `inject.js` into `index.html`.

## Phase 5 — Build and Release v1.0.5–v1.0.8 (2026-03-14) ✅
- Multiple tagged releases fixing build errors:
  - `TaskTriggerInfoType.StartupTrigger` (not `TaskTriggerInfo.TriggerStartup`)
  - `using System.IO` added to `TransformationPatches.cs`
- First working build: **v1.0.8**

## Phase 6 — Native Script Injection via File Transformation Plugin (2026-03-14) ✅
- Root cause of blank tab: `discoverPage.js` was never loaded (no JS Injector configured).
- Replicated Custom Tabs Plugin pattern: `StartupService.cs` registers `index.html` transformation.
- `TransformationPatches.cs` reads embedded `inject.js` and splices it before `</body>`.
- `inject.js` dynamically loads `discoverPage.js` from plugin endpoint.

## Phase 7 — Live Server Diagnosis & Critical Fixes (2026-03-14) ✅

### 7.1 Live Debugging Findings (Browser at http://192.168.2.54:1000/)
- `discoverPage.js` **loads successfully** (200 OK) — file injection is working.
- Script **executes** — console logs confirm Discover tab initializes.
- **Root cause of blank sections**: Backend `/UpcomingMovies/tmdb/upcoming` and `/UpcomingMovies/tmdb/recommendations` return **HTTP 500**.
- TMDB API key IS configured and saved (32 chars, starts `06227...`). Config save/load confirmed correct.
- The 500 was an **unhandled exception in Jellyfin's auth middleware** that ran BEFORE our try-catch — caused by `[Authorize(Policy = "DefaultAuthorization")]` which does not exist in Jellyfin 10.11's auth pipeline.

### 7.2 Backend Fix — TmdbController.cs (v1.0.11)
**Root cause**: `[Authorize(Policy = "DefaultAuthorization")]` → throws unhandled exception before controller code runs.
**Fix**: Changed to plain `[Authorize]` (no named policy). Also:
- Replaced static `HttpClient` with `IHttpClientFactory` (proper Jellyfin DI pattern)
- Wrapped every action in `try-catch` at outermost level
- Added `tmdbConfigured` boolean to `/config` endpoint response

### 7.3 Frontend Fix — discoverPage.js (v1.0.11)
Complete clean rewrite after partial edit corrupted the file:
- **Watchlist**: Changed `Filters=IsLiked` → `IsFavorite=true` (matches KefinTweaks native Jellyfin Favourites)
- **Card hover**: Native Jellyfin play-button style (translucent circle + SVG play icon + bottom action bar)
- **Watchlist card navigation**: Click anywhere → `window.location.hash = '#/details?id=ITEM_ID'`; green "▶ Open" button also navigates to Jellyfin detail page
- **NEEDS_SETUP sentinel**: Detects 400/500 from backend and shows actionable setup prompt with link to plugin settings

### 7.4 Manifest Pipeline Fix — build-release.yml (v1.0.10)
**Root cause**: `jq` command was overwriting `versions[0]` instead of prepending new entry.
**Fix**: Changed to prepend new version object — all history preserved, Jellyfin shows full revision history.

### 7.5 Manifest Race Condition Fix (v1.0.12+)
**Root cause**: When we `git push` local commits while GitHub Actions is simultaneously pushing a manifest update, our local push overwrites the GA commit.
**Fix**: GA workflow now does `git fetch + rebase` before committing manifest; local workflow should always pull before pushing.

### 7.6 Brave Browser Cache Issue
Page loads in Incognito and Chrome but not Brave. **Not a code issue.**
**Cause**: Brave's Service Worker cache persists even after browser cache clear.
**Fix**: Navigate to `brave://serviceworker-internals/` → find Jellyfin → Stop + Unregister.

---

# 9. File Structure

```
Jellyfin.Plugin.UpcomingMovies/
├── Api/
│   ├── TmdbController.cs       — TMDB proxy (upcoming, recommendations, config)
│   └── JellyseerrController.cs — Jellyseerr request proxy
├── Configuration/
│   ├── configPage.html          — Dashboard settings UI
│   └── PluginConfiguration.cs  — Config model (TmdbApiKey, JellyseerrUrl, etc.)
├── Helpers/
│   └── TransformationPatches.cs — Reads inject.js, splices into index.html
├── Model/
│   └── PatchRequestPayload.cs  — Local mirror of File Transformation Plugin payload type
├── Services/
│   └── StartupService.cs       — IScheduledTask: registers index.html transformation on startup
├── Web/
│   ├── discoverPage.js         — Main frontend script (injectStyles, fetch, render)
│   ├── discoverPage.html       — Bare page shell (for sidebar navigation)
│   └── inject.js               — Bootstrap: dynamically loads discoverPage.js
└── Plugin.cs                   — Plugin entry, registers pages + GUID
```

---

# 10. Current Status

**Latest Release: v1.0.11** (2026-03-14)
- TMDB API calls working (Authorize fix)
- Watchlist matches KefinTweaks (IsFavorite)
- Cards have native Jellyfin play-button hover
- Manifest pipeline fixed (prepend, not overwrite)

**Next Steps**
- User installs v1.0.11 from Jellyfin catalog (Repository URL: see Section 7 above)
- Verify TMDB sections load with API key already saved
- Verify Watchlist matches KefinTweaks list exactly