
    // ─────────────────────────────────────────────────────────────────────────────
    // 6. RECOMMENDATIONS  (Phase 21 — server-side profile + scoring engine)
    // ─────────────────────────────────────────────────────────────────────────────
    //
    // The user's taste profile (genre/director/actor/language weights, watch history)
    // is maintained server-side and auto-updated by UserDataSavedConsumer whenever
    // a movie is played. This function simply passes userId to the backend and
    // receives a pre-scored, ranked list — no heavy client-side work needed.
    // ─────────────────────────────────────────────────────────────────────────────

    async function fetchRecommendations(page) {
        page = page || 1;
        var client = window.ApiClient;
        var userId = client && client.getCurrentUserId ? client.getCurrentUserId() : '';

        var params = ['page=' + page];
        if (userId) params.push('userId=' + encodeURIComponent(userId));

        var res = await fetch('/UpcomingMovies/tmdb/recommendations?' + params.join('&'), {
            headers: { 'X-Emby-Authorization': getJellyfinAuthHeader() }
        });
        if (res.status === 400 || res.status === 500) return NEEDS_SETUP;
        if (!res.ok) throw new Error('Recommendations error ' + res.status);
        return res.json();
    }
