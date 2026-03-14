/**
 * discoverPage.js
 * Jellyfin Plugin — Upcoming Movies & Recommendations
 * GitHub: https://github.com/Hu1k1e/jellyfin-plugin-upcoming-movies
 *
 * Responsibilities:
 *  1. Fetches the nav placement setting (Sidebar | Header) from the plugin config.
 *  2. Injects a "Discover" link into either the sidebar drawer OR the home page tab slider.
 *  3. Routes to the Discover content when the link/tab is clicked.
 *     - Sidebar mode routes to a standalone `#discoverPage.html`
 *     - Header mode dynamically injects a `.tabContent` block into the Home page.
 *  4. Fetches and renders three sections of movie cards (Upcoming, Recommended, Watchlist).
 *  5. Provides action buttons: Request on Jellyseerr & Stream Directly.
 */

(function () {
    'use strict';

    const LOG  = (...a) => console.log('[UpcomingMovies]', ...a);
    const WARN = (...a) => console.warn('[UpcomingMovies]', ...a);
    const ERR  = (...a) => console.error('[UpcomingMovies]', ...a);

    const PLUGIN_PAGE_HASH  = '#/discoverPage.html'; // Used for Sidebar mode
    const TMDB_IMAGE_BASE   = 'https://image.tmdb.org/t/p/w342';

    // ──────────────────────────────────────────────────────────
    // 1. PLUGIN CONFIG (cached after first fetch)
    // ──────────────────────────────────────────────────────────

    let _pluginConfig       = null;
    let _navInjected        = false;
    let _headerTabInjected  = false;

    /** Fetches public display settings from our proxy endpoint. API keys are never returned. */
    async function fetchPluginConfig() {
        if (_pluginConfig) return _pluginConfig;
        try {
            const res = await fetch('/UpcomingMovies/tmdb/config', {
                headers: { 'X-Emby-Authorization': getJellyfinAuthHeader() }
            });
            if (res.ok) {
                _pluginConfig = await res.json();
                LOG('Plugin config loaded:', _pluginConfig);
            }
        } catch (err) {
            WARN('Could not fetch plugin config, using defaults:', err);
        }
        // Defaults — safe fallback if the server is not reachable yet
        return _pluginConfig || {
            streamBaseUrl: '',
            navPlacement: 'Sidebar',
            showUpcoming: true,
            showRecommendations: true,
            showWatchlist: true
        };
    }

    // ──────────────────────────────────────────────────────────
    // 2. NAV INJECTION — SIDEBAR
    // ──────────────────────────────────────────────────────────

    function createSidebarLink() {
        const a = document.createElement('a');
        a.href = PLUGIN_PAGE_HASH;
        a.setAttribute('data-discover-nav', 'sidebar');
        a.className = 'navMenuOption navMenuOption-ltr';

        a.innerHTML = `
            <span class="navMenuOptionIcon">
                <svg xmlns="http://www.w3.org/2000/svg" class="navMenuOptionIcon-svg" viewBox="0 0 24 24" fill="currentColor">
                    <path d="M12 2l2.4 7.4H22l-6.2 4.5 2.4 7.4L12 17l-6.2 4.3 2.4-7.4L2 9.4h7.6z"/>
                </svg>
            </span>
            <span class="navMenuOptionText">Discover</span>
        `;
        return a;
    }

    function injectSidebarLink() {
        if (_navInjected) return;
        if (document.querySelector('[data-discover-nav]')) { _navInjected = true; return; }

        const candidates = [
            '.mainDrawer .navMenuOptions',
            '.mainDrawer-scrollContainer .navMenuOptions',
            '.navDrawerContent .navMenuOptions',
        ];

        let navList = null;
        for (const sel of candidates) {
            navList = document.querySelector(sel);
            if (navList) break;
        }

        if (!navList) {
            const drawer = document.querySelector('.mainDrawer');
            if (drawer) navList = drawer.querySelector('nav') || drawer.querySelector('[class*="navMenu"]');
        }

        if (!navList) return;

        navList.appendChild(createSidebarLink());
        _navInjected = true;
        LOG('Sidebar link injected');
        syncSidebarActiveState();
    }

    function syncSidebarActiveState() {
        const link = document.querySelector('[data-discover-nav="sidebar"]');
        if (!link) return;

        const isDiscoverPage = window.location.hash.startsWith('#/discoverPage');
        document.querySelectorAll('.navMenuOption').forEach(el => el.classList.remove('navMenuOption-selected'));
        if (isDiscoverPage) link.classList.add('navMenuOption-selected');
    }

    // ──────────────────────────────────────────────────────────
    // 3. NAV INJECTION — HEADER TAB BAR (Custom Tabs Pattern)
    // ──────────────────────────────────────────────────────────

    /**
     * Injects a custom tab into the Home page tabs slider.
     * Based on IAmParadox27's Custom Tabs pattern.
     */
    function injectHeaderTab() {
        const hash = window.location.hash;
        if (hash !== '' && hash !== '#/home' && hash !== '#/home.html' && !hash.includes('#/home?') && !hash.includes('#/home.html?')) {
            return; // Only inject header tabs on the home page
        }

        const tabsSlider = document.querySelector('.emby-tabs-slider');
        const favoritesTab = document.querySelector('#favoritesTab');
        
        if (!tabsSlider || !favoritesTab) {
            return; // Not ready yet
        }

        if (document.getElementById('discoverHeaderTabBtn')) {
            _headerTabInjected = true;
            return; // Already exists
        }

        // 1. Create the Button in the slider
        // Determine the next index (Favorites is usually index 1, so we take the next available)
        const tabButtons = tabsSlider.querySelectorAll('.emby-tab-button');
        const tabIndex = tabButtons.length; 

        const btnTitle = document.createElement('div');
        btnTitle.className = 'emby-button-foreground';
        btnTitle.innerText = 'Discover';

        const btn = document.createElement('button');
        btn.type = 'button';
        btn.setAttribute('is', 'emby-button');
        btn.className = 'emby-tab-button emby-button';
        btn.setAttribute('data-index', tabIndex);
        btn.id = 'discoverHeaderTabBtn';
        btn.appendChild(btnTitle);

        tabsSlider.appendChild(btn);

        // 2. Create the hidden Content Container next to the favorites container
        // Jellyfin core logic will automatically show/hide this based on data-index matching
        const contentContainer = document.createElement('div');
        contentContainer.className = 'tabContent pageTabContent';
        contentContainer.id = 'discoverHeaderTabContent';
        contentContainer.setAttribute('data-index', tabIndex);
        contentContainer.style.display = 'none'; // Hidden by default

        // Load the HTML structure from our plugin endpoint or inline it
        contentContainer.innerHTML = getDiscoverHtmlTemplate();
        
        // Insert it as a sibling to the favorites tab content
        favoritesTab.parentElement.insertBefore(contentContainer, favoritesTab.nextSibling);

        _headerTabInjected = true;
        LOG(`Header tab injected (Index: ${tabIndex})`);

        // Jellyfin's tabs component doesn't inherently notify us when our custom tab becomes active 
        // using the native events easily, so we observe the style changes on our container to trigger the render.
        const observer = new MutationObserver(mutations => {
            mutations.forEach(mutation => {
                if (mutation.attributeName === 'style') {
                    const isVisible = contentContainer.style.display !== 'none';
                    if (isVisible) {
                        contentContainer.classList.add('is-active-tab'); // Native Jellyfin class helps with scrolling
                        renderDiscoverPage();
                    } else {
                        contentContainer.classList.remove('is-active-tab');
                    }
                }
            });
        });

        observer.observe(contentContainer, { attributes: true, attributeFilter: ['style'] });
    }

    /** The raw HTML structure to inject into the tab content. (Copied from discoverPage.html) */
    function getDiscoverHtmlTemplate() {
        return `
            <style>
                /* Native Jellyfin component overrides for our layout */
                .discover-page-content { padding: 1.5rem 0; width: 100%; box-sizing: border-box; }
                .discover-section { margin-bottom: 2.5rem; }
                .discover-section-title { margin-bottom: 1rem; font-size: 1.25em; padding: 0 5%; font-weight: 500; }
                
                .discover-row {
                    display: flex;
                    overflow-x: auto;
                    overflow-y: hidden;
                    white-space: nowrap;
                    padding-bottom: 15px; /* for scrollbar */
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
                    border-radius: var(--rounding);
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
                
                .btn-request { background-color: #00a4dc; }
                .btn-request:hover { background-color: #008ebf; }
                
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
            </style>
            <div class="discover-page-content">
                <div id="discoverUpcomingSection" class="discover-section">
                    <h2 class="discover-section-title">Upcoming Movies</h2>
                    <div id="discoverUpcomingItems" class="discover-row">
                        <div class="discover-loading">Loading upcoming movies...</div>
                    </div>
                </div>
                <div id="discoverRecommendedSection" class="discover-section">
                    <h2 class="discover-section-title">Recommended For You</h2>
                    <div id="discoverRecommendedItems" class="discover-row">
                        <div class="discover-loading">Loading recommendations...</div>
                    </div>
                </div>
                <div id="discoverWatchlistSection" class="discover-section">
                    <h2 class="discover-section-title">My Watchlist</h2>
                    <div id="discoverWatchlistItems" class="discover-row">
                        <div class="discover-loading">Loading watchlist...</div>
                    </div>
                </div>
            </div>
        `;
    }

    // ──────────────────────────────────────────────────────────
    // 4. DOM WATCHING
    //    Jellyfin is a SPA — the sidebar/header may render lazily.
    //    Use MutationObserver to retry injection as DOM changes.
    // ──────────────────────────────────────────────────────────

    async function startNavWatcher() {
        const config = await fetchPluginConfig();
        const placement = (config.navPlacement || 'Sidebar').toLowerCase();

        const tryInject = () => {
            if (placement === 'header') {
                injectHeaderTab();
            } else {
                injectSidebarLink();
            }
        };

        // Watch for DOM changes (necessary because Jellyfin mounts/unmounts complete views on navigation)
        const observer = new MutationObserver(tryInject);
        observer.observe(document.body, { childList: true, subtree: true });

        // Handle navigation events
        const handleNav = () => {
            // Give DOM a moment to rebuild the new view before injecting
            setTimeout(() => {
                _navInjected = false;
                _headerTabInjected = false;
                tryInject();
                if (placement === 'sidebar') syncSidebarActiveState();
            }, 300);
        };

        window.addEventListener('hashchange', handleNav);
        window.addEventListener('popstate', handleNav);

        // Monkey patch pushState to catch all navigations
        const originalPushState = history.pushState;
        history.pushState = function() {
            originalPushState.apply(history, arguments);
            handleNav();
        };

        // Try immediately
        setTimeout(tryInject, 100);
    }

    // ──────────────────────────────────────────────────────────
    // 5. PAGE EVENT LISTENER (For Sidebar mode only)
    // ──────────────────────────────────────────────────────────

    function attachPageEvents() {
        // When using the standalone sidebar page, we catch these events to render
        ['pageshow', 'viewshow', 'viewbeforeshow'].forEach(evtName => {
            document.addEventListener(evtName, function (e) {
                if (e.target && e.target.id === 'DiscoverPage') {
                    renderDiscoverPage();
                }
            });
        });
    }

    // ──────────────────────────────────────────────────────────
    // 6. DATA FETCHING
    // ──────────────────────────────────────────────────────────

    async function fetchUpcoming() {
        const res = await fetch('/UpcomingMovies/tmdb/upcoming', {
            headers: { 'X-Emby-Authorization': getJellyfinAuthHeader() }
        });
        if (!res.ok) throw new Error('Failed to fetch upcoming movies from proxy');
        return res.json();
    }

    async function fetchRecommendations() {
        let genreIds = '';
        try {
            const userId        = window.ApiClient?.getCurrentUserId();
            const serverAddress = window.ApiClient?._serverAddress;
            const token         = window.ApiClient?.accessToken ? window.ApiClient.accessToken() : '';

            if (userId && serverAddress && token) {
                const histRes = await fetch(
                    `${serverAddress}/Users/${userId}/Items?IncludeItemTypes=Movie&Filters=IsPlayed&SortBy=DatePlayed&SortOrder=Descending&Limit=30&Recursive=true`,
                    { headers: { 'X-Emby-Token': token } }
                );
                if (histRes.ok) {
                    const histData = await histRes.json();
                    const genreSet = new Set();
                    (histData.Items || []).forEach(item =>
                        (item.GenreItems || []).forEach(g => {
                            const id = GENRE_MAP[g.Name];
                            if (id) genreSet.add(id);
                        })
                    );
                    genreIds = [...genreSet].slice(0, 3).join(',');
                }
            }
        } catch (err) {
            WARN('Genre lookup failed, using generic recommendations:', err);
        }

        const url = '/UpcomingMovies/tmdb/recommendations' + (genreIds ? `?genreIds=${genreIds}` : '');
        const res = await fetch(url, { headers: { 'X-Emby-Authorization': getJellyfinAuthHeader() } });
        if (!res.ok) throw new Error('Failed to fetch recommendations from proxy');
        return res.json();
    }

    async function fetchWatchlist() {
        const userId        = window.ApiClient?.getCurrentUserId();
        const serverAddress = window.ApiClient?._serverAddress;
        const token         = window.ApiClient?.accessToken ? window.ApiClient.accessToken() : '';
        if (!userId || !serverAddress || !token) throw new Error('Not authenticated');

        const res = await fetch(
            `${serverAddress}/Users/${userId}/Items?IncludeItemTypes=Movie&Filters=IsLiked&SortBy=SortName&Recursive=true`,
            { headers: { 'X-Emby-Token': token } }
        );
        if (!res.ok) throw new Error('Failed to fetch watchlist');
        return res.json();
    }

    // Genre name → TMDB genre ID lookup map
    const GENRE_MAP = {
        'Action': 28, 'Adventure': 12, 'Animation': 16, 'Comedy': 35,
        'Crime': 80, 'Documentary': 99, 'Drama': 18, 'Family': 10751,
        'Fantasy': 14, 'History': 36, 'Horror': 27, 'Music': 10402,
        'Mystery': 9648, 'Romance': 10749, 'Science Fiction': 878,
        'Sci-Fi': 878, 'Thriller': 53, 'War': 10752, 'Western': 37
    };

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
    // 7. CARD RENDERING
    // ──────────────────────────────────────────────────────────

    function renderCards(movies, containerId, streamBaseUrl) {
        const container = document.getElementById(containerId);
        if (!container) return;
        container.innerHTML = '';
        if (!movies || movies.length === 0) {
            container.innerHTML = '<div class="discover-error">No items found.</div>';
            return;
        }
        movies.forEach(movie => {
            const card = buildCard(
                movie.id,
                movie.title,
                movie.poster_path ? `${TMDB_IMAGE_BASE}${movie.poster_path}` : null,
                movie.release_date,
                streamBaseUrl
            );
            container.appendChild(card);
        });
    }

    function renderJellyfinCards(items, containerId, streamBaseUrl) {
        const container = document.getElementById(containerId);
        if (!container) return;
        container.innerHTML = '';
        if (!items || items.length === 0) {
            container.innerHTML = '<div class="discover-loading">Your watchlist is empty. Like a movie in Jellyfin to add it here.</div>';
            return;
        }

        const serverAddress = window.ApiClient?._serverAddress || '';
        const token         = window.ApiClient?.accessToken ? window.ApiClient.accessToken() : '';

        items.forEach(item => {
            const tmdbId    = item.ProviderIds?.Tmdb;
            const imageTag  = item.ImageTags?.Primary;
            const posterUrl = (imageTag && serverAddress)
                ? `${serverAddress}/Items/${item.Id}/Images/Primary?tag=${imageTag}&quality=70&maxWidth=342&api_key=${token}`
                : null;

            const card = buildCard(
                tmdbId || null,
                item.Name || 'Unknown',
                posterUrl,
                item.PremiereDate?.substring(0, 10),
                streamBaseUrl
            );
            container.appendChild(card);
        });
    }

    function buildCard(tmdbId, title, posterUrl, date, streamBaseUrl) {
        const card = document.createElement('div');
        card.className = 'discover-card';

        card.innerHTML = `
            ${posterUrl
                ? `<img src="${posterUrl}" alt="${escapeHtml(title)}" loading="lazy" />`
                : `<div class="no-poster">🎬</div>`
            }
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

        const btnRequest = card.querySelector('.btn-request');
        if (btnRequest) btnRequest.addEventListener('click', e => {
            e.stopPropagation();
            handleRequest(parseInt(btnRequest.dataset.tmdb, 10), btnRequest.dataset.title, btnRequest);
        });

        const btnStream = card.querySelector('.btn-stream');
        if (btnStream) btnStream.addEventListener('click', e => {
            e.stopPropagation();
            const base = (btnStream.dataset.streamBase || streamBaseUrl).replace(/\/$/, '');
            window.open(`${base}/${btnStream.dataset.tmdb}`, '_blank', 'noopener');
        });

        return card;
    }

    // ──────────────────────────────────────────────────────────
    // 8. ACTION HANDLERS
    // ──────────────────────────────────────────────────────────

    async function handleRequest(tmdbId, title, btn) {
        if (!tmdbId) return;
        const original   = btn.textContent;
        btn.textContent  = 'Requesting…';
        btn.disabled     = true;

        try {
            const res = await fetch('/UpcomingMovies/jellyseerr/request', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'X-Emby-Authorization': getJellyfinAuthHeader()
                },
                body: JSON.stringify({ tmdbId, mediaType: 'movie' })
            });

            if (res.ok) {
                btn.textContent   = '✓ Requested';
                btn.style.background = '#2e7d32';
            } else {
                const err = await res.json().catch(() => ({}));
                btn.textContent      = 'Error';
                btn.style.background = '#c62828';
                if (window.Dashboard?.alert) Dashboard.alert({ message: err.error || 'Jellyseerr request failed.' });
                setTimeout(() => { btn.textContent = original; btn.style.background = ''; btn.disabled = false; }, 3000);
            }
        } catch (err) {
            ERR('Request error:', err);
            btn.textContent = 'Error';
            setTimeout(() => { btn.textContent = original; btn.style.background = ''; btn.disabled = false; }, 3000);
        }
    }

    // ──────────────────────────────────────────────────────────
    // 9. MAIN PAGE RENDER
    // ──────────────────────────────────────────────────────────

    let _rendering = false;

    async function renderDiscoverPage() {
        if (_rendering) return;
        _rendering = true;
        LOG('Rendering Discover page…');

        const config        = await fetchPluginConfig();
        const streamBaseUrl = config.streamBaseUrl || '';

        // Section visibility
        document.getElementById('discoverUpcomingSection')?.classList
            .toggle('discover-hidden', !config.showUpcoming);
        document.getElementById('discoverRecommendedSection')?.classList
            .toggle('discover-hidden', !config.showRecommendations);
        document.getElementById('discoverWatchlistSection')?.classList
            .toggle('discover-hidden', !config.showWatchlist);

        // Fetch all three data sources in parallel
        const [upcoming, recommended, watchlist] = await Promise.all([
            config.showUpcoming        ? fetchUpcoming().catch(e        => { ERR('upcoming:', e);        return null; }) : null,
            config.showRecommendations ? fetchRecommendations().catch(e => { ERR('recommends:', e);      return null; }) : null,
            config.showWatchlist       ? fetchWatchlist().catch(e       => { ERR('watchlist:', e);       return null; }) : null,
        ]);

        if (upcoming)    renderCards(upcoming.results || [],    'discoverUpcomingItems',    streamBaseUrl);
        if (recommended) renderCards(recommended.results || [], 'discoverRecommendedItems', streamBaseUrl);
        if (watchlist)   renderJellyfinCards(watchlist.Items || [], 'discoverWatchlistItems', streamBaseUrl);

        // Error states for failed fetches
        if (config.showUpcoming && !upcoming) {
            const el = document.getElementById('discoverUpcomingItems');
            if (el) el.innerHTML = '<div class="discover-error">Could not load upcoming movies. Check your TMDB API key in plugin settings.</div>';
        }
        if (config.showRecommendations && !recommended) {
            const el = document.getElementById('discoverRecommendedItems');
            if (el) el.innerHTML = '<div class="discover-error">Could not load recommendations. Check your TMDB API key in plugin settings.</div>';
        }
        if (config.showWatchlist && !watchlist) {
            const el = document.getElementById('discoverWatchlistItems');
            if (el) el.innerHTML = '<div class="discover-error">Could not load watchlist. Please ensure you are logged in.</div>';
        }

        LOG('Render complete');
        _rendering = false;
    }

    // ──────────────────────────────────────────────────────────
    // 10. UTILITIES
    // ──────────────────────────────────────────────────────────

    function escapeHtml(str) {
        if (!str) return '';
        return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    // ──────────────────────────────────────────────────────────
    // 11. BOOT
    // ──────────────────────────────────────────────────────────

    async function init() {
        LOG('Booting (Jellyfin Upcoming Movies Plugin v1.0.0)');
        await startNavWatcher();
        attachPageEvents(); // Active only for sidebar mode
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        setTimeout(init, 150); // Small delay to let Jellyfin SPA shell settle
    }

})();
