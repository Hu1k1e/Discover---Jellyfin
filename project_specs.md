# Project Specifications: Native Upcoming Movies & Recommendations Plugin

---

# 1. Project Overview

This project is a custom Jellyfin Plugin that introduces a native "Upcoming Movies & Recommendations" page directly into the Jellyfin Web UI. 

The system will aggregate data from TMDB and the specific user's Jellyfin watch history/watchlist to display:
1. General Upcoming Movies
2. Recommended Watches (released movies catered to the user)
3. The user's KefinTweaks Watchlist

The layout must mirror the logic and native look of the KefinTweaks watchlist integration.

---

# 2. Initial Discovery & Analysis Mandate

Before writing plugin code, the agent must perform the following analysis:
1. **Download and Analyze:** Review `https://github.com/ranaldsgift/KefinTweaks/` to understand exactly how the Watchlist is natively integrated as a page.
2. **Jellyfin Docs:** Deeply analyze the official Jellyfin Plugin documentation to determine the best method for deploying this specific UI injection and backend data routing.

---

# 3. System Components & UI Interactions

## Frontend Layout
The page must be injected into the Jellyfin client, rendering three distinct sections (Upcoming, Recommended, KefinTweaks Watchlist).

## Action Cards
Movie cards in the Upcoming and Recommended sections must feature two distinct action buttons:
1. **Request on Jellyseerr:** Submits a request to the user's configured Jellyseerr instance.
2. **Stream Directly:** Redirects the user to `https://stream.hulksmash.ca/movie/{tmdb_id}`. The TMDB ID must be dynamically parsed and appended to the URL.

## Plugin Settings Page
The plugin must feature a robust settings page within the Jellyfin dashboard providing maximum customization:
- TMDB API Key input.
- Jellyseerr API Key and URL inputs.
- Toggles for displaying/hiding specific sections.
- UI preference adjustments.

---

# 4. Design System

The application MUST adhere strictly to the native Jellyfin design system.
- Follow the exact layout structural logic used in the KefinTweaks repository.
- Rely exclusively on Jellyfin's default CSS variables and class names (e.g., `card`, `cardBox`, `cardText`) so it adapts to the user's chosen Jellyfin theme.
- Ensure the grid layouts scale gracefully on mobile devices.

---

# 5. Data & API Requirements

1. **TMDB API:** Fetch upcoming movie releases (posters, IDs, titles, dates).
2. **Jellyfin Core API:** Fetch the active `UserId` context, the user's playback history, and watchlist items to drive the recommendation logic.
3. **Jellyseerr API:** Format endpoints to submit media requests.

---

# 6. Current Phase & Next Action

**Phase 3 Complete: Frontend Custom Tabs Injection & GitHub Configuration**
- **Next Action:** Instruct the user on the Jellyfin architecture quirk (JS injection requires the Custom HTML snippet in the Jellyfin Dashboard) so the frontend renders.

---

# 7. Implementation History & Changelog

## Phase 1 — Discovery & Architecture Planning (2026-03-14) ✅
- Analyzed KefinTweaks: JS injection, Watchlist = `Filters=IsLiked`, native Jellyfin CSS classes.
- Analyzed `jellyfin-plugin-custom-tabs`: `data-index` swapping on `.emby-tabs-slider`.
- Confirmed Jellyfin.Controller/Model 10.11.6 NuGet packages exist.
- Proposed C# proxy + dual JS injection architecture.

## Phase 2 — Plugin Scaffolded (2026-03-14) ✅
- Created `.csproj` targeting net9.0 and Jellyfin 10.11.6.
- Added `PluginConfiguration.cs` with TMDB, Jellyseerr, Stream URL, and Nav Placement settings.
- Wrote `configPage.html` using native Jellyfin components.
- Set up C# API proxies `TmdbController` and `JellyseerrController`.

## Phase 3 — Frontend UI & GitHub CI (2026-03-14) ✅
- **discoverPage.js**: Created dual-mode injection system.
  - **Sidebar Mode**: Uses KefinTweaks pattern (MutationObserver on `mainDrawer`).
  - **Header Mode**: Uses IAmParadox27 Custom Tabs pattern (injects `.emby-tab-button` and `.tabContent` directly into the Home page slider).
- Data fetches (Upcoming, Recommendations, Watchlist) happen in parallel via `Promise.all`.
- Updated `manifest.json` with GitHub username `Hu1k1e`.
- Created `.github/workflows/build-release.yml` for automated ZIP builds and JSON manifest updates.

## Pending
- Phase 4: Push code to GitHub, tag release. **(Done)**
- Phase 5: Add manifest URL to Jellyfin repositories, install, configure. **(Done)**
- Phase 6: Inject the UI JS via Dashboard -> General -> Custom HTML code, and perform end-to-end user validation.