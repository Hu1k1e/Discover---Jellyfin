/**
 * discoverPage.js
 * Jellyfin Plugin — Upcoming Movies & Recommendations  (Phase 8)
 *
 * Changes in Phase 8:
 *  - Row navigation: drag-to-scroll + clickable arrow buttons (< >) on each side
 *  - Card colours: Request = Jellyseerr purple (#7B5EA7), Stream = H-TV green (#00C853)
 *  - Upcoming cards: only unreleased films, Request-only (no stream / play-click)
 *  - Request button: opens a Jellyseerr quality-profile modal (Destination Server, Quality Profile, Root Folder)
 *  - Recommendations: intelligent multi-source algorithm (watch history + favourites + genre weights + per-film seeds)
 *  - Watchlist section removed; favourites/watchlist data fed into recommendation engine as signals
 */

(function () {
    'use strict';

    const LOG  = (...a) => console.log('[UpcomingMovies]', ...a);
    const WARN = (...a) => console.warn('[UpcomingMovies]', ...a);
    const ERR  = (...a) => console.error('[UpcomingMovies]', ...a);

    const TMDB_IMAGE_BASE = 'https://image.tmdb.org/t/p/w342';

    // ─────────────────────────────────────────────
    // 1. AUTH HELPERS
    // ─────────────────────────────────────────────

    function getJellyfinAuthHeader() {
        var c = window.ApiClient;
        if (!c) return '';
        var token      = c.accessToken ? c.accessToken() : '';
        var deviceId   = c._deviceId || '';
        var deviceName = c._deviceName || 'Jellyfin Web';
        var version    = c._appVersion || '10.11.0';
        return 'MediaBrowser Token="' + token + '", Client="Jellyfin Web", Device="' + deviceName + '", DeviceId="' + deviceId + '", Version="' + version + '"';
    }

    // ─────────────────────────────────────────────
    // 2. PLUGIN CONFIG
    // ─────────────────────────────────────────────

    var _pluginConfig = null;

    async function fetchPluginConfig() {
        if (_pluginConfig) return _pluginConfig;
        try {
            var res = await fetch('/UpcomingMovies/tmdb/config', {
                headers: { 'X-Emby-Authorization': getJellyfinAuthHeader() }
            });
            if (res.ok) _pluginConfig = await res.json();
        } catch (err) {
            WARN('Could not fetch plugin config:', err);
        }
        return _pluginConfig || { streamBaseUrl: '', showUpcoming: true, showRecommendations: true, tmdbConfigured: false, jellyseerrConfigured: false };
    }

    // ─────────────────────────────────────────────
    // 3. INJECTED STYLES
    // ─────────────────────────────────────────────

    function injectStyles() {
        if (document.getElementById('discover-plugin-styles')) return;
        var style = document.createElement('style');
        style.id = 'discover-plugin-styles';
        style.textContent = `
            /* ── Layout ── */
            .discover-page-content { padding: 1.5rem 0; width: 100%; box-sizing: border-box; }
            .discover-section { margin-bottom: 2.5rem; }
            .discover-section-title { margin-bottom: 0.75rem; font-size: 1.2em; padding: 0 5%; font-weight: 500; }

            /* ── Row wrapper with arrow navigation ── */
            .discover-row-wrap {
                position: relative;
            }
            .discover-row {
                display: flex;
                overflow-x: auto;
                gap: 24px;
                padding-top: 4px; padding-bottom: 24px;
                cursor: grab;
                user-select: none;
                -webkit-user-select: none;
                scrollbar-width: none;
            }
            .discover-row::-webkit-scrollbar { display: none; }
            .discover-grid {
                display: grid;
                grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
                gap: 24px;
                padding-top: 4px; padding-bottom: 24px;
            }
            .discover-row.dragging { cursor: grabbing; }

            /* ── Arrow buttons ── */
            .discover-arrow {
                position: absolute;
                top: 50%;
                transform: translateY(-50%);
                width: 34px; height: 34px;
                border-radius: 50%;
                border: none;
                background: rgba(0,0,0,0.72);
                color: #fff;
                font-size: 1.1em;
                cursor: pointer;
                z-index: 5;
                display: flex; align-items: center; justify-content: center;
                transition: background 0.15s, opacity 0.15s;
                opacity: 0;
                pointer-events: none;
            }
            .discover-row-wrap:hover .discover-arrow { opacity: 1; pointer-events: auto; }
            .discover-arrow:hover { background: rgba(0,0,0,0.92); }
            .discover-arrow.left { left: 0; }
            .discover-arrow.right { right: 0; }
            .discover-arrow[disabled] { opacity: 0; pointer-events: none; }

            /* ── Card ── */
            .discover-card {
                display: flex; flex-direction: column;
                flex: 0 0 auto;
                width: 150px;
                background: rgba(255, 255, 255, 0.04);
                border-radius: 8px;
                overflow: hidden; 
                padding-bottom: 6px;
                scroll-snap-align: start;
                user-select: none;
                transition: transform 0.2s ease, box-shadow 0.2s ease;
            }
            .discover-card:hover { transform: scale(1.04); box-shadow: 0 8px 24px rgba(0,0,0,0.6); }
            .discover-grid .discover-card {
                width: 100%;
            }

            /* ── Poster wrapper ── */
            .discover-card .dc-poster {
                position: relative; width: 100%; aspect-ratio: 2/3;
                overflow: hidden; background: #111; flex-shrink: 0;
            }
            .discover-card .dc-poster img { width: 100%; height: 100%; object-fit: cover; display: block; pointer-events: none; }
            .dc-no-poster {
                width: 100%; height: 100%;
                display: flex; align-items: center; justify-content: center;
                background: #1a1a2e; color: #888; font-size: 2em;
            }

            /* ── Star Badge (top-left now) ── */
            .dc-star-badge {
                position: absolute; top: 6px; left: 6px;
                background: rgba(10,10,10,0.85); color: #fff;
                font-size: 0.8em; font-weight: 700;
                padding: 3px 6px; border-radius: 4px;
                display: flex; align-items: center; gap: 4px;
                z-index: 2; pointer-events: none;
            }
            .dc-star-badge svg { width: 12px; height: 12px; fill: #ffc107; }

            /* ── Dismiss X button (top-right, only on recommendation cards) ── */
            .dc-dismiss-btn {
                position: absolute; top: 5px; right: 5px;
                width: 22px; height: 22px;
                background: rgba(20,20,20,0.85);
                border: none; border-radius: 50%;
                color: #fff; font-size: 10px; font-weight: 700; line-height: 22px;
                cursor: pointer; z-index: 4;
                display: flex; align-items: center; justify-content: center;
                opacity: 0; transition: opacity 0.15s, background 0.15s;
                padding: 0;
            }
            .discover-card:hover .dc-dismiss-btn { opacity: 1; }
            .dc-dismiss-btn:hover { background: rgba(220,50,50,0.9); }

            /* ── Hover overlay: dark + centered play circle ── */
            .dc-overlay {
                position: absolute; inset: 0;
                background: rgba(0,0,0,0.52);
                display: flex; align-items: center; justify-content: center;
                opacity: 0; transition: opacity 0.18s ease; z-index: 1;
            }
            .discover-card.hover-enabled:hover .dc-overlay { opacity: 1; }

            /* ── Jellyfin-style play icon (no circle — just icon on dark overlay) ── */
            .dc-jellyfin-play-btn {
                width: 56px; height: 56px;
                background: none;
                border: none;
                display: flex; align-items: center; justify-content: center;
                cursor: pointer; pointer-events: auto;
                transition: transform 0.18s ease, filter 0.18s ease;
                color: #fff; filter: drop-shadow(0 2px 6px rgba(0,0,0,0.7));
            }
            .dc-jellyfin-play-btn:hover { transform: scale(1.18); filter: drop-shadow(0 4px 12px rgba(0,0,0,0.9)) brightness(1.3); }
            .dc-jellyfin-play-btn:active { transform: scale(0.92); }
            .dc-jellyfin-play-btn .material-icons { font-size: 52px; user-select: none; }
            /* Hide play button for upcoming cards */
            .discover-card.upcoming-card .dc-jellyfin-play-btn { display: none; }

            /* ── Card meta (Title below poster) ── */
            .dc-title {
                margin-top: 8px; font-size: 0.9em; font-weight: 500;
                text-align: center; color: #fff;
                white-space: nowrap; overflow: hidden; text-overflow: ellipsis; padding: 0 6px;
            }
            .dc-date { text-align: center; font-size: 0.77em; color: #aaa; margin-top: 2px; }

            /* ── Button Bar (Below Title) ── */
            .dc-action-bar {
                display: flex; flex-direction: row; gap: 5px;
                margin-top: auto; padding: 10px 5px 5px 5px;
            }
            .dc-action-bar button, .dc-action-bar a {
                flex: 1; min-width: 0; border-radius: 6px; font-weight: 600; cursor: pointer; border: none;
                padding: 7px 4px; font-size: 12px;
                text-align: center; text-decoration: none; transition: transform 0.2s, background 0.2s, opacity 0.2s;
                box-sizing: border-box; white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
                background: rgba(255,255,255,0.08); border: 1px solid rgba(255,255,255,0.15);
                color: #e0e0e0; backdrop-filter: blur(4px);
            }
            .dc-action-bar button:hover, .dc-action-bar a:hover { transform: translateY(-2px); color: #fff; }
            .dc-action-bar button:disabled { opacity: 1 !important; transform: none !important; cursor: default; }

            /* Hover Colors */
            .btn-request:hover { background: #7B5EA7 !important; border-color: #7B5EA7 !important; }
            /* Base requested style (no font-size change — upcoming cards have 1 button, plenty of space) */
            .btn-request.requested { background: #4a4a4a !important; border-color: #4a4a4a !important; color: #fff !important; }
            /* Only squeeze font in recommended cards where Request + Stream sit side-by-side */
            .discover-card:not(.upcoming-card) .btn-request.requested { font-size: 10px !important; }
            .btn-stream:hover { background: #00C853 !important; border-color: #00C853 !important; }
            .btn-play:hover { background: #00C853 !important; border-color: #00C853 !important; }

            /* ── Rating badges in overview modal ── */
            .htv-ratings-row { display: flex; gap: 10px; margin: 10px 0 16px; flex-wrap: wrap; }
            .htv-rating-badge {
                display: inline-flex; align-items: center; gap: 5px;
                background: rgba(255,255,255,0.1); border: 1px solid rgba(255,255,255,0.18);
                border-radius: 6px; padding: 5px 9px; font-size: 13px; font-weight: 600;
                color: #fff; text-decoration: none; cursor: default; transition: background 0.18s;
            }
            .htv-rating-badge.clickable { cursor: pointer; }
            .htv-rating-badge.clickable:hover { background: rgba(255,255,255,0.2); }
            .htv-rating-badge .badge-icon { font-size: 15px; }
            .htv-rating-badge.rt-fresh { color: #fa320a; border-color: rgba(250,50,10,0.4); }
            .htv-rating-badge.imdb { color: #f5c518; border-color: rgba(245,197,24,0.4); }
            .htv-rating-badge.jellyfin { color: #00a4dc; border-color: rgba(0,164,220,0.4); }

            .discover-loading { padding: 14px 0; color: #999; font-style: italic; }
            .discover-error   { padding: 14px 0; color: #ef5350; line-height: 1.7; }
            /* ── Overview Modal ── */
            .htv-modal-overlay {
                position: fixed; inset: 0; background: rgba(0,0,0,0.85); z-index: 99999;
                display: flex; align-items: center; justify-content: center; backdrop-filter: blur(10px);
                opacity: 0; transition: opacity 0.3s ease;
            }
            .htv-modal-overlay.show { opacity: 1; }
            .htv-modal-content {
                background: #111; border-radius: 12px; width: 90%; max-width: 800px;
                max-height: 90vh; overflow-y: auto; position: relative;
                box-shadow: 0 10px 40px rgba(0,0,0,0.8);
                transform: scale(0.95); transition: transform 0.3s cubic-bezier(0.175, 0.885, 0.32, 1.275);
            }
            .htv-modal-overlay.show .htv-modal-content { transform: scale(1); }
            
            .htv-modal-backdrop-wrap {
                position: absolute; top: 0; left: 0; right: 0; height: 350px;
                overflow: hidden; border-radius: 12px 12px 0 0; z-index: 0; pointer-events: none;
            }
            .htv-modal-backdrop {
                width: 100%; height: 100%; background-size: cover; background-position: center 20%;
                filter: blur(12px) brightness(50%) saturate(120%); transform: scale(1.15);
            }
            .htv-modal-backdrop-overlay {
                position: absolute; inset: 0;
                background: linear-gradient(transparent 10%, #111 100%); z-index: 1;
            }

            .htv-modal-close {
                position: absolute; top: 12px; right: 12px; background: rgba(0,0,0,0.6);
                border: none; color: #fff; font-size: 28px; width: 52px; height: 52px;
                border-radius: 50%; cursor: pointer; z-index: 10; display: flex; align-items: center; justify-content: center;
                transition: background 0.2s;
            }
            .htv-modal-close:hover { background: rgba(255,255,255,0.25); }
            
            .htv-modal-body {
                padding: 0 30px 30px 30px; display: flex; gap: 30px; margin-top: 120px; position: relative; z-index: 5;
            }
            @media (max-width: 600px) {
                .htv-modal-body { flex-direction: column; align-items: center; margin-top: -40px; gap: 20px; }
            }
            .htv-modal-poster {
                width: 160px; flex-shrink: 0; border-radius: 8px; box-shadow: 0 5px 20px rgba(0,0,0,0.6);
                aspect-ratio: 2/3; object-fit: cover; border: 2px solid rgba(255,255,255,0.1);
            }
            
            .htv-modal-info { flex: 1; color: #ddd; display: flex; flex-direction: column; }
            .htv-modal-title { font-size: 2em; color: #fff; margin: 0 0 5px 0; font-weight: 700; line-height: 1.1; }
            .htv-modal-date { font-size: 0.9em; color: #aaa; margin-bottom: 16px; }
            .htv-modal-overview { font-size: 1.05em; line-height: 1.6; margin-bottom: 24px; flex: 1; }
            
            .htv-modal-actions { display: flex; gap: 12px; }
            .htv-modal-actions button, .htv-modal-actions a {
                padding: 12px 24px; border-radius: 8px; font-weight: 600; cursor: pointer; border: none; font-size: 15px;
                text-align: center; text-decoration: none; transition: transform 0.2s, background 0.2s, box-shadow 0.2s;
                background: rgba(255, 255, 255, 0.08); border: 1px solid rgba(255, 255, 255, 0.15); color: #fff;
            }
            .htv-modal-actions button:hover, .htv-modal-actions a:hover { transform: translateY(-2px); box-shadow: 0 4px 15px rgba(0,0,0,0.3); }
            .htv-modal-actions .btn-request:hover { background: #7B5EA7 !important; border-color: #7B5EA7 !important; }
            .htv-modal-actions .btn-stream:hover { background: #00C853 !important; border-color: #00C853 !important; }
            .htv-modal-actions .btn-request.requested { background: #4a4a4a !important; border-color: #4a4a4a !important; transform: none; cursor: default; box-shadow: none; pointer-events: none; opacity: 0.6; }
            /* Global card-level requested button */
            .btn-request.requested { opacity: 0.65 !important; cursor: default !important; pointer-events: none !important; }
            /* ─────────────────────────────────────────────────────
               REQUEST MODAL (Jellyseerr quality-profile)
               ───────────────────────────────────────────────────── */
            .dcm-backdrop {
                position: fixed; inset: 0; z-index: 10000;
                background: rgba(0,0,0,0.78);
                display: flex; align-items: center; justify-content: center;
                animation: dcm-fadein 0.18s ease;
            }
            @keyframes dcm-fadein { from { opacity: 0; } to { opacity: 1; } }
            .dcm-box {
                background: #1a1c2c; border-radius: 8px;
                width: 520px; max-width: 94vw;
                overflow: hidden;
                box-shadow: 0 20px 60px rgba(0,0,0,0.8);
                animation: dcm-slidein 0.2s ease;
            }
            @keyframes dcm-slidein { from { transform: translateY(24px); opacity: 0; } to { transform: none; opacity: 1; } }
            .dcm-header {
                position: relative; height: 130px;
                background: linear-gradient(to bottom, transparent 40%, #1a1c2c), center/cover;
                display: flex; align-items: flex-end; padding: 16px 20px 12px;
            }
            .dcm-header-title { font-size: 1.3em; font-weight: 700; color: #00c2ff; }
            .dcm-header-movie { font-size: 0.95em; color: #ddd; margin-top: 4px; }
            .dcm-body { padding: 18px 20px 20px; }
            .dcm-section-label {
                font-size: 0.78em; font-weight: 600; letter-spacing: 0.08em;
                color: #00c2ff; text-transform: uppercase; margin-bottom: 6px; margin-top: 14px;
            }
            .dcm-section-label:first-child { margin-top: 0; }
            .dcm-select {
                width: 100%; padding: 10px 12px; border-radius: 5px;
                background: #252840; border: 1px solid #3a3d5c;
                color: #fff; font-size: 0.93em; cursor: pointer;
                appearance: none;
                background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24'%3E%3Cpath fill='%23888' d='M7 10l5 5 5-5z'/%3E%3C/svg%3E");
                background-repeat: no-repeat; background-position: right 10px center; background-size: 20px;
                padding-right: 36px;
            }
            .dcm-select:focus { outline: none; border-color: #7B5EA7; }
            .dcm-row { display: flex; gap: 14px; }
            .dcm-row > div { flex: 1; }
            .dcm-footer { display: flex; justify-content: flex-end; gap: 10px; padding: 14px 20px 18px; border-top: 1px solid #2d3050; }
            .dcm-btn { padding: 9px 22px; border-radius: 5px; font-size: 0.93em; font-weight: 600; cursor: pointer; border: none; }
            .dcm-btn-cancel { background: #2e3150; color: #aaa; }
            .dcm-btn-cancel:hover { background: #3a3e65; }
            .dcm-btn-request { background: #00C853; color: #000; }
            .dcm-btn-request:hover { background: #00e676; }
            .dcm-btn-request:disabled { background: #555; color: #888; cursor: not-allowed; }
            .dcm-status { padding: 8px 20px; font-size: 0.87em; color: #aaa; text-align: center; min-height: 28px; }
            .dcm-status.error { color: #ef5350; }
            .dcm-status.success { color: #00C853; }

            /* ── Watchlist bookmark banner ── */
            .dc-watchlist-btn {
                position: absolute; bottom: 7px; right: 7px;
                width: 30px; height: 36px;
                background: rgba(0, 0, 0, 0.75); backdrop-filter: blur(4px);
                border: none; border-radius: 4px;
                cursor: pointer; z-index: 3;
                display: flex; align-items: center; justify-content: center;
                transition: background 0.18s, transform 0.18s;
                padding: 0;
            }
            .dc-watchlist-btn:hover { background: rgba(0, 200, 83, 0.85); transform: scale(1.1); }
            .dc-watchlist-btn svg { width: 16px; height: 16px; fill: #fff; transition: fill 0.18s; }
            .dc-watchlist-btn.active { background: rgba(0, 200, 83, 0.9); }
            .dc-watchlist-btn.active svg { fill: #fff; }

            /* ── Filter Panel ————————————————— */
            .df-toggle-btn {
                background: rgba(255,255,255,0.07);
                border: 1px solid rgba(255,255,255,0.16);
                color: #e0e0e0;
                border-radius: 6px;
                padding: 5px 12px;
                cursor: pointer;
                font-size: 0.82em;
                font-weight: 600;
                letter-spacing: 0.03em;
                white-space: nowrap;
                transition: background 0.18s, border-color 0.18s;
                margin-right: 4%;
                flex-shrink: 0;
            }
            .df-toggle-btn:hover, .df-toggle-btn.active { background: rgba(0,200,83,0.16); border-color: #00C853; color: #00C853; }
            .df-panel {
                margin: 0 4% 14px 4%;
                background: rgba(18,18,28,0.97);
                border: 1px solid rgba(255,255,255,0.1);
                border-radius: 10px;
                padding: 16px 20px 14px 20px;
                display: none;
            }
            .df-panel.show { display: block; }
            .df-section-label {
                font-size: 0.75em;
                font-weight: 700;
                letter-spacing: 0.08em;
                text-transform: uppercase;
                color: #888;
                margin: 12px 0 7px 0;
            }
            .df-section-label:first-child { margin-top: 0; }
            .df-pills { display: flex; flex-wrap: wrap; gap: 6px; }
            .df-pill {
                padding: 4px 11px;
                border-radius: 20px;
                border: 1px solid rgba(255,255,255,0.18);
                background: rgba(255,255,255,0.05);
                color: #ccc;
                font-size: 0.8em;
                cursor: pointer;
                transition: background 0.15s, border-color 0.15s, color 0.15s;
                user-select: none;
            }
            .df-pill.active { background: rgba(0,200,83,0.22); border-color: #00C853; color: #00C853; }
            .df-pill:hover { border-color: #aaa; color: #fff; }
            .df-check-row { display: flex; flex-wrap: wrap; gap: 10px 18px; }
            .df-check-row label {
                display: flex; align-items: center; gap: 5px;
                font-size: 0.82em; color: #ccc; cursor: pointer;
            }
            .df-check-row input[type=checkbox] { accent-color: #00C853; width: 14px; height: 14px; }
            .df-date-row { display: flex; gap: 14px; flex-wrap: wrap; align-items: center; }
            .df-date-row label { font-size: 0.82em; color: #aaa; display: flex; align-items: center; gap: 6px; }
            .df-date-input {
                background: rgba(255,255,255,0.07);
                border: 1px solid rgba(255,255,255,0.15);
                color: #e0e0e0;
                border-radius: 5px;
                padding: 4px 8px;
                font-size: 0.82em;
            }
            .df-footer { display: flex; gap: 10px; margin-top: 14px; justify-content: flex-end; }
            .df-apply-btn {
                background: #00C853;
                color: #000;
                border: none;
                border-radius: 6px;
                padding: 6px 18px;
                font-weight: 700;
                font-size: 0.84em;
                cursor: pointer;
                transition: background 0.18s;
            }
            .df-apply-btn:hover { background: #00e676; }
            .df-reset-btn {
                background: transparent;
                color: #888;
                border: 1px solid rgba(255,255,255,0.12);
                border-radius: 6px;
                padding: 6px 14px;
                font-size: 0.84em;
                cursor: pointer;
            }
            .df-reset-btn:hover { color: #ccc; border-color: #aaa; }
        `;
        document.head.appendChild(style);
    }

    // ─────────────────────────────────────────────
    // 4. HTML TEMPLATE
    // ─────────────────────────────────────────────

    // Language + genre constants used by both sections
    var FILTER_LANGS = [
        { code: 'en', label: 'English' },
        { code: 'hi', label: 'Hindi' },
        { code: 'ml', label: 'Malayalam' },
        { code: 'ta', label: 'Tamil' },
        { code: 'te', label: 'Telugu' },
        { code: 'ko', label: 'Korean' },
        { code: 'ja', label: 'Japanese' }
    ];
    var FILTER_GENRES = [
        { id: 28,    name: 'Action' },    { id: 12,    name: 'Adventure' },
        { id: 16,    name: 'Animation' }, { id: 35,    name: 'Comedy' },
        { id: 80,    name: 'Crime' },     { id: 99,    name: 'Documentary' },
        { id: 18,    name: 'Drama' },     { id: 10751, name: 'Family' },
        { id: 14,    name: 'Fantasy' },   { id: 36,    name: 'History' },
        { id: 27,    name: 'Horror' },    { id: 10402, name: 'Music' },
        { id: 9648,  name: 'Mystery' },   { id: 10749, name: 'Romance' },
        { id: 878,   name: 'Sci-Fi' },    { id: 53,    name: 'Thriller' },
        { id: 10752, name: 'War' },       { id: 37,    name: 'Western' }
    ];
    var RELEASE_TYPES = [
        { val: 1, label: 'Premiere' },
        { val: 2, label: 'Theatrical (limited)' },
        { val: 3, label: 'Theatrical' },
        { val: 4, label: 'Digital' },
        { val: 5, label: 'Physical' },
        { val: 6, label: 'TV' }
    ];

    function _todayStr() { return new Date().toISOString().slice(0,10); }
    function _oneYearStr() { var d = new Date(); d.setFullYear(d.getFullYear()+1); return d.toISOString().slice(0,10); }

    // Default filter states
    var _upcFilters = {
        languages:    FILTER_LANGS.map(function(l){ return l.code; }),   // all selected
        genres:       [],           // [] = all genres
        releaseTypes: [1,2,3,4,5], // all except TV
        dateFrom:     _todayStr(),
        dateTo:       _oneYearStr()
    };
    var _recFilters = {
        languages: [],  // [] = no restriction
        genres:    []   // [] = all genres
    };

    function buildFilterPanelHtml(sectionId) {
        var isUpcoming = sectionId === 'upcoming';
        var filters    = isUpcoming ? _upcFilters : _recFilters;

        var langPills = FILTER_LANGS.map(function(l) {
            var active = isUpcoming
                ? (filters.languages.indexOf(l.code) !== -1 ? ' active' : '')
                : (filters.languages.indexOf(l.code) !== -1 ? ' active' : '');
            return '<button class="df-pill' + active + '" data-filter="lang" data-val="' + l.code + '">' + l.label + '</button>';
        }).join('');

        var genrePills = FILTER_GENRES.map(function(g) {
            var active = filters.genres.indexOf(g.id) !== -1 ? ' active' : '';
            return '<button class="df-pill' + active + '" data-filter="genre" data-val="' + g.id + '">' + g.name + '</button>';
        }).join('');

        var rtHtml = '';
        var dateHtml = '';
        if (isUpcoming) {
            rtHtml = '<div class="df-section-label">Release Types</div>'
                + '<div class="df-check-row">' + RELEASE_TYPES.map(function(rt) {
                    var chk = filters.releaseTypes.indexOf(rt.val) !== -1 ? ' checked' : '';
                    return '<label><input type="checkbox" data-filter="rt" data-val="' + rt.val + '"' + chk + '> ' + rt.label + '</label>';
                }).join('') + '</div>';
            dateHtml = '<div class="df-section-label">Release Date Range</div>'
                + '<div class="df-date-row">'
                + '<label>From <input type="date" class="df-date-input" data-filter="dateFrom" value="' + filters.dateFrom + '"></label>'
                + '<label>To &nbsp; <input type="date" class="df-date-input" data-filter="dateTo" value="' + filters.dateTo + '"></label>'
                + '</div>';
        }

        return '<div class="df-panel" data-panel="' + sectionId + '">'
            + '<div class="df-section-label">Languages</div>'
            + '<div class="df-pills" data-group="lang">' + langPills + '</div>'
            + '<div class="df-section-label">Genres</div>'
            + '<div class="df-pills" data-group="genre">' + genrePills + '</div>'
            + rtHtml + dateHtml
            + '<div class="df-footer">'
            + '<button class="df-reset-btn" data-reset="' + sectionId + '">Reset</button>'
            + '<button class="df-apply-btn" data-apply="' + sectionId + '">Apply Filters</button>'
            + '</div>'
            + '</div>';
    }

    // Shared header wrapper style: consistent height so both filter buttons align vertically
    var SECTION_HEADER_STYLE = 'display:flex;align-items:center;justify-content:space-between;min-height:48px;';

    function buildSectionHtml(id, title, isGrid) {
        var filterBtn = '<button class="df-toggle-btn" data-toggle-filter="' + id + '">Filters &#9660;</button>';
        if (isGrid) {
            return '<div class="discover-section" data-section="' + id + '">'
                + '<div style="' + SECTION_HEADER_STYLE + '">'
                + '<h2 class="discover-section-title sectionTitle sectionTitle-cards padded-left" style="margin:0;">'
                +   title
                + '</h2>'
                + '<div style="display:flex;gap:8px;align-items:center;margin-right:4%;">' + filterBtn + '</div>'
                + '</div>'
                + buildFilterPanelHtml(id)
                + '<div class="discover-grid padded-left padded-right" data-row="' + id + '">'
                + '  <div class="discover-loading">Loading&hellip;</div>'
                + '</div>'
                + '<div style="text-align:center; padding: 10px;"><button class="btn-discover-more dcm-btn" data-more="' + id + '" style="background: rgba(255,255,255,0.08); border: 1px solid rgba(255,255,255,0.15); color:#fff; display:none; padding:12px 24px; border-radius:8px; font-weight:600; cursor:pointer; transition:background 0.2s, transform 0.2s;" onmouseover="this.style.background=\'#00C853\'" onmouseout="this.style.background=\'rgba(255,255,255,0.08)\'">Discover More</button></div>'
                + '</div>';
        }
        var refreshBtn = '<button is="emby-button" class="emby-button paper-icon-button-light" style="color:#aaa; font-size:1.1em; padding:8px;" title="Refresh Upcoming" data-action="refresh-upcoming"><i class="material-icons">refresh</i></button>';
        return '<div class="discover-section" data-section="' + id + '">'
            + '<div style="' + SECTION_HEADER_STYLE + '">'
            + '<h2 class="discover-section-title sectionTitle sectionTitle-cards padded-left" style="margin:0;">'
            +   title
            + '</h2>'
            + '<div style="display:flex;gap:8px;align-items:center;margin-right:4%;">' + refreshBtn + filterBtn + '</div>'
            + '</div>'
            + buildFilterPanelHtml(id)
            + '<div class="discover-row-wrap">'
            + '  <div class="discover-row padded-left padded-right" data-row="' + id + '">'
            + '    <div class="discover-loading">Loading&hellip;</div>'
            + '  </div>'
            + '</div></div>';
    }

    function getGridTemplate() {
        return '<div class="discover-page-content">'
            + buildSectionHtml('upcoming', 'Upcoming Movies', false)
            + buildSectionHtml('recommended', 'Recommended For You', true)
            + '</div>';
    }

    function escapeHtml(str) {
        if (!str) return '';
        return String(str)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;')
            .replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    // ─────────────────────────────────────────────
    // 5. ROW DRAG + ARROW NAVIGATION
    // ─────────────────────────────────────────────

    function initRowNavigation(rowEl) {
        var wrap = rowEl.closest('.discover-row-wrap');
        var btnLeft  = wrap ? wrap.querySelector('.discover-arrow.left')  : null;
        var btnRight = wrap ? wrap.querySelector('.discover-arrow.right') : null;

        var step = 480; // px per arrow click

        function updateArrows() {
            if (!btnLeft || !btnRight) return;
            btnLeft.disabled  = rowEl.scrollLeft <= 0;
            btnRight.disabled = rowEl.scrollLeft >= rowEl.scrollWidth - rowEl.clientWidth - 2;
        }
        rowEl.addEventListener('scroll', updateArrows, { passive: true });

        if (btnLeft) btnLeft.addEventListener('click', function() {
            rowEl.scrollBy({ left: -step, behavior: 'smooth' });
        });
        if (btnRight) btnRight.addEventListener('click', function() {
            rowEl.scrollBy({ left: step, behavior: 'smooth' });
        });

        // Drag-to-scroll (mouse)
        var isDragging = false, startX = 0, startScrollLeft = 0;

        rowEl.addEventListener('mousedown', function(e) {
            if (e.button !== 0) return;
            isDragging = true;
            startX = e.pageX - rowEl.offsetLeft;
            startScrollLeft = rowEl.scrollLeft;
            rowEl.classList.add('dragging');
            e.preventDefault();
        });
        document.addEventListener('mousemove', function(e) {
            if (!isDragging) return;
            var x = e.pageX - rowEl.offsetLeft;
            var walk = (x - startX) * 1.2;
            rowEl.scrollLeft = startScrollLeft - walk;
        });
        document.addEventListener('mouseup', function() {
            if (isDragging) { isDragging = false; rowEl.classList.remove('dragging'); }
        });

        // Touch drag
        var touchStartX = 0, touchScrollLeft = 0;
        rowEl.addEventListener('touchstart', function(e) {
            touchStartX = e.touches[0].pageX;
            touchScrollLeft = rowEl.scrollLeft;
        }, { passive: true });
        rowEl.addEventListener('touchmove', function(e) {
            var diff = touchStartX - e.touches[0].pageX;
            rowEl.scrollLeft = touchScrollLeft + diff;
        }, { passive: true });

        // Use proper overflow for scroll when nav is enabled
        rowEl.style.overflowX = 'auto';
        rowEl.style.scrollbarWidth = 'none';
        rowEl.style.msOverflowStyle = 'none';
        rowEl.style.webkitOverflowScrolling = 'touch';
        // Hide scrollbar via pseudo — already in CSS; also hide via style:
        rowEl.addEventListener('scroll', updateArrows, { passive: true });
        setTimeout(updateArrows, 100);
    }

    // ─────────────────────────────────────────────
    // 6. DATA FETCHING
    // ─────────────────────────────────────────────

    var GENRE_MAP = {
        'Action': 28, 'Adventure': 12, 'Animation': 16, 'Comedy': 35,
        'Crime': 80, 'Documentary': 99, 'Drama': 18, 'Family': 10751,
        'Fantasy': 14, 'History': 36, 'Horror': 27, 'Music': 10402,
        'Mystery': 9648, 'Romance': 10749, 'Science Fiction': 878,
        'Sci-Fi': 878, 'Thriller': 53, 'War': 10752, 'Western': 37
    };

    var NEEDS_SETUP = { _needsSetup: true };

    async function fetchUpcoming(filters) {
        var params = [];
        if (filters) {
            if (filters.languages  && filters.languages.length)  params.push('languages='    + encodeURIComponent(filters.languages.join(',')));
            if (filters.genres     && filters.genres.length)     params.push('genres='       + encodeURIComponent(filters.genres.join(',')));
            if (filters.releaseTypes && filters.releaseTypes.length) params.push('releaseTypes=' + encodeURIComponent(filters.releaseTypes.join(',')));
            if (filters.dateFrom)  params.push('dateFrom=' + encodeURIComponent(filters.dateFrom));
            if (filters.dateTo)    params.push('dateTo='   + encodeURIComponent(filters.dateTo));
        }
        var qs = params.length ? '?' + params.join('&') : '';
        var res = await fetch('/UpcomingMovies/tmdb/upcoming' + qs, {
            headers: { 'X-Emby-Authorization': getJellyfinAuthHeader() }
        });
        if (res.status === 400 || res.status === 500) return NEEDS_SETUP;
        if (!res.ok) throw new Error('TMDB upstream error ' + res.status);
        return res.json();
    }


    // ─────────────────────────────────────────────────────────────────────────────
    // 6. RECOMMENDATIONS  (Phase 21 — server-side profile + scoring engine)
    // ─────────────────────────────────────────────────────────────────────────────
    //
    // The user's taste profile (genre/director/actor/language weights, watch history)
    // is maintained server-side and auto-updated by UserDataSavedConsumer whenever
    // a movie is played. This function simply passes userId to the backend and
    // receives a pre-scored, ranked list — no heavy client-side work needed.
    // ─────────────────────────────────────────────────────────────────────────────

    async function fetchRecommendations(page, filters) {
        page = page || 1;
        var client = window.ApiClient;
        var userId = client && client.getCurrentUserId ? client.getCurrentUserId() : '';

        var params = ['page=' + page];
        if (userId) params.push('userId=' + encodeURIComponent(userId));
        if (filters) {
            if (filters.languages && filters.languages.length) params.push('filterLanguages=' + encodeURIComponent(filters.languages.join(',')));
            if (filters.genres    && filters.genres.length)    params.push('filterGenres='    + encodeURIComponent(filters.genres.join(',')));
            if (filters.dateFrom) params.push('filterDateFrom=' + encodeURIComponent(filters.dateFrom));
            if (filters.dateTo)   params.push('filterDateTo='   + encodeURIComponent(filters.dateTo));
        }

        var res = await fetch('/UpcomingMovies/tmdb/recommendations?' + params.join('&'), {
            headers: { 'X-Emby-Authorization': getJellyfinAuthHeader() }
        });
        if (res.status === 400 || res.status === 500) return NEEDS_SETUP;
        if (!res.ok) throw new Error('Recommendations error ' + res.status);
        return res.json();
    }

    // ─────────────────────────────────────────────
    // 7. JELLYSEERR MODAL
    // ─────────────────────────────────────────────

    // Pre-cached Radarr instances — fetched once, reused for instant modal open
    var _radarrCache = null;
    async function _fetchRadarrCached() {
        if (_radarrCache !== null) return _radarrCache;
        try {
            var rr = await fetch('/UpcomingMovies/jellyseerr/radarr', { headers: { 'X-Emby-Authorization': getJellyfinAuthHeader() } });
            if (rr.ok) _radarrCache = await rr.json();
        } catch(e) { WARN('Radarr prefetch failed', e); }
        return _radarrCache || [];
    }

    async function openRequestModal(tmdbId, movieTitle, backdropUrl) {
        closeAnyOpenModal();
        var radarrInstances = await _fetchRadarrCached();

        // Build modal DOM
        var backdrop = document.createElement('div');
        backdrop.className = 'dcm-backdrop';

        // Build server options
        var serverOptions = radarrInstances.length
            ? radarrInstances.map(function(s, i) {
                return '<option value="' + s.id + '" data-index="' + i + '">' + escapeHtml(s.name) + '</option>';
            }).join('')
            : '<option value="">Default</option>';

        function getProfileOptions(instance) {
            if (!instance || !instance.profiles || !instance.profiles.length) return '<option value="">Default</option>';
            return instance.profiles.map(function(p) {
                var isDefault = instance.activeProfileId === p.id;
                return '<option value="' + p.id + '"' + (isDefault ? ' selected' : '') + '>' + escapeHtml(p.name) + '</option>';
            }).join('');
        }

        function getRootFolderOptions(instance) {
            if (!instance || !instance.paths || !instance.paths.length) return '<option value="">Default</option>';
            return instance.paths.map(function(p) {
                var isDefault = instance.activeDirectory === p;
                return '<option value="' + escapeHtml(p) + '"' + (isDefault ? ' selected' : '') + '>' + escapeHtml(p) + '</option>';
            }).join('');
        }

        var firstInstance = radarrInstances.length ? radarrInstances[0] : null;

        var bgStyle = backdropUrl ? 'background-image: url(' + backdropUrl + ')' : '';
        backdrop.innerHTML =
            '<div class="dcm-box">'
            + '  <div class="dcm-header" style="' + bgStyle + '">'
            + '    <div>'
            + '      <div class="dcm-header-title">Request Movie</div>'
            + '      <div class="dcm-header-movie">' + escapeHtml(movieTitle) + '</div>'
            + '    </div>'
            + '  </div>'
            + '  <div class="dcm-body">'
            + '    <div class="dcm-row">'
            + (radarrInstances.length > 0
                ? '      <div>'
                + '        <div class="dcm-section-label">Destination Server</div>'
                + '        <select class="dcm-select" id="dcm-server">' + serverOptions + '</select>'
                + '      </div>'
                : '')
            + '      <div>'
            + '        <div class="dcm-section-label">Quality Profile</div>'
            + '        <select class="dcm-select" id="dcm-profile">' + getProfileOptions(firstInstance) + '</select>'
            + '      </div>'
            + '    </div>'
            + '    <div class="dcm-section-label">Root Folder</div>'
            + '    <select class="dcm-select" id="dcm-rootfolder">' + getRootFolderOptions(firstInstance) + '</select>'
            + '  </div>'
            + '  <div class="dcm-status" id="dcm-status"></div>'
            + '  <div class="dcm-footer">'
            + '    <button class="dcm-btn dcm-btn-cancel" id="dcm-cancel">Cancel</button>'
            + '    <button class="dcm-btn dcm-btn-request" id="dcm-submit">Request</button>'
            + '  </div>'
            + '</div>';

        document.body.appendChild(backdrop);

        function close() { if (backdrop.parentNode) backdrop.parentNode.removeChild(backdrop); }

        // Update profile/rootfolder dropdowns when server changes
        var serverSel = backdrop.querySelector('#dcm-server');
        var profileSel = backdrop.querySelector('#dcm-profile');
        var rootSel = backdrop.querySelector('#dcm-rootfolder');
        var statusEl = backdrop.querySelector('#dcm-status');
        var submitBtn = backdrop.querySelector('#dcm-submit');

        if (serverSel) {
            serverSel.addEventListener('change', function() {
                var idx = serverSel.selectedOptions[0].dataset.index;
                var inst = radarrInstances[parseInt(idx, 10)];
                if (profileSel) profileSel.innerHTML = getProfileOptions(inst);
                if (rootSel) rootSel.innerHTML = getRootFolderOptions(inst);
            });
        }

        backdrop.querySelector('#dcm-cancel').addEventListener('click', close);
        backdrop.addEventListener('click', function(e) { if (e.target === backdrop) close(); });

        submitBtn.addEventListener('click', async function() {
            submitBtn.disabled = true;
            statusEl.textContent = 'Submitting request\u2026';
            statusEl.className = 'dcm-status';

            var body = {
                tmdbId: parseInt(tmdbId, 10),
                mediaType: 'movie'
            };
            if (serverSel && serverSel.value) {
                var sId = parseInt(serverSel.value, 10);
                if (!isNaN(sId)) body.serverId = sId;
            }
            if (profileSel && profileSel.value) {
                var pId = parseInt(profileSel.value, 10);
                if (!isNaN(pId)) body.profileId = pId;
            }
            if (rootSel && rootSel.value) body.rootFolder = rootSel.value;

            try {
                var res = await fetch('/UpcomingMovies/jellyseerr/request', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        'X-Emby-Authorization': getJellyfinAuthHeader()
                    },
                    body: JSON.stringify(body)
                });

                if (res.ok) {
                    statusEl.textContent = '\u2713 Requested successfully!';
                    statusEl.className = 'dcm-status success';
                    // Add to in-memory Set so future card builds also show Requested
                    if (!window._jellyseerrRequests) window._jellyseerrRequests = new Set();
                    window._jellyseerrRequests.add(String(tmdbId));
                    var btns = document.querySelectorAll('.btn-request[data-tmdb="' + tmdbId + '"]');
                    btns.forEach(function(btn) {
                        btn.innerHTML = '&#10003; Requested';
                        btn.classList.add('requested');
                        btn.disabled = true;
                    });
                    setTimeout(close, 1600);

                } else {
                    var errData = await res.json().catch(function() { return {}; });
                    statusEl.textContent = 'Error: ' + (errData.error || ('HTTP ' + res.status));
                    statusEl.className = 'dcm-status error';
                    submitBtn.disabled = false;
                }
            } catch (err) {
                statusEl.textContent = 'Network error. Check Jellyseerr is reachable.';
                statusEl.className = 'dcm-status error';
                submitBtn.disabled = false;
            }
        });
    }

    // ─────────────────────────────────────────────
    // 8. CARD BUILDING
    // ─────────────────────────────────────────────

    var PLAY_SVG = '<svg viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg"><path d="M8 5v14l11-7z"/></svg>';

    // Bookmark SVG — outline when not watchlisted, filled when active
    var WL_SVG_OFF = '<svg viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg"><path d="M17 3H7c-1.1 0-2 .9-2 2v16l7-3 7 3V5c0-1.1-.9-2-2-2zm0 15l-5-2.18L7 18V5h10v13z"/></svg>';
    var WL_SVG_ON  = '<svg viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg"><path d="M17 3H7c-1.1 0-2 .9-2 2v16l7-3 7 3V5c0-1.1-.9-2-2-2z"/></svg>';

    // ── Watchlist API helper (uses Jellyfin UserData.Likes = KefinTweaks Watchlist) ──
    // Confirmed from swiparr JellyfinProvider.toggleWatchlist:
    //   POST /Users/{uid}/Items/{id}/Rating?Likes=true  → add to watchlist
    //   POST /Users/{uid}/Items/{id}/Rating?Likes=false → remove from watchlist
    // Header must be 'Authorization' (not 'X-Emby-Authorization') per Jellyfin API spec.
    async function addToWatchlist(jellyfinId) {
        var client = window.ApiClient;
        if (!client) return;
        var server = client.serverAddress ? client.serverAddress() : '';
        var uid    = client.getCurrentUserId ? client.getCurrentUserId() : '';
        var token  = client.accessToken ? client.accessToken() : '';
        if (!uid || !server || !token) return;
        try {
            var res = await fetch(server + '/Users/' + uid + '/Items/' + jellyfinId + '/Rating?Likes=true', {
                method: 'POST',
                headers: { 'Authorization': 'MediaBrowser Token="' + token + '"' }
            });
            if (!res.ok) WARN('[Watchlist] POST Rating?Likes=true failed:', res.status, await res.text());
            else LOG('[Watchlist] Added to watchlist:', jellyfinId);
        } catch (err) {
            WARN('[Watchlist] network error adding to watchlist:', err);
        }
    }

    async function removeFromWatchlist(jellyfinId) {
        var client = window.ApiClient;
        if (!client) return;
        var server = client.serverAddress ? client.serverAddress() : '';
        var uid    = client.getCurrentUserId ? client.getCurrentUserId() : '';
        var token  = client.accessToken ? client.accessToken() : '';
        if (!uid || !server || !token) return;
        try {
            var res = await fetch(server + '/Users/' + uid + '/Items/' + jellyfinId + '/Rating?Likes=false', {
                method: 'POST',
                headers: { 'Authorization': 'MediaBrowser Token="' + token + '"' }
            });
            if (!res.ok) WARN('[Watchlist] POST Rating?Likes=false failed:', res.status, await res.text());
            else LOG('[Watchlist] Removed from watchlist:', jellyfinId);
        } catch (err) {
            WARN('[Watchlist] network error removing from watchlist:', err);
        }
    }

    var SETUP_HTML = '<div class="discover-error">'
        + '&#9888;&#65039; <strong>TMDB API key not configured.</strong><br>'
        + 'Go to <strong>Dashboard &rarr; Plugins &rarr; Upcoming Movies &amp; Recommendations</strong> and enter your TMDB API key.<br>'
        + '</div>';
    // ──── OVERVIEW MODAL ────

    function showOverviewModal(opts) {
        var overlay = document.createElement('div');
        overlay.className = 'htv-modal-overlay';
        
        var modalHtml = '<div class="htv-modal-content">';
        modalHtml += '<button class="htv-modal-close" aria-label="Close">\u00D7</button>';
        
        if (opts.backdropUrl) {
            modalHtml += '<div class="htv-modal-backdrop-wrap"><div class="htv-modal-backdrop" style="background-image: url(\'' + escapeHtml(opts.backdropUrl) + '\');"></div><div class="htv-modal-backdrop-overlay"></div></div>';
        } else {
            modalHtml += '<div class="htv-modal-backdrop-wrap"><div class="htv-modal-backdrop" style="background: #222;"></div><div class="htv-modal-backdrop-overlay"></div></div>';
        }

        modalHtml += '<div class="htv-modal-body">';
        
        if (opts.posterUrl) {
            modalHtml += '<img class="htv-modal-poster" src="' + escapeHtml(opts.posterUrl) + '" alt="Poster" />';
        } else {
            modalHtml += '<div class="htv-modal-poster dc-no-poster">\uD83C\uDFAC</div>';
        }

        modalHtml += '<div class="htv-modal-info">';
        modalHtml += '<h1 class="htv-modal-title">' + escapeHtml(opts.title) + '</h1>';
        if (opts.date) modalHtml += '<div class="htv-modal-date">' + escapeHtml(opts.date) + '</div>';

        // Rating badges — Jellyfin/TMDB always shown; IMDB and RT loaded async
        var tmdbScore = opts.voteAverage ? opts.voteAverage.toFixed(1) : '—';
        modalHtml += '<div class="htv-ratings-row" id="htv-ratings-' + opts.tmdbId + '">'
            + '<span class="htv-rating-badge rt-fresh" id="htv-rt-' + opts.tmdbId + '" title="Rotten Tomatoes">🍅 <span>—</span></span>'
            + '<span class="htv-rating-badge imdb clickable" id="htv-imdb-' + opts.tmdbId + '" title="IMDb">⭐ <span>—</span></span>'
            + '<span class="htv-rating-badge jellyfin" title="Jellyfin / TMDB community rating">🎬 <span>' + tmdbScore + '</span></span>'
            + '</div>';

        var overviewText = opts.overview || 'No overview available.';
        modalHtml += '<div class="htv-modal-overview">' + escapeHtml(overviewText) + '</div>';

        // Actions
        var actionsHtml = '';
        var isRequested = (window._jellyseerrRequests && window._jellyseerrRequests.has(String(opts.tmdbId))) || false;

        var existingBtn = document.querySelector('.btn-request[data-tmdb="' + opts.tmdbId + '"]');
        if (isRequested || (existingBtn && existingBtn.classList.contains('requested'))) {
            actionsHtml += '<button class="jellyseerr-request-button btn-request requested" data-tmdb="' + opts.tmdbId + '" disabled>&#10003; Requested</button>';
        } else {
            actionsHtml += '<button class="jellyseerr-request-button btn-request" data-tmdb="' + opts.tmdbId + '">Request</button>';
        }

        if (!opts.isUpcoming && opts.streamBaseUrl) {
            actionsHtml += '<button class="btn-stream" data-stream-url="' + opts.streamBaseUrl + '/movie/' + opts.tmdbId + '">Stream</button>';
        }

        modalHtml += '<div class="htv-modal-actions">' + actionsHtml + '</div>';

        // Actor section placeholder — filled asynchronously after modal mounts
        modalHtml += '<div id="htv-actors-' + opts.tmdbId + '" style="margin-top:16px;"></div>';

        modalHtml += '</div></div></div>';
        overlay.innerHTML = modalHtml;
        document.body.appendChild(overlay);

        // ── Async: load actor credits and render round avatars ──
        if (opts.tmdbId) {
            var credCacheKey = 'credits_' + opts.tmdbId;
            if (!window._creditsCache) window._creditsCache = {};
            function renderActors(cast) {
                if (!cast || cast.length === 0) return;
                var actorsEl = document.getElementById('htv-actors-' + opts.tmdbId);
                if (!actorsEl) return;
                var topCast = cast.slice(0, 8);
                var html = '<div style="display:flex;gap:12px;flex-wrap:wrap;margin-top:4px;">';
                topCast.forEach(function(a) {
                    var imgUrl = a.profile_path ? 'https://image.tmdb.org/t/p/w185' + a.profile_path : null;
                    var avatarHtml = imgUrl
                        ? '<img src="' + escapeHtml(imgUrl) + '" alt="' + escapeHtml(a.name) + '" style="width:44px;height:44px;border-radius:50%;object-fit:cover;background:#222;"/>'
                        : '<div style="width:44px;height:44px;border-radius:50%;background:#333;display:flex;align-items:center;justify-content:center;color:#888;font-size:18px;">&#128100;</div>';
                    html += '<div style="display:flex;flex-direction:column;align-items:center;gap:4px;width:54px;">'
                        + avatarHtml
                        + '<span style="font-size:0.68em;color:#bbb;text-align:center;line-height:1.2;word-break:break-word;">' + escapeHtml(a.name) + '</span>'
                        + '</div>';
                });
                html += '</div>';
                actorsEl.innerHTML = html;
            }
            if (window._creditsCache[credCacheKey] !== undefined) {
                renderActors(window._creditsCache[credCacheKey]);
            } else {
                fetch('/UpcomingMovies/tmdb/credits?tmdbId=' + opts.tmdbId, {
                    headers: { 'X-Emby-Authorization': getJellyfinAuthHeader() }
                }).then(function(r) { return r.ok ? r.json() : null; }).then(function(data) {
                    var cast = data && data.cast ? data.cast : null;
                    window._creditsCache[credCacheKey] = cast;
                    renderActors(cast);
                }).catch(function() { window._creditsCache[credCacheKey] = null; });
            }
        }

        // Async fetch ratings to fill in IMDB and RT badges
        if (opts.tmdbId) {
            var cacheKey = String(opts.tmdbId);
            if (!window._ratingsCache) window._ratingsCache = {};
            var cachedRatings = window._ratingsCache[cacheKey];

            function applyRatings(data) {
                if (!data) return;
                var rtEl = document.getElementById('htv-rt-' + opts.tmdbId);
                if (rtEl) {
                    var rtSpan = rtEl.querySelector('span');
                    if (rtSpan) rtSpan.textContent = data.rtScore || '—';
                }
                var imdbEl = document.getElementById('htv-imdb-' + opts.tmdbId);
                if (imdbEl) {
                    var imdbSpan = imdbEl.querySelector('span');
                    if (imdbSpan) imdbSpan.textContent = data.imdbRating ? (data.imdbRating + '/10') : '—';
                    if (data.imdbId) {
                        imdbEl.style.cursor = 'pointer';
                        imdbEl.addEventListener('click', function() {
                            window.open('https://www.imdb.com/title/' + data.imdbId + '/', '_blank', 'noopener');
                        });
                    }
                }
                var jfEl = document.querySelector('#htv-ratings-' + opts.tmdbId + ' .jellyfin span');
                if (jfEl && data.tmdbRating) jfEl.textContent = data.tmdbRating.toFixed(1);
            }

            if (cachedRatings !== undefined) {
                // Use cached result immediately (no extra API call)
                applyRatings(cachedRatings);
            } else {
                fetch('/UpcomingMovies/tmdb/ratings?tmdbId=' + opts.tmdbId, {
                    headers: { 'X-Emby-Authorization': getJellyfinAuthHeader() }
                }).then(function(r) { return r.ok ? r.json() : null; }).then(function(data) {
                    window._ratingsCache[cacheKey] = data; // cache whether data or null
                    applyRatings(data);
                }).catch(function() { window._ratingsCache[cacheKey] = null; });
            }
        }


        // Force reflow and transition
        window.getComputedStyle(overlay).opacity;
        overlay.classList.add('show');

        var closeFunc = function(e) {
            if (e && e.target !== overlay && !e.target.closest('.htv-modal-close')) return;
            overlay.classList.remove('show');
            setTimeout(function() { if (overlay.parentNode) overlay.parentNode.removeChild(overlay); }, 300);
        };
        overlay.addEventListener('click', closeFunc);

        // Request button inside overview modal
        var modalReqBtn = overlay.querySelector('.btn-request:not(.requested)');
        if (modalReqBtn) {
            modalReqBtn.addEventListener('click', function(e) {
                e.stopPropagation();
                overlay.classList.remove('show');
                setTimeout(function() {
                    if (overlay.parentNode) overlay.parentNode.removeChild(overlay);
                    openRequestModal(String(opts.tmdbId), opts.title, opts.backdropUrl);
                }, 50);
            });
        }

        // Stream button inside overview modal → open stream info modal
        var modalStreamBtn = overlay.querySelector('.btn-stream[data-stream-url]');
        if (modalStreamBtn) {
            modalStreamBtn.addEventListener('click', function(e) {
                e.stopPropagation();
                var sUrl = e.currentTarget.dataset.streamUrl;
                overlay.classList.remove('show');
                setTimeout(function() {
                    if (overlay.parentNode) overlay.parentNode.removeChild(overlay);
                    showStreamModal({ title: opts.title, posterUrl: opts.posterUrl, backdropUrl: opts.backdropUrl, streamUrl: sUrl });
                }, 50);
            });
        }
    }

    // ── MODAL UTILITIES ──

    // Dismiss any open overlay or request modal to prevent stacking
    function closeAnyOpenModal() {
        document.querySelectorAll('.htv-modal-overlay, .dcm-backdrop').forEach(function(el) {
            if (el.parentNode) el.parentNode.removeChild(el);
        });
    }

    // Stream info modal — centered overlay with poster + Stream Now button
    function showStreamModal(opts) {
        closeAnyOpenModal();
        var overlay = document.createElement('div');
        overlay.className = 'htv-modal-overlay';
        var mHtml = '<div class="htv-modal-content" style="max-width:460px;">';
        mHtml += '<button class="htv-modal-close" aria-label="Close">\u00D7</button>';
        mHtml += opts.backdropUrl
            ? '<div class="htv-modal-backdrop-wrap"><div class="htv-modal-backdrop" style="background-image:url(\'' + escapeHtml(opts.backdropUrl) + '\');"></div><div class="htv-modal-backdrop-overlay"></div></div>'
            : '<div class="htv-modal-backdrop-wrap"><div class="htv-modal-backdrop" style="background:#222;"></div><div class="htv-modal-backdrop-overlay"></div></div>';
        mHtml += '<div class="htv-modal-body" style="flex-direction:column;align-items:center;text-align:center;margin-top:80px;">';
        if (opts.posterUrl) mHtml += '<img class="htv-modal-poster" src="' + escapeHtml(opts.posterUrl) + '" alt="Poster" style="width:110px;margin-bottom:16px;" />';
        mHtml += '<div class="htv-modal-info" style="align-items:center;width:100%;">';
        mHtml += '<h2 class="htv-modal-title" style="font-size:1.4em;text-align:center;">' + escapeHtml(opts.title) + '</h2>';
        mHtml += '<p style="color:#aaa;font-size:0.9em;margin:8px 0 20px;">Available to stream on your service</p>';
        mHtml += '<div class="htv-modal-actions" style="justify-content:center;">';
        mHtml += '<a href="' + escapeHtml(opts.streamUrl) + '" target="_blank" rel="noopener" style="padding:12px 36px;background:#00C853!important;border-color:#00C853!important;color:#000!important;font-weight:700;border-radius:8px;text-decoration:none;font-size:15px;display:inline-block;">&#9654; Stream Now</a>';
        mHtml += '</div></div></div></div>';
        overlay.innerHTML = mHtml;
        document.body.appendChild(overlay);
        window.getComputedStyle(overlay).opacity;
        overlay.classList.add('show');
        overlay.addEventListener('click', function(e) {
            if (e.target === overlay || e.target.closest('.htv-modal-close')) {
                overlay.classList.remove('show');
                setTimeout(function() { if (overlay.parentNode) overlay.parentNode.removeChild(overlay); }, 300);
            }
        });
    }

    var STAR_SVG = '<svg viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg"><path d="M12 2l3.09 6.26L22 9.27l-5 4.87 1.18 6.88L12 17.77l-6.18 3.25L7 14.14 2 9.27l6.91-1.01L12 2z"/></svg>';

    /**
     * Build a card element.
     * @param {Object} opts
     *   tmdbId, title, posterUrl, backdropUrl, date, streamBaseUrl,
     *   isUpcoming {bool}
     *   jellyfinId {string}
     *   voteAverage {number}
     *   overview {string}
     */
    function buildCard(opts) {
        var tmdbId       = opts.tmdbId;
        var title        = opts.title || '';
        var posterUrl    = opts.posterUrl;
        var backdropUrl  = opts.backdropUrl;
        var date         = opts.date;
        var overview     = opts.overview;
        var streamBaseUrl = opts.streamBaseUrl || '';
        var isUpcoming   = !!opts.isUpcoming;
        var isAvailable  = !!opts.isAvailable;
        var jellyfinId   = opts.jellyfinId;
        var vote         = opts.voteAverage ? opts.voteAverage.toFixed(1) : '';

        var isWatchlisted = !!opts.isWatchlisted;

        var card = document.createElement('div');
        card.className = 'discover-card' + (isUpcoming ? ' upcoming-card' : '');
        // Only natively linkable items get hover scale
        if (!isUpcoming && isAvailable) card.className += ' hover-enabled';

        var actionsHtml = '';
        var playHtml = '';

        if (!isUpcoming) {
            if (isAvailable && jellyfinId) {
                // Play button (action bar) + Jellyfin-style animated overlay button
                actionsHtml += '<button class="btn-play" data-jellyfin="' + jellyfinId + '">Play</button>';
                playHtml = '<div class="dc-overlay">'
                    + '<button is="emby-button" type="button" class="dc-jellyfin-play-btn" tabindex="-1" aria-label="Play">'
                    + '<span class="material-icons">play_circle</span>'
                    + '</button></div>';
            } else if (tmdbId) {
                var alreadyReq = window._jellyseerrRequests && window._jellyseerrRequests.has(String(tmdbId));
                if (alreadyReq) {
                    actionsHtml += '<button class="jellyseerr-request-button btn-request requested" data-tmdb="' + tmdbId + '" disabled>&#10003; Requested</button>';
                } else {
                    actionsHtml += '<button class="jellyseerr-request-button btn-request" data-tmdb="' + tmdbId + '">Request</button>';
                }
                if (streamBaseUrl) {
                    actionsHtml += '<button class="btn-stream" data-stream-url="' + streamBaseUrl + '/movie/' + tmdbId + '">Stream</button>';
                }
            }
        } else if (tmdbId) {
            // Upcoming: Just Request block button
            var alreadyReqUp = window._jellyseerrRequests && window._jellyseerrRequests.has(String(tmdbId));
            if (alreadyReqUp) {
                actionsHtml += '<button class="jellyseerr-request-button btn-request requested" data-tmdb="' + tmdbId + '" disabled>&#10003; Requested</button>';
            } else {
                actionsHtml += '<button class="jellyseerr-request-button btn-request" data-tmdb="' + tmdbId + '">Request</button>';
            }
        }

        var posterHtml = posterUrl
            ? '<img src="' + escapeHtml(posterUrl) + '" alt="' + escapeHtml(title) + '" loading="lazy" />'
            : '<div class="dc-no-poster">\uD83C\uDFAC</div>';

        var badgeHtml = vote && vote > 0 ? '<div class="dc-star-badge">' + STAR_SVG + vote + '</div>' : '';

        // Bookmark banner — only for available movies (jellyfinId known)
        var watchlistHtml = '';
        if (!isUpcoming && isAvailable && jellyfinId) {
            watchlistHtml = '<button class="dc-watchlist-btn' + (isWatchlisted ? ' active' : '') + '"'
                + ' title="' + (isWatchlisted ? 'Remove from Watchlist' : 'Add to Watchlist') + '"'
                + ' data-jellyfin-id="' + jellyfinId + '">'
                + (isWatchlisted ? WL_SVG_ON : WL_SVG_OFF)
                + '</button>';
        }

        card.innerHTML =
            '<div class="dc-poster">'
            + posterHtml
            + badgeHtml
            + (!isUpcoming ? '<button class="dc-dismiss-btn" title="Not interested" aria-label="Dismiss">&#x2715;</button>' : '')
            + watchlistHtml
            + playHtml
            + '</div>'
            + '<div class="dc-title" title="' + escapeHtml(title) + '">' + escapeHtml(title) + '</div>'
            + (isUpcoming && date ? '<div class="dc-date">' + date + '</div>' : '')
            + (actionsHtml ? '<div class="dc-action-bar">' + actionsHtml + '</div>' : '');

        // Poster Click Routing - ALWAYS open the overview modal. The Play button handles direct routing.
        card.querySelector('.dc-poster').addEventListener('click', function(e) {
            showOverviewModal(opts);
        });

        // Action Bar Play Route (Mobile/Direct)
        var btnPlay = card.querySelector('.btn-play');
        if (btnPlay && jellyfinId) {
            btnPlay.addEventListener('click', function(e) {
                e.stopPropagation();
                window.location.hash = '#/details?id=' + jellyfinId;
            });
        }

        // Overlay Play Route
        var btnOverlayPlay = card.querySelector('.dc-jellyfin-play-btn');
        if (btnOverlayPlay && jellyfinId) {
            btnOverlayPlay.addEventListener('click', function(e) {
                e.stopPropagation();
                window.location.hash = '#/details?id=' + jellyfinId;
            });
        }

        // Request button → Jellyseerr modal
        var btnReq = card.querySelector('.btn-request');
        if (btnReq) btnReq.addEventListener('click', function(e) {
            e.stopPropagation();
            openRequestModal(e.currentTarget.dataset.tmdb, title, backdropUrl);
        });

        // Watchlist bookmark toggle
        var btnWL = card.querySelector('.dc-watchlist-btn');
        if (btnWL) {
            btnWL.addEventListener('click', async function(e) {
                e.stopPropagation();
                var isNowActive = btnWL.classList.contains('active');
                if (isNowActive) {
                    // Toggle off — remove from watchlist
                    btnWL.classList.remove('active');
                    btnWL.innerHTML = WL_SVG_OFF;
                    btnWL.title = 'Add to Watchlist';
                    await removeFromWatchlist(jellyfinId);
                } else {
                    // Toggle on — add to watchlist
                    btnWL.classList.add('active');
                    btnWL.innerHTML = WL_SVG_ON;
                    btnWL.title = 'Remove from Watchlist';
                    await addToWatchlist(jellyfinId);
                }
            });
        }

        // Stream button → stream info modal
        var btnStream = card.querySelector('.btn-stream');
        if (btnStream) btnStream.addEventListener('click', function(e) {
            e.stopPropagation();
            showStreamModal({ title: title, posterUrl: posterUrl, backdropUrl: backdropUrl, streamUrl: e.currentTarget.dataset.streamUrl });
        });

        // Dismiss button — only wired for recommendation cards
        var btnDismiss = card.querySelector('.dc-dismiss-btn');
        if (btnDismiss && !isUpcoming) {
            btnDismiss.addEventListener('click', async function(e) {
                e.stopPropagation();
                // Animate out
                card.style.transition = 'opacity 0.25s, transform 0.25s';
                card.style.opacity = '0';
                card.style.transform = 'scale(0.85)';
                // Record dismiss with profile service
                try {
                    var dClient = window.ApiClient;
                    var dUserId = dClient && dClient.getCurrentUserId ? dClient.getCurrentUserId() : '';
                    // Send genre IDs if we have them from the card data (opts.genreIds)
                    var gParam = opts.genreIds && opts.genreIds.length ? '&genreIds=' + encodeURIComponent(opts.genreIds.join(',')) : '';
                    if (dUserId && tmdbId) {
                        await fetch('/UpcomingMovies/tmdb/dismiss?userId=' + encodeURIComponent(dUserId) + '&tmdbId=' + tmdbId + gParam, {
                            method: 'POST',
                            headers: { 'X-Emby-Authorization': getJellyfinAuthHeader() }
                        });
                    }
                } catch(err) { WARN('Dismiss API error:', err); }
                // Remove from DOM after animation
                setTimeout(function() {
                    if (card.parentNode) card.parentNode.removeChild(card);
                }, 280);
            });
        }

        return card;
    }

    // ─────────────────────────────────────────────
    // 9. RENDERING
    // ─────────────────────────────────────────────

    function renderTmdbCards(movies, containerEl, streamBaseUrl, isUpcoming, isAppend) {
        if (!isAppend) containerEl.innerHTML = '';
        if (!movies || movies.length === 0) {
            if (!isAppend) containerEl.innerHTML = '<div class="discover-loading">No items found.</div>';
            return;
        }
        // Deduplicate by TMDB ID on the frontend — guards against parallel-source overlap
        var seenIds = new Set();
        movies = movies.filter(function(m) {
            if (!m || !m.id) return false;
            var id = String(m.id);
            if (seenIds.has(id)) return false;
            seenIds.add(id);
            return true;
        });
        movies.forEach(function(movie) {
            var posterUrl   = movie.poster_path   ? TMDB_IMAGE_BASE + movie.poster_path   : null;
            var backdropUrl = movie.backdrop_path ? 'https://image.tmdb.org/t/p/w780' + movie.backdrop_path : null;
            containerEl.appendChild(buildCard({
                tmdbId:       movie.id,
                title:        movie.title,
                posterUrl:    posterUrl,
                backdropUrl:  backdropUrl,
                date:         movie.release_date || null,
                overview:     movie.overview,
                streamBaseUrl: streamBaseUrl,
                isUpcoming:   !!isUpcoming,
                isAvailable:  movie.isAvailable,
                jellyfinId:   movie.jellyfinId,
                voteAverage:  movie.vote_average,
                isWatchlisted: !!movie.isWatchlisted,
                genreIds:     movie.genre_ids || []   // for dismiss genre penalization
            }));
        });

    }

    // ─────────────────────────────────────────────
    // 10. BOOT
    // ─────────────────────────────────────────────

    // Load all Jellyseerr requests and cache them as a Set of string TMDB IDs
    // Backend returns an integer array (HashSet<int> of TMDB IDs)
    async function fetchAndCacheJellyseerrRequests() {
        if (window._jellyseerrRequestsFetched) return;
        window._jellyseerrRequestsFetched = true;
        try {
            var res = await fetch('/UpcomingMovies/jellyseerr/requests', {
                headers: { 'X-Emby-Authorization': getJellyfinAuthHeader() }
            });
            if (res.ok) {
                var data = await res.json();
                var s = new Set();
                var items = Array.isArray(data) ? data : (data.results || []);
                items.forEach(function(item) {
                    var id = typeof item === 'number' ? item : (item.tmdbId || (item.media && item.media.tmdbId));
                    if (id) s.add(String(id));
                });
                window._jellyseerrRequests = s;
                LOG('Jellyseerr requests cached:', s.size, 'items');
                // Refresh any already-rendered request buttons so they reflect correct state
                document.querySelectorAll('.btn-request:not(.requested)').forEach(function(btn) {
                    var tid = btn.dataset.tmdb;
                    if (tid && s.has(String(tid))) {
                        btn.className = 'jellyseerr-request-button btn-request requested';
                        btn.disabled = true;
                        btn.innerHTML = '&#10003; Requested';
                    }
                });
            }
        } catch(e) { WARN('Failed to cache Jellyseerr requests:', e); }
    }

    var _renderingContainers = new Set();

    async function populateDiscoverContainer(containerDiv) {
        if (_renderingContainers.has(containerDiv)) return;
        _renderingContainers.add(containerDiv);
        LOG('Populating Discover container…');

        if (!containerDiv.querySelector('.discover-page-content')) {
            containerDiv.innerHTML = getGridTemplate();
        }


        var config = await fetchPluginConfig();
        var streamBaseUrl = config.streamBaseUrl || '';

        // Section visibility
        var secUpcoming = containerDiv.querySelector('[data-section="upcoming"]');
        var secRec      = containerDiv.querySelector('[data-section="recommended"]');
        if (secUpcoming) secUpcoming.classList.toggle('discover-hidden', !config.showUpcoming);
        if (secRec)      secRec.classList.toggle('discover-hidden', !config.showRecommendations);

        var rowUpcoming = containerDiv.querySelector('[data-row="upcoming"]');
        var rowRec      = containerDiv.querySelector('[data-row="recommended"]');

        // Init navigation for each row
        if (rowUpcoming) initRowNavigation(rowUpcoming);
        if (rowRec)      initRowNavigation(rowRec);

        // Pre-warm caches while data fetches run in parallel
        setTimeout(function() { _fetchRadarrCached(); }, 50);
        setTimeout(function() { fetchAndCacheJellyseerrRequests(); }, 100);

        function isSetup(v) { return v && v._needsSetup; }

        var results = await Promise.all([
            config.showUpcoming        ? fetchUpcoming(_upcFilters).catch(function(e)           { ERR('fetchUpcoming:', e);      return null; }) : null,
            config.showRecommendations ? fetchRecommendations(1, _recFilters).catch(function(e) { ERR('fetchRecommendations:', e); return null; }) : null
        ]);
        var upc = results[0], rec = results[1];

        // Fetch Jellyfin library map so we know which items are available
        var client = window.ApiClient;
        var userId = client && client.getCurrentUserId();
        var tmdbMap = {};

        if (userId && config.showRecommendations && rec && rec.results) {
            try {
                var server = client._serverAddress;
                var token  = client.accessToken();
                var allRes = await fetch(
                    server + '/Users/' + userId + '/Items?IncludeItemTypes=Movie&Recursive=true&Fields=ProviderIds,UserData',
                    { headers: { 'X-Emby-Token': token } }
                );
                if (allRes.ok) {
                    var allData = await allRes.json();
                    (allData.Items || []).forEach(function(item) {
                        var tid = item.ProviderIds && item.ProviderIds.Tmdb ? parseInt(item.ProviderIds.Tmdb, 10) : null;
                        if (tid) {
                            tmdbMap[tid] = {
                                id: item.Id,
                                played: item.UserData && item.UserData.Played,
                                isWatchlisted: !!(item.UserData && item.UserData.Likes)
                            };
                        }
                    });

                    // Filter out already watched movies from recommendations
                    var validRecs = [];
                    for(var i=0; i<rec.results.length; i++) {
                        var m = rec.results[i];
                        var localInfo = tmdbMap[m.id];
                        if (localInfo && localInfo.played) continue; // Hide completely
                        if (localInfo) {
                            m.isAvailable = true;
                            m.jellyfinId = localInfo.id;
                            m.isWatchlisted = localInfo.isWatchlisted;
                        }
                        validRecs.push(m);
                    }
                    rec.results = validRecs;
                }
            } catch (err) {
                WARN('Failed to check Jellyfin library availability:', err);
            }
        }

        if (rowUpcoming) {
            if (isSetup(upc)) { rowUpcoming.innerHTML = SETUP_HTML; }
            else if (upc)     { renderTmdbCards(upc.results, rowUpcoming, streamBaseUrl, true); }
            else              { rowUpcoming.innerHTML = '<div class="discover-error">Failed to load. Check browser console.</div>'; }
        }

        var btnMore = containerDiv.querySelector('.btn-discover-more');
        var _tmdbRecBuffer = [];
        var _tmdbRecPage = 1;
        var _tmdbRecTotalPages = 1;
        // Tracks every TMDB ID already rendered in recommendations
        var _renderedRecIds = new Set();
        // Tracks every TMDB ID currently in the buffer (prevents cross-page dups from entering buffer)
        var _bufferedRecIds = new Set();

        // ── Wire filter panel interactions (after all vars are in scope) ──
        // Pill toggle (language / genre)
        containerDiv.querySelectorAll('.df-pill').forEach(function(pill) {
            pill.addEventListener('click', function() {
                var filterType = pill.dataset.filter;
                var val        = pill.dataset.val;
                var panelEl    = pill.closest('[data-panel]');
                var sid        = panelEl ? panelEl.dataset.panel : null;
                if (!sid) return;
                var f = sid === 'upcoming' ? _upcFilters : _recFilters;
                if (filterType === 'lang') {
                    var idx = f.languages.indexOf(val);
                    if (idx !== -1) { f.languages.splice(idx, 1); pill.classList.remove('active'); }
                    else            { f.languages.push(val);       pill.classList.add('active'); }
                } else if (filterType === 'genre') {
                    var gid = parseInt(val, 10);
                    var gi  = f.genres.indexOf(gid);
                    if (gi !== -1) { f.genres.splice(gi, 1); pill.classList.remove('active'); }
                    else           { f.genres.push(gid);      pill.classList.add('active'); }
                }
            });
        });
        // Release-type checkboxes
        containerDiv.querySelectorAll('input[data-filter="rt"]').forEach(function(cb) {
            cb.addEventListener('change', function() {
                var val = parseInt(cb.dataset.val, 10);
                var idx = _upcFilters.releaseTypes.indexOf(val);
                if (cb.checked) { if (idx === -1) _upcFilters.releaseTypes.push(val); }
                else             { if (idx !== -1) _upcFilters.releaseTypes.splice(idx, 1); }
            });
        });
        // Date inputs
        containerDiv.querySelectorAll('input[data-filter="dateFrom"]').forEach(function(el) {
            el.addEventListener('change', function() { _upcFilters.dateFrom = el.value; });
        });
        containerDiv.querySelectorAll('input[data-filter="dateTo"]').forEach(function(el) {
            el.addEventListener('change', function() { _upcFilters.dateTo = el.value; });
        });
        // Toggle buttons
        containerDiv.querySelectorAll('[data-toggle-filter]').forEach(function(btn) {
            btn.addEventListener('click', function() {
                var sid   = btn.dataset.toggleFilter;
                var panel = containerDiv.querySelector('[data-panel="' + sid + '"]');
                if (!panel) return;
                var isOpen = panel.classList.toggle('show');
                btn.classList.toggle('active', isOpen);
                btn.innerHTML = 'Filters ' + (isOpen ? '&#9650;' : '&#9660;');
            });
        });
        // Reset buttons — update pill/checkbox state WITHOUT replacing the DOM
        // (replacing DOM causes a duplicate listener bug on the toggle button)
        containerDiv.querySelectorAll('[data-reset]').forEach(function(resetBtn) {
            resetBtn.addEventListener('click', function() {
                var sid = resetBtn.dataset.reset;
                if (sid === 'upcoming') {
                    _upcFilters.languages    = FILTER_LANGS.map(function(l){ return l.code; });
                    _upcFilters.genres       = [];
                    _upcFilters.releaseTypes = [1,2,3,4,5];
                    _upcFilters.dateFrom     = _todayStr();
                    _upcFilters.dateTo       = _oneYearStr();
                } else {
                    _recFilters.languages = [];
                    _recFilters.genres    = [];
                }
                var f = sid === 'upcoming' ? _upcFilters : _recFilters;
                // Update pill active states
                var panel = containerDiv.querySelector('[data-panel="' + sid + '"]');
                if (panel) {
                    panel.querySelectorAll('.df-pill[data-filter="lang"]').forEach(function(p) {
                        var isActive = f.languages.indexOf(p.dataset.val) !== -1;
                        p.classList.toggle('active', isActive);
                    });
                    panel.querySelectorAll('.df-pill[data-filter="genre"]').forEach(function(p) {
                        p.classList.remove('active'); // genres default = none
                    });
                    // Restore checkboxes (upcoming only)
                    panel.querySelectorAll('input[data-filter="rt"]').forEach(function(cb) {
                        cb.checked = _upcFilters.releaseTypes.indexOf(parseInt(cb.dataset.val, 10)) !== -1;
                    });
                    // Restore date inputs
                    panel.querySelectorAll('input[data-filter="dateFrom"]').forEach(function(el) { el.value = _upcFilters.dateFrom; });
                    panel.querySelectorAll('input[data-filter="dateTo"]').forEach(function(el) { el.value = _upcFilters.dateTo; });
                }
            });
        });
        // Apply buttons
        containerDiv.querySelectorAll('[data-apply]').forEach(function(applyBtn) {
            applyBtn.addEventListener('click', async function() {
                var sid = applyBtn.dataset.apply;
                applyBtn.textContent = 'Applying...';
                applyBtn.disabled = true;
                try {
                    if (sid === 'upcoming' && rowUpcoming) {
                        rowUpcoming.innerHTML = '<div class="discover-loading">Filtering&hellip;</div>';
                        var uNew = await fetchUpcoming(_upcFilters);
                        if (isSetup(uNew)) rowUpcoming.innerHTML = SETUP_HTML;
                        else if (uNew)     renderTmdbCards(uNew.results, rowUpcoming, streamBaseUrl, true);
                        else               rowUpcoming.innerHTML = '<div class="discover-error">Failed to load.</div>';
                    } else if (sid === 'recommended' && rowRec) {
                        rowRec.innerHTML = '<div class="discover-loading">Filtering&hellip;</div>';
                        // Full reset of recommendation state
                        _tmdbRecBuffer = []; _tmdbRecPage = 2; _renderedRecIds.clear(); _bufferedRecIds.clear(); _tmdbRecTotalPages = 50;
                        var rNew = await fetchRecommendations(1, _recFilters);
                        if (isSetup(rNew)) { rowRec.innerHTML = SETUP_HTML; }
                        else if (rNew && rNew.results) {
                            // Seed buffer with page-1 results, then fill to 3 rows using ensureRecommendationsBuffer
                            Array.prototype.push.apply(_tmdbRecBuffer, rNew.results);
                            var cw2 = rowRec.clientWidth || 1000;
                            var c2  = Math.max(1, Math.floor((cw2 + 24) / (150 + 24)));
                            var rTarget = c2 * 3;
                            await ensureRecommendationsBuffer(rTarget);
                            // Enrich with library info and dedup
                            var rChunk = _tmdbRecBuffer.splice(0, rTarget).filter(function(m) {
                                if (!m || !m.id) return false;
                                if (_renderedRecIds.has(String(m.id))) return false;
                                _renderedRecIds.add(String(m.id));
                                var li = tmdbMap[m.id];
                                if (li && li.played) return false;
                                if (li) { m.isAvailable=true; m.jellyfinId=li.id; m.isWatchlisted=li.isWatchlisted; }
                                return true;
                            });
                            renderTmdbCards(rChunk, rowRec, streamBaseUrl, false, false);
                        } else {
                            rowRec.innerHTML = '<div class="discover-error">Failed to load.</div>';
                        }
                    }
                    // Auto-close the filter panel after successful apply
                    var panel = containerDiv.querySelector('[data-panel="' + sid + '"]');
                    var toggleBtn = containerDiv.querySelector('[data-toggle-filter="' + sid + '"]');
                    if (panel)     { panel.classList.remove('show'); }
                    if (toggleBtn) { toggleBtn.classList.remove('active'); toggleBtn.innerHTML = 'Filters &#9660;'; }
                } catch(err) { ERR('Apply filter error:', err); }
                finally { applyBtn.textContent = 'Apply Filters'; applyBtn.disabled = false; }
            });
        });
        // ── End filter wiring ───────────────────────────────

        async function ensureRecommendationsBuffer(targetCount) {
            // Fetch until buffer has enough items AFTER dedup, or we've exhausted all pages.
            // _bufferedRecIds tracks IDs in the buffer (cross-page dedup).
            // _renderedRecIds tracks IDs already shown on screen (initial-load dedup).
            var fetchTarget = targetCount * 3;
            while (_tmdbRecBuffer.length < fetchTarget && _tmdbRecPage <= _tmdbRecTotalPages) {
                var raw = await fetchRecommendations(_tmdbRecPage, _recFilters);
                if (!raw || !raw.results) break;
                if (_tmdbRecPage === 2) _tmdbRecTotalPages = raw.total_pages || 50;

                var valid = [];
                for(var j=0; j<raw.results.length; j++) {
                    var m = raw.results[j];
                    if (!m || !m.id) continue;
                    var sid = String(m.id);
                    if (_renderedRecIds.has(sid))  continue; // already on screen
                    if (_bufferedRecIds.has(sid))   continue; // already in buffer (cross-page dup)
                    _bufferedRecIds.add(sid);
                    var info = tmdbMap[m.id];
                    if (info && info.played) continue;
                    if (info) { m.isAvailable = true; m.jellyfinId = info.id; m.isWatchlisted = info.isWatchlisted; }
                    valid.push(m);
                }
                Array.prototype.push.apply(_tmdbRecBuffer, valid);
                _tmdbRecPage++;

                if (_tmdbRecBuffer.length >= fetchTarget) break;
            }
        }


        if (rowRec) {
            if (isSetup(rec)) { rowRec.innerHTML = SETUP_HTML; }
            else if (rec) {
                // Determine target count (cols * 3) — always a full multiple of cols so last row is complete
                var containerWidth = rowRec.clientWidth || 1000;
                var cols = Math.floor((containerWidth + 24) / (150 + 24));
                if (cols < 1) cols = 1;
                var targetCount = cols * 3; // exactly 3 complete rows

                // Load initial chunk
                _tmdbRecPage = 2; // already fetched page 1 in rec
                _tmdbRecTotalPages = rec.total_pages || 1;
                // Seed buffer and _bufferedRecIds from initial page-1 results
                rec.results.forEach(function(m) {
                    if (m && m.id) {
                        _bufferedRecIds.add(String(m.id));
                        _tmdbRecBuffer.push(m);
                    }
                });

                await ensureRecommendationsBuffer(targetCount);

                // Round up to a full row so the last row isn't incomplete
                var availableCount = _tmdbRecBuffer.length;
                var fullRowCount = Math.floor(availableCount / cols) * cols;
                var sliceCount = fullRowCount > 0 ? Math.min(fullRowCount, targetCount) : Math.min(targetCount, availableCount);
                var chunk = _tmdbRecBuffer.splice(0, sliceCount);
                // Filter out any IDs already shown (shouldn't happen on first load, but be safe)
                chunk = chunk.filter(function(m) {
                    if (!m || !m.id) return false;
                    var id = String(m.id);
                    if (_renderedRecIds.has(id)) return false;
                    _renderedRecIds.add(id);
                    return true;
                });
                renderTmdbCards(chunk, rowRec, streamBaseUrl, false, false);

                if (btnMore) {
                    btnMore.style.display = 'inline-block';
                    btnMore.addEventListener('click', async function() {
                        btnMore.textContent = 'Loading...';
                        btnMore.disabled = true;
                        try {
                            var cw = rowRec.clientWidth || 1000;
                            var dynCols = Math.floor((cw + 24) / (150 + 24));
                            if (dynCols < 1) dynCols = 1;
                            var dynTargetCount = dynCols * 3;

                            await ensureRecommendationsBuffer(dynTargetCount);

                            // Round down to full rows so last row is always complete
                            var dynAvail = _tmdbRecBuffer.length;
                            var dynFullRows = Math.floor(dynAvail / dynCols) * dynCols;
                            var sc = dynFullRows > 0 ? Math.min(dynFullRows, dynTargetCount) : Math.min(dynTargetCount, dynAvail);
                            var nextChunk = _tmdbRecBuffer.splice(0, sc);
                            // Deduplicate against everything already rendered in this session
                            nextChunk = nextChunk.filter(function(m) {
                                if (!m || !m.id) return false;
                                var id = String(m.id);
                                if (_renderedRecIds.has(id)) return false;
                                _renderedRecIds.add(id);
                                return true;
                            });
                            if (nextChunk.length > 0) {
                                renderTmdbCards(nextChunk, rowRec, streamBaseUrl, false, true);
                            }

                            // ── Infinite scroll: never hide the button ──────────────────
                            // When the backend page cycle is exhausted, wrap back to page 1
                            // and clear the _renderedRecIds for a fresh cycle so the user
                            // keeps getting movies indefinitely.
                            if (_tmdbRecBuffer.length === 0 && _tmdbRecPage > _tmdbRecTotalPages) {
                                _tmdbRecPage = 2;
                                _renderedRecIds.clear();
                                _bufferedRecIds.clear();
                                LOG('Infinite scroll: wrapped page cycle back to page 2');
                            }

                            btnMore.textContent = 'Discover More';
                            btnMore.disabled = false;
                        } catch (err) {
                            ERR('Discover More error:', err);
                            btnMore.textContent = 'Error Loading. Try Again';
                            btnMore.disabled = false;
                        }

                    });

                }   // end if (btnMore)
            }   // end else if (rec) — CRITICAL: this brace must close the success branch before the else
            else { rowRec.innerHTML = '<div class="discover-error">Failed to load. Check browser console.</div>'; }
        }

        // Attach Refresh button handler for Upcoming Movies
        var btnUpcomingRefresh = containerDiv.querySelector('[data-action="refresh-upcoming"]');
        if (btnUpcomingRefresh && rowUpcoming) {
            btnUpcomingRefresh.addEventListener('click', async function() {
                var icon = this.querySelector('i');
                if (icon) { icon.textContent = 'hourglass_empty'; }
                rowUpcoming.innerHTML = '<div class="discover-loading">Refreshing...</div>';
                try {
                    var upcNew = await fetchUpcoming();
                    if (isSetup(upcNew)) { rowUpcoming.innerHTML = SETUP_HTML; }
                    else if (upcNew)     { renderTmdbCards(upcNew.results, rowUpcoming, streamBaseUrl, true); }
                    else                 { rowUpcoming.innerHTML = '<div class="discover-error">Failed to load. Check browser console.</div>'; }
                } catch(e) {
                    rowUpcoming.innerHTML = '<div class="discover-error">Failed to load. Check browser console.</div>';
                }
                if (icon) icon.textContent = 'refresh';
            });
        }

        _renderingContainers.delete(containerDiv);
    }

    // ── INTEGRATION HOOKS & NATIVE INJECTION ──

    // Helper: Mount the Discover UI natively, replacing the active Jellyfin view
    function mountNativeDiscoverView() {
        var page = document.querySelector('.page:not(.hide)');
        if (!page) return;

        // Clear existing Jellyfin content and inject ours
        var contentTarget = page.querySelector('.content-primary') || page;
        contentTarget.innerHTML = '';
        var injectWrapper = document.createElement('div');
        injectWrapper.className = 'upcoming-movies-plugin';
        contentTarget.appendChild(injectWrapper);

        populateDiscoverContainer(injectWrapper);
    }

    // Global URL Interceptor to hijack #/home?custom=discover (prevents Page Not Found header stripping)
    var _lastCustomHash = window.location.hash;
    setInterval(function() {
        var currentHash = window.location.hash;
        if (currentHash !== _lastCustomHash) {
            _lastCustomHash = currentHash;
            if (currentHash.indexOf('custom=discover') !== -1) {
                // Let Jellyfin render the Home base first to keep Head Tabs, then hijack
                setTimeout(mountNativeDiscoverView, 150);
            } else if (_lastCustomHash.indexOf('custom=discover') !== -1 && currentHash.indexOf('custom=discover') === -1) {
                // If navigating away from our hijacked view, force a clean DOM reload to un-hijack the Home layout
                window.location.reload();
            }
        }
    }, 150);

    // Inject Navigation dynamically based on NavPlacement configuration + Secondary Links
    async function injectNativeNavigation() {
        if (window._discoverNavInjected) return;
        var config = await fetchPluginConfig();
        var placement = config.navPlacement || 'Sidebar';

        if (placement === 'Header') {
            // Header placement: do NOT inject a Discover tab — user controls header via their own JS inject
            window._discoverNavInjected = true;
            return;
        } else {
            // Sidebar Placement
            var sbInterval = setInterval(function() {
                var menu = document.querySelector('.navMenu');
                if (menu && !menu.querySelector('.discover-sidebar-tab')) {
                    clearInterval(sbInterval);
                    var link = document.createElement('a');
                    link.className = 'navMenuOption emby-button discover-sidebar-tab';
                    link.title = 'Discover';
                    link.innerHTML = '<span class="navMenuOptionIcon material-icons">explore</span><span class="navMenuOptionText">Discover</span>';
                    link.addEventListener('click', function(e) {
                        e.preventDefault();
                        var drawer = document.querySelector('.appDrawer-open');
                        if (drawer) drawer.classList.remove('appDrawer-open');
                        window.location.hash = '#/home?custom=discover';
                        setTimeout(mountNativeDiscoverView, 100);
                    });
                    
                    var container = document.querySelector('.customMenuOptions');
                    var watchlist = container ? container.querySelector('[data-name="watchlist"]') : null;
                    
                    if (watchlist) {
                        watchlist.after(link);
                    } else if (container) {
                        container.appendChild(link);
                    } else {
                        // Fallback logic
                        var homeLink = menu.querySelector('a[href="#/home"]');
                        if (homeLink && homeLink.nextSibling) menu.insertBefore(link, homeLink.nextSibling);
                        else menu.appendChild(link);
                    }
                    
                    window._discoverNavInjected = true;
                }
            }, 500);
        }
    }

    var observer = new MutationObserver(function() {
        // Fallback for Custom Tabs method
        document.querySelectorAll('.upcoming-movies-plugin').forEach(function(el) {
            if (!el.hasAttribute('data-discover-initialized')) {
                el.setAttribute('data-discover-initialized', 'true');
                populateDiscoverContainer(el);
            }
        });
        injectNativeNavigation();
    });
    observer.observe(document.body, { childList: true, subtree: true });

    ['pageshow', 'viewshow', 'viewbeforeshow', 'hashchange'].forEach(function(evtName) {
        window.addEventListener(evtName, function() {
            if (window.location.hash.includes('discover')) {
                setTimeout(mountNativeDiscoverView, 100);
            }
        });
    });

    // ── INIT ──
    injectStyles();
    injectNativeNavigation();

    setTimeout(function() {
        document.querySelectorAll('.upcoming-movies-plugin').forEach(function(el) {
            if (!el.hasAttribute('data-discover-initialized')) {
                el.setAttribute('data-discover-initialized', 'true');
                populateDiscoverContainer(el);
            }
        });
    }, 800);

    LOG('discoverPage.js (Phase 12) loaded natively. Safari/Brave cross-origin tracker bugs bypassed.');
})();
