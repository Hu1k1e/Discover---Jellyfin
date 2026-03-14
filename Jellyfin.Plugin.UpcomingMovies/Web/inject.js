/**
 * inject.js — Upcoming Movies & Recommendations Plugin
 *
 * This tiny script is inlined into Jellyfin's index.html by the
 * File Transformation Plugin at startup (same technique as Custom Tabs).
 *
 * It dynamically loads discoverPage.js from the plugin's embedded
 * resource endpoint, so it runs on every page without any JS Injector
 * or manual user configuration.
 */
(function () {
    'use strict';

    // The plugin exposes discoverPage.js via Plugin.cs → GetPages()
    // at /web/ConfigurationPage?name=discoverPage.js
    var scriptUrl = '/web/ConfigurationPage?name=discoverPage.js';

    function loadScript(src) {
        var s = document.createElement('script');
        s.src = src;
        s.async = true;
        s.onerror = function () {
            console.warn('[UpcomingMovies] Failed to load', src);
        };
        document.head.appendChild(s);
    }

    // Load immediately — the DOMContentLoaded guarantee comes from the
    // <script defer> wrapper injected by TransformationPatches.cs
    loadScript(scriptUrl);
})();
