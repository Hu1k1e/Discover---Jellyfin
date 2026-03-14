/**
 * discoverPage.js
 * Jellyfin Plugin — Upcoming Movies & Recommendations
 *
 * Responsibilities:
 *  1. Listen for the native #DiscoverPage view (for Custom Menu Links JSON sidebar integration).
 *  2. Listen for the emergence of `.upcoming-movies-plugin` (for Custom Tabs plugin integration).
 *  3. Render the movie grid inside whichever native container appears.
 */

(function () {
    'use strict';

    const LOG  = (...a) => console.log('[UpcomingMovies]', ...a);
    const WARN = (...a) => console.warn('[UpcomingMovies]', ...a);
    const ERR  = (...a) => console.error('[UpcomingMovies]', ...a);

    const TMDB_IMAGE_BASE = 'https://image.tmdb.org/t/p/w342';

    // ──────────────────────────────────────────────────────────
    // 1. CONFIG
    // ──────────────────────────────────────────────────────────

    let _pluginConfig = null;

    async function fetchPluginConfig() {
        if (_pluginConfig) return _pluginConfig;
        try {
            const res = await fetch('/UpcomingMovies/tmdb/config', {
                headers: { 'X-Emby-Authorization': getJellyfinAuthHeader() }
            });
            if (res.ok) {
                _pluginConfig = await res.json();
            }
        } catch (err) {
            WARN('Could not fetch plugin config, using defaults:', err);
        }
        return _pluginConfig || {
            streamBaseUrl: '',
            showUpcoming: true,
            showRecommendations: true,
            showWatchlist: true,
            tmdbConfigured: false
        };
    }

    function getJellyfinAuthHeader() {
        const c = window.ApiClient;
        if (!c) return '';
        const token      = c.accessToken ? c.accessToken() : '';
        const deviceId   = c._deviceId || '';
        const deviceName = c._deviceName || 'Jellyfin Web';
        const version    = c._appVersion || '10.11.0';
        return `MediaBrowser Token="${token}", Client="Jellyfin Web", Device="${deviceName}", DeviceId="${deviceId}", Version="${version}"`;
    }

    // ──────────────────────────────────────────────────────────
    // 2. STYLES — Native Jellyfin card look
    // ──────────────────────────────────────────────────────────

    function injectStyles() {
        if (document.getElementById('discover-plugin-styles')) return;
        const style = document.createElement('style');
        style.id = 'discover-plugin-styles';
        style.textContent = `
            .discover-page-content { padding: 1.5rem 0; width: 100%; box-sizing: border-box; }
            .discover-section { margin-bottom: 2.5rem; }
            .discover-section-title { margin-bottom: 1rem; font-size: 1.25em; padding: 0 5%; font-weight: 500; }

            .discover-row {
                display: flex;
                overflow-x: auto;
                overflow-y: hidden;
                padding: 0 5% 12px 5%;
                scroll-snap-type: x mandatory;
                gap: 12px;
            }
            .discover-row::-webkit-scrollbar { height: 6px; }
            .discover-row::-webkit-scrollbar-track { background: rgba(0,0,0,0.1); border-radius: 4px; }
            .discover-row::-webkit-scrollbar-thumb { background: rgba(255,255,255,0.2); border-radius: 4px; }

            /* ── Card ── */
            .discover-card {
                display: inline-flex;
                flex-direction: column;
                flex: 0 0 auto;
                width: 150px;
                border-radius: 4px;
                overflow: hidden;
                cursor: pointer;
                scroll-snap-align: start;
                transition: transform 0.2s ease, box-shadow 0.2s ease;
                background: rgba(255,255,255,0.04);
            }
            .discover-card:hover { transform: scale(1.04); box-shadow: 0 8px 24px rgba(0,0,0,0.6); }

            /* ── Poster wrapper — holds image + overlays ── */
            .discover-card .dc-poster {
                position: relative;
                width: 150px;
                height: 225px;
                overflow: hidden;
                background: #111;
                flex-shrink: 0;
            }
            .discover-card .dc-poster img {
                width: 100%; height: 100%;
                object-fit: cover; display: block;
            }
            .dc-no-poster {
                width: 100%; height: 100%;
                display: flex; align-items: center; justify-content: center;
                background: #1a1a2e; color: #888; font-size: 2em;
            }

            /* ── Jellyfin-style semi-dark overlay + centered play circle ── */
            .dc-overlay {
                position: absolute;
                inset: 0;
                background: rgba(0,0,0,0.52);
                display: flex;
                align-items: center;
                justify-content: center;
                opacity: 0;
                transition: opacity 0.18s ease;
            }
            .discover-card:hover .dc-overlay { opacity: 1; }

            .dc-play-btn {
                width: 48px; height: 48px;
                border-radius: 50%;
                border: 2px solid rgba(255,255,255,0.85);
                background: rgba(255,255,255,0.12);
                display: flex; align-items: center; justify-content: center;
                transition: background 0.15s;
                pointer-events: none;
            }
            .dc-play-btn svg { width: 22px; height: 22px; fill: #fff; margin-left: 3px; }

            /* ── Bottom action bar ── */
            .dc-actions {
                position: absolute;
                bottom: 0; left: 0; right: 0;
                padding: 5px 7px;
                display: flex; gap: 5px;
                background: linear-gradient(transparent, rgba(0,0,0,0.92));
                opacity: 0;
                transition: opacity 0.18s ease;
            }
            .discover-card:hover .dc-actions { opacity: 1; }
            .dc-actions button {
                flex: 1;
                padding: 4px 3px;
                border-radius: 3px;
                cursor: pointer;
                font-size: 0.70em;
                font-weight: 700;
                letter-spacing: 0.02em;
                white-space: nowrap;
                transition: background 0.12s;
                background: rgba(0,0,0,0.45);
            }
            .btn-jellyfin { border: 1px solid #4caf50; color: #4caf50 !important; }
            .btn-jellyfin:hover { background: rgba(76,175,80,0.3) !important; }
            .btn-request  { border: 1px solid var(--theme-primary-color, #00a4dc); color: var(--theme-primary-color, #00a4dc) !important; }
            .btn-request:hover  { background: rgba(0,164,220,0.3) !important; }
            .btn-stream   { border: 1px solid #ef5350; color: #ef5350 !important; }
            .btn-stream:hover   { background: rgba(239,83,80,0.3) !important; }

            /* ── Card meta below poster ── */
            .dc-title {
                padding: 6px 7px 2px 7px;
                font-size: 0.84em; font-weight: 500;
                white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
                color: #e0e0e0;
            }
            .dc-date { padding: 0 7px 7px 7px; font-size: 0.77em; color: #888; }

            .discover-loading, .discover-error { padding: 20px 5%; color: #aaa; }
            .discover-error { color: #ef5350; line-height: 1.7; }
            .discover-hidden { display: none !important; }
        `;
        document.head.appendChild(style);
    }

    // ──────────────────────────────────────────────────────────
    // 3. HTML TEMPLATE
    // ──────────────────────────────────────────────────────────

    function getGridTemplate() {
        return `
            <div class="discover-page-content">
                <div class="discover-section" data-section="upcoming">
                    <h2 class="discover-section-title">Upcoming Movies</h2>
                    <div class="discover-row upcoming-items">
                        <div class="discover-loading">Loading upcoming movies...</div>
                    </div>
                </div>
                <div class="discover-section" data-section="recommended">
                    <h2 class="discover-section-title">Recommended For You</h2>
                    <div class="discover-row recommended-items">
                        <div class="discover-loading">Loading recommendations...</div>
                    </div>
                </div>
                <div class="discover-section" data-section="watchlist">
                    <h2 class="discover-section-title">My Watchlist</h2>
                    <div class="discover-row watchlist-items">
                        <div class="discover-loading">Loading watchlist...</div>
                    </div>
                </div>
            </div>
        `;
    }

    function escapeHtml(str) {
        if (!str) return '';
        return String(str)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;')
            .replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    // ──────────────────────────────────────────────────────────
    // 4. DATA FETCHING
    // ──────────────────────────────────────────────────────────

    const GENRE_MAP = {
        'Action': 28, 'Adventure': 12, 'Animation': 16, 'Comedy': 35,
        'Crime': 80, 'Documentary': 99, 'Drama': 18, 'Family': 10751,
        'Fantasy': 14, 'History': 36, 'Horror': 27, 'Music': 10402,
        'Mystery': 9648, 'Romance': 10749, 'Science Fiction': 878,
        'Sci-Fi': 878, 'Thriller': 53, 'War': 10752, 'Western': 37
    };

    // Sentinel: tells the renderer to show a "please configure" message
    const NEEDS_SETUP = { _needsSetup: true };

    async function fetchUpcoming() {
        const res = await fetch('/UpcomingMovies/tmdb/upcoming', {
            headers: { 'X-Emby-Authorization': getJellyfinAuthHeader() }
        });
        if (res.status === 400 || res.status === 500) {
            ERR('fetchUpcoming returned', res.status, '— API key may not be configured');
            return NEEDS_SETUP;
        }
        if (!res.ok) throw new Error('TMDB upstream error: ' + res.status);
        return res.json();
    }

    async function fetchRecommendations() {
        let genreIds = '';
        try {
            const userId = window.ApiClient && window.ApiClient.getCurrentUserId();
            if (userId) {
                const histRes = await fetch(
                    window.ApiClient._serverAddress + '/Users/' + userId +
                    '/Items?IncludeItemTypes=Movie&Filters=IsPlayed&SortBy=DatePlayed&SortOrder=Descending&Limit=30&Recursive=true',
                    { headers: { 'X-Emby-Token': window.ApiClient.accessToken() } }
                );
                if (histRes.ok) {
                    const histData = await histRes.json();
                    const genreSet = new Set();
                    (histData.Items || []).forEach(function(item) {
                        (item.GenreItems || []).forEach(function(g) {
                            if (GENRE_MAP[g.Name]) genreSet.add(GENRE_MAP[g.Name]);
                        });
                    });
                    genreIds = Array.from(genreSet).slice(0, 3).join(',');
                }
            }
        } catch (err) { WARN('Genre lookup failed:', err); }

        const url = '/UpcomingMovies/tmdb/recommendations' + (genreIds ? '?genreIds=' + genreIds : '');
        const res = await fetch(url, {
            headers: { 'X-Emby-Authorization': getJellyfinAuthHeader() }
        });
        if (res.status === 400 || res.status === 500) {
            ERR('fetchRecommendations returned', res.status, '— API key may not be configured');
            return NEEDS_SETUP;
        }
        if (!res.ok) throw new Error('TMDB upstream error: ' + res.status);
        return res.json();
    }

    async function fetchWatchlist() {
        const userId = window.ApiClient && window.ApiClient.getCurrentUserId();
        if (!userId) throw new Error('Not logged in');
        // IsFavorite=true matches the KefinTweaks Watchlist (Jellyfin native favourites/heart system)
        const server = window.ApiClient._serverAddress;
        const res = await fetch(
            server + '/Users/' + userId +
            '/Items?IncludeItemTypes=Movie&IsFavorite=true&SortBy=DateCreated&SortOrder=Descending&Recursive=true',
            { headers: { 'X-Emby-Token': window.ApiClient.accessToken() } }
        );
        if (!res.ok) throw new Error('Failed to fetch watchlist: ' + res.status);
        return res.json();
    }

    // ──────────────────────────────────────────────────────────
    // 5. RENDERING
    // ──────────────────────────────────────────────────────────

    // Play icon SVG — identical to native Jellyfin
    const PLAY_SVG = '<svg viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg"><path d="M8 5v14l11-7z"/></svg>';

    const SETUP_HTML = `<div class="discover-error">
        &#9888;&#65039; <strong>TMDB API key not configured.</strong><br>
        Go to <strong>Dashboard &rarr; Plugins &rarr; Upcoming Movies &amp; Recommendations</strong> and enter your TMDB API key.<br>
        <a href="#/configurationpage?name=Upcoming Movies %26 Recommendations" style="color:#90caf9">Open Plugin Settings &rarr;</a>
    </div>`;

    function buildCard(tmdbId, title, posterUrl, date, streamBaseUrl, jellyfinId) {
        const card = document.createElement('div');
        card.className = 'discover-card';

        // Action buttons differ by card type
        let actionsHtml = '';
        if (jellyfinId) {
            // Watchlist card: View in Jellyfin + optional Stream
            actionsHtml = '<button class="btn-jellyfin" data-jellyfin="' + jellyfinId + '">\u25b6 Open</button>';
            if (tmdbId) {
                actionsHtml += '<button class="btn-stream" data-tmdb="' + tmdbId + '" data-stream-base="' + escapeHtml(streamBaseUrl) + '">Stream</button>';
            }
        } else if (tmdbId) {
            // TMDB card: Request + Stream
            actionsHtml = '<button class="btn-request" data-tmdb="' + tmdbId + '" data-title="' + escapeHtml(title) + '">Request</button>'
                + '<button class="btn-stream" data-tmdb="' + tmdbId + '" data-stream-base="' + escapeHtml(streamBaseUrl) + '">Stream</button>';
        }

        const posterHtml = posterUrl
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

        // Whole-card click → Jellyfin detail page
        card.addEventListener('click', function(e) {
            if (e.target.closest('button')) return;
            if (jellyfinId) {
                window.location.hash = '#/details?id=' + jellyfinId;
            }
        });

        // View in Jellyfin button
        var btnJ = card.querySelector('.btn-jellyfin');
        if (btnJ) btnJ.addEventListener('click', function(e) {
            e.stopPropagation();
            window.location.hash = '#/details?id=' + e.currentTarget.dataset.jellyfin;
        });

        // Request on Jellyseerr button
        var btnR = card.querySelector('.btn-request');
        if (btnR) btnR.addEventListener('click', function(e) {
            e.stopPropagation();
            handleRequest(e.currentTarget.dataset.tmdb, e.currentTarget.dataset.title, e.currentTarget);
        });

        // Stream Directly button
        var btnS = card.querySelector('.btn-stream');
        if (btnS) btnS.addEventListener('click', function(e) {
            e.stopPropagation();
            var base = (e.currentTarget.dataset.streamBase || streamBaseUrl).replace(/\/$/, '');
            window.open(base + '/' + e.currentTarget.dataset.tmdb, '_blank', 'noopener');
        });

        return card;
    }

    function renderTmdbCards(movies, containerEl, streamBaseUrl) {
        containerEl.innerHTML = '';
        if (!movies || movies.length === 0) {
            containerEl.innerHTML = '<div class="discover-error">No items found.</div>';
            return;
        }
        movies.forEach(function(movie) {
            var posterUrl = movie.poster_path ? TMDB_IMAGE_BASE + movie.poster_path : null;
            containerEl.appendChild(buildCard(movie.id, movie.title, posterUrl, movie.release_date, streamBaseUrl, null));
        });
    }

    function renderJellyfinCards(items, containerEl, streamBaseUrl) {
        containerEl.innerHTML = '';
        if (!items || items.length === 0) {
            containerEl.innerHTML = '<div class="discover-loading">Your watchlist is empty. Mark a movie as favourite in Jellyfin to add it here.</div>';
            return;
        }
        var token  = window.ApiClient && window.ApiClient.accessToken();
        var server = window.ApiClient && window.ApiClient._serverAddress;
        items.forEach(function(item) {
            var posterUrl = null;
            if (item.ImageTags && item.ImageTags.Primary && server) {
                posterUrl = server + '/Items/' + item.Id + '/Images/Primary?tag=' + item.ImageTags.Primary + '&quality=70&maxWidth=342&api_key=' + token;
            }
            var tmdbId = item.ProviderIds && item.ProviderIds.Tmdb ? item.ProviderIds.Tmdb : null;
            var date   = item.PremiereDate ? item.PremiereDate.substring(0, 10) : null;
            containerEl.appendChild(buildCard(tmdbId, item.Name, posterUrl, date, streamBaseUrl, item.Id));
        });
    }

    async function handleRequest(tmdbId, title, btn) {
        var original = btn.textContent;
        btn.textContent = 'Requesting\u2026';
        btn.disabled = true;
        try {
            var res = await fetch('/UpcomingMovies/jellyseerr/request', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'X-Emby-Authorization': getJellyfinAuthHeader() },
                body: JSON.stringify({ tmdbId: parseInt(tmdbId, 10), mediaType: 'movie' })
            });
            if (res.ok) {
                btn.textContent = '\u2713 Requested';
                btn.style.borderColor = '#2e7d32';
                btn.style.color = '#4caf50';
            } else {
                throw new Error('Failed');
            }
        } catch (err) {
            btn.textContent = 'Error';
            btn.style.borderColor = '#c62828';
            setTimeout(function() { btn.textContent = original; btn.style.borderColor = ''; btn.style.color = ''; btn.disabled = false; }, 3000);
        }
    }

    // ──────────────────────────────────────────────────────────
    // 6. BOOT — Populate a container div
    // ──────────────────────────────────────────────────────────

    var _renderingContainers = new Set();

    async function populateDiscoverContainer(containerDiv) {
        if (_renderingContainers.has(containerDiv)) return;
        _renderingContainers.add(containerDiv);
        LOG('Populating Discover container…', containerDiv);

        if (!containerDiv.querySelector('.discover-page-content')) {
            containerDiv.innerHTML = getGridTemplate();
        }

        var config = await fetchPluginConfig();
        var streamBaseUrl = config.streamBaseUrl || '';

        // Toggle section visibility from plugin config
        var upcomingSection = containerDiv.querySelector('[data-section="upcoming"]');
        var recommendedSection = containerDiv.querySelector('[data-section="recommended"]');
        var watchlistSection = containerDiv.querySelector('[data-section="watchlist"]');
        if (upcomingSection)   upcomingSection.classList.toggle('discover-hidden', !config.showUpcoming);
        if (recommendedSection) recommendedSection.classList.toggle('discover-hidden', !config.showRecommendations);
        if (watchlistSection)  watchlistSection.classList.toggle('discover-hidden', !config.showWatchlist);

        var upcBox = containerDiv.querySelector('.upcoming-items');
        var recBox = containerDiv.querySelector('.recommended-items');
        var watBox = containerDiv.querySelector('.watchlist-items');

        function isSetup(v) { return v && v._needsSetup; }

        // Fetch all sections in parallel
        var results = await Promise.all([
            config.showUpcoming      ? fetchUpcoming().catch(function(e)      { ERR('fetchUpcoming:', e);      return null; }) : null,
            config.showRecommendations ? fetchRecommendations().catch(function(e) { ERR('fetchRecommendations:', e); return null; }) : null,
            config.showWatchlist     ? fetchWatchlist().catch(function(e)     { ERR('fetchWatchlist:', e);     return null; }) : null,
        ]);
        var upc = results[0], rec = results[1], wat = results[2];

        if (upcBox) {
            if (isSetup(upc))  upcBox.innerHTML = SETUP_HTML;
            else if (upc)      renderTmdbCards(upc.results, upcBox, streamBaseUrl);
            else               upcBox.innerHTML = '<div class="discover-error">Failed to load Upcoming Movies. Check browser console for details.</div>';
        }
        if (recBox) {
            if (isSetup(rec))  recBox.innerHTML = SETUP_HTML;
            else if (rec)      renderTmdbCards(rec.results, recBox, streamBaseUrl);
            else               recBox.innerHTML = '<div class="discover-error">Failed to load Recommendations. Check browser console for details.</div>';
        }
        if (watBox) {
            if (wat)           renderJellyfinCards(wat.Items, watBox, streamBaseUrl);
            else               watBox.innerHTML = '<div class="discover-error">Failed to load Watchlist. Check browser console for details.</div>';
        }

        _renderingContainers.delete(containerDiv);
    }

    // ── INTEGRATION HOOKS ──

    // Hook A: MutationObserver for Custom Tabs `.upcoming-movies-plugin` div
    var observer = new MutationObserver(function() {
        document.querySelectorAll('.upcoming-movies-plugin').forEach(function(el) {
            if (!el.hasAttribute('data-discover-initialized')) {
                el.setAttribute('data-discover-initialized', 'true');
                populateDiscoverContainer(el);
            }
        });
    });
    observer.observe(document.body, { childList: true, subtree: true });

    // Hook B: Jellyfin native page events for #DiscoverPage (sidebar menu link)
    ['pageshow', 'viewshow', 'viewbeforeshow'].forEach(function(evtName) {
        document.addEventListener(evtName, function(e) {
            if (e.target && e.target.id === 'DiscoverPage') {
                var innerContainer = e.target.querySelector('.content-primary');
                if (innerContainer && !innerContainer.hasAttribute('data-discover-initialized')) {
                    innerContainer.setAttribute('data-discover-initialized', 'true');
                    populateDiscoverContainer(innerContainer);
                }
            }
        });
    });

    // Hook C: KefinTweaks Custom Tab — detect the 'Discover' tab in the header
    // and add a custom menu link for the sidebar if the user configured Header mode
    (function detectAndRegisterTab() {
        function tryInject() {
            var tabs = document.querySelectorAll('.headerTabs .headerTab, [data-tab]');
            tabs.forEach(function(tab) {
                var label = (tab.textContent || '').trim();
                if (label === 'Discover') {
                    var idx = tab.dataset.index || Array.from(tab.parentElement.children).indexOf(tab);
                    LOG('Found Discover Custom Tab at index', idx, '. Auto-injecting link…');
                    // KefinTweaks API to add a custom menu link with our Discover tab
                    if (window.KefinTweaksUtils && window.KeminTweaksUtils.addCustomMenuLink) {
                        window.KefinTweaksUtils.addCustomMenuLink({
                            name: 'Discover',
                            icon: 'explore',
                            url: '#/home?tab=' + idx,
                            openInNewTab: false
                        });
                    }
                }
            });
        }

        // Try immediately, then retry after a short delay to ensure KefinTweaks has loaded
        setTimeout(tryInject, 2000);
        setTimeout(tryInject, 5000);
    })();

    // ── INIT ──
    injectStyles();

    // Run once in case page is already loaded
    setTimeout(function() {
        document.querySelectorAll('.upcoming-movies-plugin').forEach(function(el) {
            if (!el.hasAttribute('data-discover-initialized')) {
                el.setAttribute('data-discover-initialized', 'true');
                populateDiscoverContainer(el);
            }
        });
    }, 800);

    LOG('discoverPage.js loaded and hooks registered.');
})();
