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
            showWatchlist: true
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
    // 2. TEMPLATES
    // ──────────────────────────────────────────────────────────

    function injectStyles() {
        if (document.getElementById('discover-plugin-styles')) return;
        const style = document.createElement('style');
        style.id = 'discover-plugin-styles';
        style.innerHTML = `
            .discover-page-content { padding: 1.5rem 0; width: 100%; box-sizing: border-box; }
            .discover-section { margin-bottom: 2.5rem; }
            .discover-section-title { margin-bottom: 1rem; font-size: 1.25em; padding: 0 5%; font-weight: 500; }
            
            .discover-row {
                display: flex;
                overflow-x: auto;
                overflow-y: hidden;
                white-space: nowrap;
                padding-bottom: 15px;
                padding: 0 5%;
                scroll-snap-type: x mandatory;
                gap: 15px;
            }
            .discover-row::-webkit-scrollbar { height: 8px; }
            .discover-row::-webkit-scrollbar-track { background: rgba(0,0,0,0.1); border-radius: 4px; }
            .discover-row::-webkit-scrollbar-thumb { background: rgba(255,255,255,0.2); border-radius: 4px; }
            
            .discover-card {
                display: inline-block;
                position: relative;
                width: 160px;
                flex: 0 0 auto;
                scroll-snap-align: start;
                background: rgba(0,0,0,0.2);
                border-radius: var(--rounding, 4px);
                overflow: hidden;
                transition: transform 0.2s;
            }
            .discover-card:hover { transform: scale(1.05); }
            .discover-card img {
                width: 100%;
                height: 240px;
                object-fit: cover;
                display: block;
                background: #111;
            }
            .no-poster {
                width: 100%;
                height: 240px;
                display: flex;
                align-items: center;
                justify-content: center;
                background: #222;
                color: #fff;
                font-size: 2em;
            }
            
            .discover-card-overlay {
                position: absolute;
                top: 0; left: 0; right: 0; bottom: 0;
                background: rgba(0,0,0,0.85);
                display: flex;
                flex-direction: column;
                justify-content: center;
                align-items: center;
                opacity: 0;
                transition: opacity 0.2s;
                gap: 10px;
                padding: 10px;
            }
            .discover-card:hover .discover-card-overlay { opacity: 1; }
            .discover-card-overlay button {
                width: 100%;
                padding: 8px 5px;
                border: none;
                border-radius: 4px;
                cursor: pointer;
                font-size: 0.85em;
                font-weight: bold;
                color: #fff;
            }
            
            .btn-request { background-color: var(--theme-primary-color, #00a4dc); }
            .btn-request:hover { opacity: 0.8; }
            .btn-stream { background-color: #d32f2f; }
            .btn-stream:hover { background-color: #b71c1c; }
            
            .discover-card-title {
                padding: 8px 5px 2px 5px;
                font-size: 0.9em;
                white-space: nowrap;
                overflow: hidden;
                text-overflow: ellipsis;
            }
            .discover-card-date {
                padding: 0 5px 8px 5px;
                font-size: 0.8em;
                color: #aaa;
            }
            
            .discover-loading, .discover-error { padding: 20px 5%; color: #aaa; }
            .discover-error { color: #d32f2f; }
            .discover-hidden { display: none !important; }
        `;
        document.head.appendChild(style);
    }

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
        return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    // ──────────────────────────────────────────────────────────
    // 3. DATA FETCHING
    // ──────────────────────────────────────────────────────────

    const GENRE_MAP = {
        'Action': 28, 'Adventure': 12, 'Animation': 16, 'Comedy': 35,
        'Crime': 80, 'Documentary': 99, 'Drama': 18, 'Family': 10751,
        'Fantasy': 14, 'History': 36, 'Horror': 27, 'Music': 10402,
        'Mystery': 9648, 'Romance': 10749, 'Science Fiction': 878,
        'Sci-Fi': 878, 'Thriller': 53, 'War': 10752, 'Western': 37
    };

    async function fetchUpcoming() {
        const res = await fetch('/UpcomingMovies/tmdb/upcoming', { headers: { 'X-Emby-Authorization': getJellyfinAuthHeader() } });
        if (!res.ok) throw new Error('Failed to fetch upcoming');
        return res.json();
    }

    async function fetchRecommendations() {
        let genreIds = '';
        try {
            const userId = window.ApiClient?.getCurrentUserId();
            if (userId) {
                const histRes = await fetch(`${window.ApiClient._serverAddress}/Users/${userId}/Items?IncludeItemTypes=Movie&Filters=IsPlayed&SortBy=DatePlayed&SortOrder=Descending&Limit=30&Recursive=true`, { headers: { 'X-Emby-Token': window.ApiClient.accessToken() } });
                if (histRes.ok) {
                    const histData = await histRes.json();
                    const genreSet = new Set();
                    (histData.Items || []).forEach(item => (item.GenreItems || []).forEach(g => { if (GENRE_MAP[g.Name]) genreSet.add(GENRE_MAP[g.Name]); }));
                    genreIds = [...genreSet].slice(0, 3).join(',');
                }
            }
        } catch (err) { WARN('Genre lookup failed:', err); }

        const url = '/UpcomingMovies/tmdb/recommendations' + (genreIds ? `?genreIds=${genreIds}` : '');
        const res = await fetch(url, { headers: { 'X-Emby-Authorization': getJellyfinAuthHeader() } });
        if (!res.ok) throw new Error('Failed to fetch recommendations');
        return res.json();
    }

    async function fetchWatchlist() {
        const userId = window.ApiClient?.getCurrentUserId();
        if (!userId) throw new Error('Not logged in');
        const res = await fetch(`${window.ApiClient._serverAddress}/Users/${userId}/Items?IncludeItemTypes=Movie&Filters=IsLiked&SortBy=SortName&Recursive=true`, { headers: { 'X-Emby-Token': window.ApiClient.accessToken() } });
        if (!res.ok) throw new Error('Failed to fetch watchlist');
        return res.json();
    }

    // ──────────────────────────────────────────────────────────
    // 4. RENDERING ROUTINES
    // ──────────────────────────────────────────────────────────

    function renderCards(movies, containerEl, streamBaseUrl) {
        containerEl.innerHTML = '';
        if (!movies || movies.length === 0) {
            containerEl.innerHTML = '<div class="discover-error">No items found.</div>';
            return;
        }
        movies.forEach(movie => {
            const card = buildCard(movie.id, movie.title, movie.poster_path ? `${TMDB_IMAGE_BASE}${movie.poster_path}` : null, movie.release_date, streamBaseUrl);
            containerEl.appendChild(card);
        });
    }

    function renderJellyfinCards(items, containerEl, streamBaseUrl) {
        containerEl.innerHTML = '';
        if (!items || items.length === 0) {
            containerEl.innerHTML = '<div class="discover-loading">Your watchlist is empty. Like a movie in Jellyfin to add it here.</div>';
            return;
        }

        const token = window.ApiClient?.accessToken();
        const server = window.ApiClient?._serverAddress;

        items.forEach(item => {
            const posterUrl = (item.ImageTags?.Primary && server)
                ? `${server}/Items/${item.Id}/Images/Primary?tag=${item.ImageTags.Primary}&quality=70&maxWidth=342&api_key=${token}`
                : null;
            const card = buildCard(item.ProviderIds?.Tmdb || null, item.Name, posterUrl, item.PremiereDate?.substring(0, 10), streamBaseUrl);
            containerEl.appendChild(card);
        });
    }

    function buildCard(tmdbId, title, posterUrl, date, streamBaseUrl) {
        const card = document.createElement('div');
        card.className = 'discover-card';

        card.innerHTML = `
            ${posterUrl ? `<img src="${posterUrl}" alt="${escapeHtml(title)}" loading="lazy" />` : `<div class="no-poster">🎬</div>`}
            <div class="discover-card-overlay">
                ${tmdbId
                    ? `<button class="btn-request" data-tmdb="${tmdbId}" data-title="${escapeHtml(title)}">Request on Jellyseerr</button>
                       <button class="btn-stream"  data-tmdb="${tmdbId}" data-stream-base="${escapeHtml(streamBaseUrl)}">▶ Stream Directly</button>`
                    : `<span style="color:#aaa;font-size:0.8em">No TMDB ID</span>`
                }
            </div>
            <div class="discover-card-title" title="${escapeHtml(title)}">${escapeHtml(title)}</div>
            ${date ? `<div class="discover-card-date">${date}</div>` : ''}
        `;

        const btnReq = card.querySelector('.btn-request');
        if (btnReq) btnReq.addEventListener('click', e => { e.stopPropagation(); handleRequest(btnReq.dataset.tmdb, btnReq.dataset.title, btnReq); });

        const btnStream = card.querySelector('.btn-stream');
        if (btnStream) btnStream.addEventListener('click', e => {
            e.stopPropagation();
            const base = (btnStream.dataset.streamBase || streamBaseUrl).replace(/\/$/, '');
            window.open(`${base}/${btnStream.dataset.tmdb}`, '_blank', 'noopener');
        });

        return card;
    }

    async function handleRequest(tmdbId, title, btn) {
        const original = btn.textContent;
        btn.textContent = 'Requesting…';
        btn.disabled = true;
        try {
            const res = await fetch('/UpcomingMovies/jellyseerr/request', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'X-Emby-Authorization': getJellyfinAuthHeader() },
                body: JSON.stringify({ tmdbId: parseInt(tmdbId, 10), mediaType: 'movie' })
            });

            if (res.ok) {
                btn.textContent = '✓ Requested';
                btn.style.background = '#2e7d32';
            } else {
                throw new Error('Failed');
            }
        } catch (err) {
            btn.textContent = 'Error';
            btn.style.background = '#c62828';
            setTimeout(() => { btn.textContent = original; btn.style.background = ''; btn.disabled = false; }, 3000);
        }
    }

    // ──────────────────────────────────────────────────────────
    // 5. BOOT AND BIND TO DOM
    // ──────────────────────────────────────────────────────────

    let _renderingContainers = new Set();

    async function populateDiscoverContainer(containerDiv) {
        if (_renderingContainers.has(containerDiv)) return; // Already rendering
        _renderingContainers.add(containerDiv);
        LOG('Populating matching Discover container...', containerDiv);

        // Inject HTML if it's empty (Custom Tabs injects an empty div usually)
        if (!containerDiv.querySelector('.discover-page-content')) {
            containerDiv.innerHTML = getGridTemplate();
        }

        const config = await fetchPluginConfig();
        const streamBaseUrl = config.streamBaseUrl || '';

        // Determine visibility
        containerDiv.querySelector('[data-section="upcoming"]')?.classList.toggle('discover-hidden', !config.showUpcoming);
        containerDiv.querySelector('[data-section="recommended"]')?.classList.toggle('discover-hidden', !config.showRecommendations);
        containerDiv.querySelector('[data-section="watchlist"]')?.classList.toggle('discover-hidden', !config.showWatchlist);

        const upcBox = containerDiv.querySelector('.upcoming-items');
        const recBox = containerDiv.querySelector('.recommended-items');
        const watBox = containerDiv.querySelector('.watchlist-items');

        // Fetch concurrently
        const [upc, rec, wat] = await Promise.all([
            config.showUpcoming ? fetchUpcoming().catch(() => null) : null,
            config.showRecommendations ? fetchRecommendations().catch(() => null) : null,
            config.showWatchlist ? fetchWatchlist().catch(() => null) : null,
        ]);

        if (upcBox) { if (upc) renderCards(upc.results, upcBox, streamBaseUrl); else upcBox.innerHTML = '<div class="discover-error">Failed to load Upcoming Movies</div>'; }
        if (recBox) { if (rec) renderCards(rec.results, recBox, streamBaseUrl); else recBox.innerHTML = '<div class="discover-error">Failed to load Recommendations</div>'; }
        if (watBox) { if (wat) renderJellyfinCards(wat.Items, watBox, streamBaseUrl); else watBox.innerHTML = '<div class="discover-error">Failed to load Watchlist</div>'; }

        _renderingContainers.delete(containerDiv);
    }

    // ── INTEGRATION HOOKS ──

    // Hook A: Observe for Kefin Tweaks "Custom Tabs" `.upcoming-movies-plugin`
    const observer = new MutationObserver(mutations => {
        // The Custom Tabs plugin creates `.upcoming-movies-plugin` dynamically when navigating to Home
        document.querySelectorAll('.upcoming-movies-plugin').forEach(el => {
            if (!el.hasAttribute('data-discover-initialized')) {
                el.setAttribute('data-discover-initialized', 'true');
                populateDiscoverContainer(el);
            }
        });
    });
    observer.observe(document.body, { childList: true, subtree: true });

    // Hook B: Jellyfin Native Navigation Events for "#DiscoverPage" (Custom Menu Links Sidebar)
    ['pageshow', 'viewshow', 'viewbeforeshow'].forEach(evtName => {
        document.addEventListener(evtName, function (e) {
            if (e.target && e.target.id === 'DiscoverPage') {
                const innerContainer = e.target.querySelector('.content-primary');
                if (innerContainer && !innerContainer.hasAttribute('data-discover-initialized')) {
                    innerContainer.setAttribute('data-discover-initialized', 'true');
                    populateDiscoverContainer(innerContainer);
                }
            }
        });
    });

    // Run once on load just in case the elements are already there
    injectStyles();
    setTimeout(() => {
        document.querySelectorAll('.upcoming-movies-plugin, #DiscoverPage .content-primary').forEach(el => {
            if (!el.hasAttribute('data-discover-initialized')) {
                el.setAttribute('data-discover-initialized', 'true');
                populateDiscoverContainer(el);
            }
        });
    }, 500);

})();
