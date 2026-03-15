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
                padding: 0 2%;
            }
            .discover-row {
                display: flex;
                overflow: hidden;
                gap: 16px;
                padding: 4px 0 14px 0;
                cursor: grab;
                user-select: none;
                -webkit-user-select: none;
            }
            .discover-grid {
                display: grid;
                grid-template-columns: repeat(auto-fill, minmax(150px, 1fr));
                gap: 16px;
                padding: 4px 0 14px 0;
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

            /* ── Star Badge ── */
            .dc-star-badge {
                position: absolute; top: 6px; right: 6px;
                background: rgba(10,10,10,0.85); color: #fff;
                font-size: 0.8em; font-weight: 700;
                padding: 3px 6px; border-radius: 4px;
                display: flex; align-items: center; gap: 4px;
                z-index: 2; pointer-events: none;
            }
            .dc-star-badge svg { width: 12px; height: 12px; fill: #ffc107; }

            /* ── Hover overlay: dark + centered play circle ── */
            .dc-overlay {
                position: absolute; inset: 0;
                background: rgba(0,0,0,0.52);
                display: flex; align-items: center; justify-content: center;
                opacity: 0; transition: opacity 0.18s ease; z-index: 1;
            }
            .discover-card.hover-enabled:hover .dc-overlay { opacity: 1; }

            .dc-play-btn {
                width: 48px; height: 48px; border-radius: 50%;
                border: 2px solid rgba(255,255,255,0.85);
                background: rgba(255,255,255,0.12);
                display: flex; align-items: center; justify-content: center;
                pointer-events: none;
            }
            .dc-play-btn svg { width: 22px; height: 22px; fill: #fff; margin-left: 3px; }
            /* Hide play button for upcoming cards */
            .discover-card.upcoming-card .dc-play-btn { display: none; }

            /* ── Card meta (Title below poster) ── */
            .dc-title {
                margin-top: 8px; font-size: 0.9em; font-weight: 500;
                text-align: center; color: #fff;
                white-space: nowrap; overflow: hidden; text-overflow: ellipsis; padding: 0 6px;
            }
            .dc-date { text-align: center; font-size: 0.77em; color: #aaa; margin-top: 2px; }

            /* ── Button Bar (Below Title) ── */
            .dc-action-bar {
                display: flex; flex-direction: column; gap: 6px;
                margin-top: auto; padding: 8px 6px 4px 6px;
            }
            .dc-action-bar button, .dc-action-bar a {
                width: 100%; border-radius: 5px; font-weight: 600; cursor: pointer; border: none; padding: 7px; font-size: 13px;
                text-align: center; text-decoration: none; transition: opacity 0.2s; box-sizing: border-box;
            }
            .dc-action-bar button:hover, .dc-action-bar a:hover { opacity: 0.85; }
            .dc-action-bar button:disabled { opacity: 1 !important; cursor: default; }

            /* Colors */
            .btn-request { background: #7B5EA7 !important; color: #fff !important; }
            .btn-request[disabled] { background: #4a4a4a !important; color: #fff !important; }
            .btn-stream { background: #00C853 !important; color: #fff !important; }

            .discover-loading { padding: 14px 0; color: #999; font-style: italic; }
            .discover-error   { padding: 14px 0; color: #ef5350; line-height: 1.7; }
            .discover-hidden  { display: none !important; }

            /* ──────────────────────────────────────────
               REQUEST MODAL (Jellyseerr quality-profile)
               ────────────────────────────────────────── */
            .dcm-backdrop {
                position: fixed; inset: 0; z-index: 9998;
                background: rgba(0,0,0,0.72);
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
        `;
        document.head.appendChild(style);
    }

    // ─────────────────────────────────────────────
    // 4. HTML TEMPLATE
    // ─────────────────────────────────────────────

    function buildSectionHtml(id, title, isGrid) {
        if (isGrid) {
            return '<div class="discover-section" data-section="' + id + '">'
                + '<h2 class="discover-section-title">' + title + '</h2>'
                + '<div class="discover-grid" data-row="' + id + '">'
                + '  <div class="discover-loading">Loading&hellip;</div>'
                + '</div>'
                + '<div style="text-align:center; padding: 10px;"><button class="btn-discover-more dcm-btn" data-more="' + id + '" style="background:#00C853; color:#fff; display:none;">Discover More</button></div>'
                + '</div>';
        }
        return '<div class="discover-section" data-section="' + id + '">'
            + '<h2 class="discover-section-title">' + title + '</h2>'
            + '<div class="discover-row-wrap">'
            + '  <button class="discover-arrow left" aria-label="Scroll left">&#8249;</button>'
            + '  <div class="discover-row" data-row="' + id + '">'
            + '    <div class="discover-loading">Loading&hellip;</div>'
            + '  </div>'
            + '  <button class="discover-arrow right" aria-label="Scroll right">&#8250;</button>'
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

    async function fetchUpcoming() {
        var res = await fetch('/UpcomingMovies/tmdb/upcoming', {
            headers: { 'X-Emby-Authorization': getJellyfinAuthHeader() }
        });
        if (res.status === 400 || res.status === 500) return NEEDS_SETUP;
        if (!res.ok) throw new Error('TMDB upstream error ' + res.status);
        return res.json();
    }

    // Build the user signal profile from Jellyfin APIs and call the backend recommendations endpoint
    async function fetchRecommendations(page) {
        page = page || 1;
        var client = window.ApiClient;
        var userId = client && client.getCurrentUserId();
        var tmdbIds   = [];
        var genreWeights = {};

        if (userId) {
            try {
                var server = client._serverAddress;
                var token  = client.accessToken();

                // a) Watched movies (last 50)
                var watchedRes = await fetch(
                    server + '/Users/' + userId + '/Items?IncludeItemTypes=Movie&Filters=IsPlayed&SortBy=DatePlayed&SortOrder=Descending&Limit=50&Recursive=true',
                    { headers: { 'X-Emby-Token': token } }
                );
                if (watchedRes.ok) {
                    var watchedData = await watchedRes.json();
                    (watchedData.Items || []).forEach(function(item) {
                        // Extract TMDB ID
                        var tid = item.ProviderIds && item.ProviderIds.Tmdb ? parseInt(item.ProviderIds.Tmdb, 10) : null;
                        if (tid && tid > 0) tmdbIds.push(tid);
                        // Weight genres by recency (watched)
                        (item.GenreItems || []).forEach(function(g) {
                            var gid = GENRE_MAP[g.Name];
                            if (gid) genreWeights[gid] = (genreWeights[gid] || 0) + 1;
                        });
                    });
                }

                // b) Favourites / Watchlist (2x weight — stronger signal)
                var favRes = await fetch(
                    server + '/Users/' + userId + '/Items?IncludeItemTypes=Movie&IsFavorite=true&Recursive=true&Limit=30',
                    { headers: { 'X-Emby-Token': token } }
                );
                if (favRes.ok) {
                    var favData = await favRes.json();
                    (favData.Items || []).forEach(function(item) {
                        var tid = item.ProviderIds && item.ProviderIds.Tmdb ? parseInt(item.ProviderIds.Tmdb, 10) : null;
                        if (tid && tid > 0 && tmdbIds.indexOf(tid) === -1) tmdbIds.push(tid);
                        (item.GenreItems || []).forEach(function(g) {
                            var gid = GENRE_MAP[g.Name];
                            if (gid) genreWeights[gid] = (genreWeights[gid] || 0) + 2; // 2x for favourites
                        });
                    });
                }
            } catch (err) {
                WARN('Failed to gather user signals:', err);
            }
        }

        // Sort genres by weight descending, take top 5
        var topGenres = Object.keys(genreWeights)
            .sort(function(a, b) { return genreWeights[b] - genreWeights[a]; })
            .slice(0, 5)
            .join('|');

        // Take top 8 TMDB IDs as movie seeds
        var seedIds = tmdbIds.slice(0, 8).join(',');

        var params = [];
        if (seedIds) params.push('tmdbIds=' + encodeURIComponent(seedIds));
        if (topGenres) params.push('genreIds=' + encodeURIComponent(topGenres));
        params.push('page=' + page);
        var qs = params.length ? '?' + params.join('&') : '';

        var res = await fetch('/UpcomingMovies/tmdb/recommendations' + qs, {
            headers: { 'X-Emby-Authorization': getJellyfinAuthHeader() }
        });
        if (res.status === 400 || res.status === 500) return NEEDS_SETUP;
        if (!res.ok) throw new Error('Recommendations error ' + res.status);
        return res.json();
    }

    // ─────────────────────────────────────────────
    // 7. JELLYSEERR MODAL
    // ─────────────────────────────────────────────

    async function openRequestModal(tmdbId, movieTitle, backdropUrl) {
        // Fetch Radarr instances for dropdowns
        var radarrInstances = [];
        try {
            var radarrRes = await fetch('/UpcomingMovies/jellyseerr/radarr', {
                headers: { 'X-Emby-Authorization': getJellyfinAuthHeader() }
            });
            if (radarrRes.ok) {
                radarrInstances = await radarrRes.json();
            }
        } catch (err) {
            WARN('Could not load Radarr instances:', err);
        }

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
                    // Update button on the UI
                    var btn = document.querySelector('.btn-request[data-tmdb="' + tmdbId + '"]');
                    if (btn) {
                        btn.innerHTML = '&#10003; Requested';
                        btn.style.background = '#4a4a4a';
                        btn.style.color = '#fff';
                        btn.disabled = true;
                    }
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

    var SETUP_HTML = '<div class="discover-error">'
        + '&#9888;&#65039; <strong>TMDB API key not configured.</strong><br>'
        + 'Go to <strong>Dashboard &rarr; Plugins &rarr; Upcoming Movies &amp; Recommendations</strong> and enter your TMDB API key.<br>'
        + '<a href="#/configurationpage?name=Upcoming Movies %26 Recommendations" style="color:#90caf9">Open Plugin Settings &rarr;</a>'
        + '</div>';

    var STAR_SVG = '<svg viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg"><path d="M12 2l3.09 6.26L22 9.27l-5 4.87 1.18 6.88L12 17.77l-6.18 3.25L7 14.14 2 9.27l6.91-1.01L12 2z"/></svg>';

    /**
     * Build a card element.
     * @param {Object} opts
     *   tmdbId, title, posterUrl, backdropUrl, date, streamBaseUrl,
     *   isUpcoming {bool}
     *   jellyfinId {string}
     *   voteAverage {number}
     */
    function buildCard(opts) {
        var tmdbId       = opts.tmdbId;
        var title        = opts.title || '';
        var posterUrl    = opts.posterUrl;
        var backdropUrl  = opts.backdropUrl;
        var date         = opts.date;
        var streamBaseUrl = opts.streamBaseUrl || '';
        var isUpcoming   = !!opts.isUpcoming;
        var isAvailable  = !!opts.isAvailable;
        var jellyfinId   = opts.jellyfinId;
        var vote         = opts.voteAverage ? opts.voteAverage.toFixed(1) : '';

        var card = document.createElement('div');
        card.className = 'discover-card' + (isUpcoming ? ' upcoming-card' : '');
        // Only natively linkable items get hover scale
        if (!isUpcoming && isAvailable) card.className += ' hover-enabled';

        var actionsHtml = '';
        var playHtml = '';

        if (!isUpcoming) {
            if (isAvailable && jellyfinId) {
                // Native Jellyfin Card behavior: No block buttons. Just the hidden hover play overlay.
                playHtml = '<div class="dc-overlay"><div class="dc-play-btn">' + PLAY_SVG + '</div></div>';
            } else if (tmdbId) {
                // Unavailable Rec: Show Request and Stream buttons below the title
                actionsHtml += '<button class="jellyseerr-request-button jellyseerr-button-request btn-request" data-tmdb="' + tmdbId + '">Request</button>';
                if (streamBaseUrl) {
                    actionsHtml += '<a href="' + streamBaseUrl + '/movie/' + tmdbId + '" target="_blank" class="btn-stream">Stream</a>';
                }
            }
        } else if (tmdbId) {
            // Upcoming: Just Request block button
            actionsHtml += '<button class="jellyseerr-request-button jellyseerr-button-request btn-request" data-tmdb="' + tmdbId + '">Request</button>';
        }

        var posterHtml = posterUrl
            ? '<img src="' + escapeHtml(posterUrl) + '" alt="' + escapeHtml(title) + '" loading="lazy" />'
            : '<div class="dc-no-poster">\uD83C\uDFAC</div>';

        var badgeHtml = vote && vote > 0 ? '<div class="dc-star-badge">' + STAR_SVG + vote + '</div>' : '';

        card.innerHTML =
            '<div class="dc-poster">'
            + posterHtml
            + badgeHtml
            + playHtml
            + '</div>'
            + '<div class="dc-title" title="' + escapeHtml(title) + '">' + escapeHtml(title) + '</div>'
            + (isUpcoming && date ? '<div class="dc-date">' + date + '</div>' : '')
            + (actionsHtml ? '<div class="dc-action-bar">' + actionsHtml + '</div>' : '');

        // Whole-card click → Jellyfin detail (for Native items only)
        if (!isUpcoming && isAvailable) {
            card.querySelector('.dc-poster').addEventListener('click', function(e) {
                if (jellyfinId) window.location.hash = '#/details?id=' + jellyfinId;
            });
        }

        // Request button → Jellyseerr modal
        var btnReq = card.querySelector('.btn-request');
        if (btnReq) btnReq.addEventListener('click', function(e) {
            e.stopPropagation();
            openRequestModal(e.currentTarget.dataset.tmdb, title, backdropUrl);
        });

        // Play button
        var btnPlay = card.querySelector('.btn-play');
        if (btnPlay) btnPlay.addEventListener('click', function(e) {
            e.stopPropagation();
            window.location.hash = '#/details?id=' + e.currentTarget.dataset.jellyfin;
        });

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
        movies.forEach(function(movie) {
            var posterUrl   = movie.poster_path   ? TMDB_IMAGE_BASE + movie.poster_path   : null;
            var backdropUrl = movie.backdrop_path ? 'https://image.tmdb.org/t/p/w780' + movie.backdrop_path : null;
            containerEl.appendChild(buildCard({
                tmdbId:       movie.id,
                title:        movie.title,
                posterUrl:    posterUrl,
                backdropUrl:  backdropUrl,
                date:         movie.release_date || null,
                streamBaseUrl: streamBaseUrl,
                isUpcoming:   !!isUpcoming,
                isAvailable:  movie.isAvailable,
                jellyfinId:   movie.jellyfinId,
                voteAverage:  movie.vote_average
            }));
        });
    }

    // ─────────────────────────────────────────────
    // 10. BOOT
    // ─────────────────────────────────────────────

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

        function isSetup(v) { return v && v._needsSetup; }

        var results = await Promise.all([
            config.showUpcoming      ? fetchUpcoming().catch(function(e)         { ERR('fetchUpcoming:', e);      return null; }) : null,
            config.showRecommendations ? fetchRecommendations().catch(function(e) { ERR('fetchRecommendations:', e); return null; }) : null
        ]);
        var upc = results[0], rec = results[1];

        // Fetch Jellyfin library map so we know which items are available
        var client = window.ApiClient;
        var userId = client && client.getCurrentUserId();
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
                    var tmdbMap = {};
                    (allData.Items || []).forEach(function(item) {
                        var tid = item.ProviderIds && item.ProviderIds.Tmdb ? parseInt(item.ProviderIds.Tmdb, 10) : null;
                        if (tid) {
                            tmdbMap[tid] = { id: item.Id, played: item.UserData && item.UserData.Played };
                        }
                    });

                    // Filter out already watched movies from recommendations
                    rec.results = rec.results.filter(function(movie) {
                        var localInfo = tmdbMap[movie.id];
                        if (localInfo && localInfo.played) return false; // Hide completely
                        if (localInfo) {
                            movie.isAvailable = true;
                            movie.jellyfinId = localInfo.id;
                        }
                        return true;
                    });
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

        var recPage = 1;
        var btnMore = containerDiv.querySelector('.btn-discover-more');

        if (rowRec) {
            if (isSetup(rec)) { rowRec.innerHTML = SETUP_HTML; }
            else if (rec) {
                renderTmdbCards(rec.results, rowRec, streamBaseUrl, false, false);
                if (btnMore && rec.page < rec.total_pages) {
                    btnMore.style.display = 'inline-block';
                    btnMore.addEventListener('click', async function() {
                        btnMore.textContent = 'Loading...';
                        recPage++;
                        try {
                            var moreRecs = await fetchRecommendations(recPage);
                            if (moreRecs && moreRecs.results) {
                                // Filter out watched items again for pagination
                                if (tmdbMap) {
                                  moreRecs.results = moreRecs.results.filter(function(m) {
                                      var info = tmdbMap[m.id];
                                      if (info && info.played) return false;
                                      if (info) {
                                          m.isAvailable = true;
                                          m.jellyfinId = info.id;
                                      }
                                      return true;
                                  });
                                }
                                renderTmdbCards(moreRecs.results, rowRec, streamBaseUrl, false, true);
                            }
                            if (!moreRecs || recPage >= moreRecs.total_pages) btnMore.style.display = 'none';
                            else btnMore.textContent = 'Discover More';
                        } catch (err) {
                            btnMore.textContent = 'Error Loading. Try Again';
                        }
                    });
                }
            }
            else { rowRec.innerHTML = '<div class="discover-error">Failed to load. Check browser console.</div>'; }
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

        // Update Jellyfin's page title natively
        var titleEl = document.querySelector('.pageTitleWithDefaultLogo');
        if (titleEl) titleEl.textContent = 'Discover';
        else {
            var logo = document.querySelector('.pageTitleWithLogo');
            if (logo) logo.style.backgroundImage = 'none';
            if (logo) logo.textContent = 'Discover';
        }

        populateDiscoverContainer(injectWrapper);
    }

    // Inject Navigation dynamically based on NavPlacement configuration + Secondary Links
    async function injectNativeNavigation() {
        if (window._discoverNavInjected) return;
        var config = await fetchPluginConfig();
        var placement = config.navPlacement || 'Sidebar';

        if (placement === 'Header') {
            // Wait for header tabs to render
            var interval = setInterval(function() {
                var tabContainer = document.querySelector('.headerTabs');
                if (tabContainer && !tabContainer.querySelector('.discover-header-tab')) {
                    clearInterval(interval);
                    var tabBtn = document.createElement('button');
                    tabBtn.className = 'emby-tab-button focuscontainer-x emby-button discover-header-tab';
                    tabBtn.innerHTML = '<div class="emby-button-foreground"><span class="emby-tab-button-inner">Discover</span></div>';
                    tabBtn.addEventListener('click', function() {
                        // Deactivate other tabs visually
                        tabContainer.querySelectorAll('.emby-tab-button').forEach(t => t.classList.remove('emby-tab-button-active'));
                        tabBtn.classList.add('emby-tab-button-active');
                        window.location.hash = '#/home?tab=discover';
                        mountNativeDiscoverView();
                    });
                    tabContainer.appendChild(tabBtn);
                    window._discoverNavInjected = true;
                }
            }, 500);
        } else {
            // Sidebar Placement - Secondary Links (Calendar, Live Downloads, Discover)
            var sbInterval = setInterval(function() {
                var container = document.querySelector('.customMenuOptions');
                if (container && !document.getElementById('htv-calendar-link')) {
                    clearInterval(sbInterval);
                    var watchlist = container.querySelector('[data-name="watchlist"]');
                    
                    var LINKS = [
                        {
                            id: "htv-calendar-link",
                            label: "Calendar",
                            href: "#/userpluginsettings.html?pageUrl=/JellyfinEnhanced/calendarPage",
                            icon: "event"
                        },
                        {
                            id: "htv-downloads-link",
                            label: "Live Downloads",
                            href: "#/userpluginsettings.html?pageUrl=/JellyfinEnhanced/downloadsPage",
                            icon: "downloading"
                        },
                        {
                            id: "htv-discover-link",
                            label: "Discover",
                            href: "#/discover",
                            icon: "explore",
                            isDiscover: true
                        }
                    ];

                    LINKS.forEach(function(cfg, i) {
                        if (document.getElementById(cfg.id)) return;

                        var a = document.createElement("a");
                        a.id = cfg.id;
                        a.href = cfg.href;
                        a.className = "emby-linkbutton navMenuOption lnkMediaFolder discover-sidebar-tab";
                        a.title = cfg.label;

                        a.innerHTML = 
                            '<i class="md-icon navMenuOptionIcon material-icons">' + cfg.icon + '</i>' +
                            '<span class="navMenuOptionText">' + cfg.label + '</span>';

                        if (cfg.isDiscover) {
                            a.addEventListener('click', function(e) {
                                e.preventDefault();
                                var drawer = document.querySelector('.appDrawer-open');
                                if (drawer) drawer.classList.remove('appDrawer-open');
                                window.location.hash = '#/discover';
                                setTimeout(mountNativeDiscoverView, 50);
                            });
                        }

                        // Insert in order directly after Watchlist
                        if (watchlist) {
                            if (i === 0) {
                                watchlist.after(a);       // Calendar
                            } else {
                                // Insert sequentially
                                var prev = document.getElementById(LINKS[i-1].id);
                                if (prev) prev.after(a);
                                else container.appendChild(a);
                            }
                        } else {
                            container.appendChild(a);
                        }
                    });
                    
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
