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
                overflow: hidden;     /* NO scroll bar — drag or arrows only */
                gap: 12px;
                padding: 4px 3% 14px 3%;
                cursor: grab;
                user-select: none;
                -webkit-user-select: none;
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
                display: inline-flex; flex-direction: column;
                flex: 0 0 auto;
                width: 150px;
                border-radius: 4px;
                overflow: hidden;
                scroll-snap-align: start;
                transition: transform 0.2s ease, box-shadow 0.2s ease;
                background: rgba(255,255,255,0.04);
                user-select: none;
            }
            .discover-card:hover { transform: scale(1.04); box-shadow: 0 8px 24px rgba(0,0,0,0.6); }

            /* ── Poster wrapper ── */
            .discover-card .dc-poster {
                position: relative; width: 150px; height: 225px;
                overflow: hidden; background: #111; flex-shrink: 0;
            }
            .discover-card .dc-poster img { width: 100%; height: 100%; object-fit: cover; display: block; pointer-events: none; }
            .dc-no-poster {
                width: 100%; height: 100%;
                display: flex; align-items: center; justify-content: center;
                background: #1a1a2e; color: #888; font-size: 2em;
            }

            /* ── Hover overlay: dark + centered play circle (Recommended/Stream cards) ── */
            .dc-overlay {
                position: absolute; inset: 0;
                background: rgba(0,0,0,0.52);
                display: flex; align-items: center; justify-content: center;
                opacity: 0; transition: opacity 0.18s ease;
            }
            .discover-card:hover .dc-overlay { opacity: 1; }
            /* Upcoming cards: light overlay only, no centered play button */
            .discover-card.upcoming-card:hover .dc-overlay { background: rgba(0,0,0,0.38); }

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

            /* ── Bottom action bar ── */
            .dc-actions {
                position: absolute; bottom: 0; left: 0; right: 0;
                padding: 5px 7px; display: flex; gap: 5px;
                background: linear-gradient(transparent, rgba(0,0,0,0.92));
                opacity: 0; transition: opacity 0.18s ease;
            }
            .discover-card:hover .dc-actions { opacity: 1; }
            .dc-actions button {
                flex: 1; padding: 5px 4px; border-radius: 3px; cursor: pointer;
                font-size: 0.70em; font-weight: 700; letter-spacing: 0.02em;
                white-space: nowrap; transition: background 0.12s;
                background: rgba(0,0,0,0.45); color: #fff;
            }
            /* Request = Jellyseerr purple */
            .btn-request { border: 1px solid #7B5EA7 !important; color: #b39ddb !important; }
            .btn-request:hover { background: rgba(123,94,167,0.32) !important; }
            /* Stream = H-TV green */
            .btn-stream  { border: 1px solid #00C853 !important; color: #69f0ae !important; }
            .btn-stream:hover  { background: rgba(0,200,83,0.22) !important; }

            /* ── Card meta ── */
            .dc-title {
                padding: 6px 7px 2px 7px; font-size: 0.84em; font-weight: 500;
                white-space: nowrap; overflow: hidden; text-overflow: ellipsis; color: #e0e0e0;
            }
            .dc-date { padding: 0 7px 7px 7px; font-size: 0.77em; color: #888; }

            .discover-loading { padding: 14px 5%; color: #999; font-style: italic; }
            .discover-error   { padding: 14px 5%; color: #ef5350; line-height: 1.7; }
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

    function buildSectionHtml(id, title) {
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
            + buildSectionHtml('upcoming', 'Upcoming Movies')
            + buildSectionHtml('recommended', 'Recommended For You')
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
    async function fetchRecommendations() {
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
            .join(',');

        // Take top 8 TMDB IDs as movie seeds
        var seedIds = tmdbIds.slice(0, 8).join(',');

        var params = [];
        if (seedIds) params.push('tmdbIds=' + encodeURIComponent(seedIds));
        if (topGenres) params.push('genreIds=' + encodeURIComponent(topGenres));
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
            if (serverSel) body.serverId = parseInt(serverSel.value, 10) || undefined;
            if (profileSel && profileSel.value) body.profileId = parseInt(profileSel.value, 10) || undefined;
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

    /**
     * Build a card element.
     * @param {Object} opts
     *   tmdbId, title, posterUrl, backdropUrl, date, streamBaseUrl,
     *   isUpcoming {bool} — if true: only Request button, no play-circle click
     *   jellyfinId — if set: navigate to Jellyfin detail
     */
    function buildCard(opts) {
        var tmdbId       = opts.tmdbId;
        var title        = opts.title || '';
        var posterUrl    = opts.posterUrl;
        var backdropUrl  = opts.backdropUrl;
        var date         = opts.date;
        var streamBaseUrl = opts.streamBaseUrl || '';
        var isUpcoming   = !!opts.isUpcoming;
        var jellyfinId   = opts.jellyfinId;

        var card = document.createElement('div');
        card.className = 'discover-card' + (isUpcoming ? ' upcoming-card' : '');

        var actionsHtml = '';
        if (tmdbId) {
            actionsHtml += '<button class="btn-request" data-tmdb="' + tmdbId + '">Request</button>';
        }
        if (!isUpcoming && tmdbId && streamBaseUrl) {
            actionsHtml += '<button class="btn-stream" data-tmdb="' + tmdbId + '">Stream</button>';
        }

        var posterHtml = posterUrl
            ? '<img src="' + escapeHtml(posterUrl) + '" alt="' + escapeHtml(title) + '" loading="lazy" />'
            : '<div class="dc-no-poster">\uD83C\uDFAC</div>';

        card.innerHTML =
            '<div class="dc-poster">'
            + posterHtml
            + '<div class="dc-overlay"><div class="dc-play-btn">' + PLAY_SVG + '</div></div>'
            + (actionsHtml ? '<div class="dc-actions">' + actionsHtml + '</div>' : '')
            + '</div>'
            + '<div class="dc-title" title="' + escapeHtml(title) + '">' + escapeHtml(title) + '</div>'
            + (date ? '<div class="dc-date">' + date + '</div>' : '');

        // Whole-card click → Jellyfin detail (for Jellyfin native items)
        if (!isUpcoming) {
            card.addEventListener('click', function(e) {
                if (e.target.closest('button')) return;
                if (jellyfinId) window.location.hash = '#/details?id=' + jellyfinId;
            });
        }

        // Request button → Jellyseerr modal
        var btnReq = card.querySelector('.btn-request');
        if (btnReq) btnReq.addEventListener('click', function(e) {
            e.stopPropagation();
            openRequestModal(e.currentTarget.dataset.tmdb, title, backdropUrl);
        });

        // Stream button
        var btnStr = card.querySelector('.btn-stream');
        if (btnStr) btnStr.addEventListener('click', function(e) {
            e.stopPropagation();
            var base = streamBaseUrl.replace(/\/$/, '');
            window.open(base + '/' + e.currentTarget.dataset.tmdb, '_blank', 'noopener');
        });

        return card;
    }

    // ─────────────────────────────────────────────
    // 9. RENDERING
    // ─────────────────────────────────────────────

    function renderTmdbCards(movies, containerEl, streamBaseUrl, isUpcoming) {
        containerEl.innerHTML = '';
        if (!movies || movies.length === 0) {
            containerEl.innerHTML = '<div class="discover-loading">No items found.</div>';
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
                isUpcoming:   !!isUpcoming
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

        if (rowUpcoming) {
            if (isSetup(upc)) { rowUpcoming.innerHTML = SETUP_HTML; }
            else if (upc)     { renderTmdbCards(upc.results, rowUpcoming, streamBaseUrl, true); }
            else              { rowUpcoming.innerHTML = '<div class="discover-error">Failed to load. Check browser console.</div>'; }
        }

        if (rowRec) {
            if (isSetup(rec)) { rowRec.innerHTML = SETUP_HTML; }
            else if (rec)     { renderTmdbCards(rec.results, rowRec, streamBaseUrl, false); }
            else              { rowRec.innerHTML = '<div class="discover-error">Failed to load. Check browser console.</div>'; }
        }

        _renderingContainers.delete(containerDiv);
    }

    // ── INTEGRATION HOOKS ──

    var observer = new MutationObserver(function() {
        document.querySelectorAll('.upcoming-movies-plugin').forEach(function(el) {
            if (!el.hasAttribute('data-discover-initialized')) {
                el.setAttribute('data-discover-initialized', 'true');
                populateDiscoverContainer(el);
            }
        });
    });
    observer.observe(document.body, { childList: true, subtree: true });

    ['pageshow', 'viewshow', 'viewbeforeshow'].forEach(function(evtName) {
        document.addEventListener(evtName, function(e) {
            if (e.target && e.target.id === 'DiscoverPage') {
                var inner = e.target.querySelector('.content-primary');
                if (inner && !inner.hasAttribute('data-discover-initialized')) {
                    inner.setAttribute('data-discover-initialized', 'true');
                    populateDiscoverContainer(inner);
                }
            }
        });
    });

    // ── INIT ──
    injectStyles();

    setTimeout(function() {
        document.querySelectorAll('.upcoming-movies-plugin').forEach(function(el) {
            if (!el.hasAttribute('data-discover-initialized')) {
                el.setAttribute('data-discover-initialized', 'true');
                populateDiscoverContainer(el);
            }
        });
    }, 800);

    LOG('discoverPage.js (Phase 8) loaded.');
})();
