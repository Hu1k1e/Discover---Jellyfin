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

**Phase 4 Complete: Native Integrations Implemented**
- **Next Action:** Push the new architecture to GitHub. The user will then install the updated plugin and configure the JS Injector, Custom Menu Links, and Custom Tabs.

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

## Phase 3 — Legacy Frontend UI & GitHub CI (2026-03-14) ✅
- Created dual-mode injection system using `MutationObserver` and manual DOM creation.
- Discovered JSON manifest errors resulting from SHA256 checksums in `.github/workflows`; migrated to MD5 checksum generation to satisfy Jellyfin requirements.

## Phase 4 — Native UI Migration (2026-03-14) ✅
- **discoverPage.js**: Stripped all DOM hacking scripts.
  - **Sidebar Mode**: Implemented a standalone listener for `#DiscoverPage`. Users now inject this via Jellyfin's official Dashboard -> Display -> Custom Menu Links JSON (`#!/configurationpage?name=discoverPage`).
  - **Header Mode**: Hooks directly into the `.upcoming-movies-plugin` DOM element dynamically created by the KefinTweaks Custom Tabs plugin.
- Relies on the user's existing JS Injector configuration.

## Pending
- Phase 5: Push the Native UI update to GitHub and tag a new release (`v1.0.4`).
- Phase 6: User installs, configures Custom Menu Links / Custom Tabs, and verifies end-to-end functionality without UI glitches.