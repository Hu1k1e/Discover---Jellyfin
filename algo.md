# Recommendation Engine & Profiling Algorithm Reference

This document outlines the complete algorithm used by the `UpcomingMovies` plugin to build a user taste profile and calculate personalized movie recommendation scores.

---

## Part 1: Profile Building (Data Ingestion)

The system builds a dynamic profile by capturing implicitly (playback) and explicitly (watchlist) provided feedback.

### 1. Event Triggers
- **Real-time Engine:** Listens to Jellyfin's `UserDataSaved` events. Triggers when a user watches a movie (`Played = true`) or adds to watchlist (`Likes = true`).
- **Historical Sync:** The scheduled `SyncProfilesTask` runs nightly to retroactively ingest a user's entire watch history and watchlist.

### 2. Signal Types & Baselines
- **Watch Event:** Baseline Weight = `1.0`. Existing profile weights decay by `0.95`.
- **Watchlist Event:** Baseline Weight = `0.5`. Existing profile weights decay by `0.98`.

### 3. Implicit Feedback (Watch Percentage)
When a watch event occurs, the system calculates the `watchPercentage` (`PlaybackPositionTicks / RunTimeTicks`). This acts as an implicit feedback multiplier:
- **Abandoned** (`watchPercentage < 0.2` and no previous completions): `Multiplier = -0.5` (Penalizes the movie's traits)
- **Loved** (`watchPercentage > 0.9`): `Multiplier = 1.2`
- **Standard** (Otherwise): `Multiplier = 1.0`

### 4. Weight Distribution
The movie's metadata (Genres, Languages, Keywords, Directors, Actors) is fetched from TMDB. The system applies the calculated weight to the profile's associative dictionaries:

```csharp
EffectiveWeight = BaselineWeight * ImplicitFeedbackMultiplier * IterationDecay
```
- **Iteration Decay:** Used for lists (Genres, Keywords, People) to favor primary genres over tertiary ones. `IterationDecay = 1.0 / (index + 1)`.
- **Example:** For the primary genre, the weight added is `1.0 * 1.2 * 1.0 = +1.2`. For the secondary genre: `1.0 * 1.2 * 0.5 = +0.6`.

### 5. Profile Trimming
To keep storage and memory minimal, the profile is periodically trimmed:
- Genres/Languages: Top 50 kept
- Keywords: Top 100 kept
- Directors/Actors: Top 200 kept
- Recent Watches: Last 200 kept (Used for short-term recency scoring)

---

## Part 2: Candidate Generation (Sourcing)

The `TmdbController` queries the TMDB API in parallel to generate a "pool" of ~400-600 candidate movies across 11 different sourcing strategies:

1. **Watched Seeds:** `/recommendations` of the user's top recent/highly rated watches.
2. **Watchlist Seeds:** `/recommendations` of watchlisted unreleased/upcoming items.
3. **Similarity Seeds:** `/similar` based on user's absolute favorite movies.
4. **Genre Affinity:** Top movies strictly filtered by the user's top 3 genres.
5. **Language Affinity:** Top movies strictly filtered by the user's top 2 non-English languages (only triggers if user watches foreign films).
6. **Director Affinity:** Top 5 latest movies from the user's most highly weighted directors.
7. **Actor Affinity:** Top 5 latest movies from the user's most highly weighted actors.
8. **Trending:** General TMDB daily trending.
9. **Popular:** General TMDB all-time popular (vote > 7.0).
10. **Explicit Filtering:** Discovers based on UI dropdown language filters.
11. **Regional Diversity:** Always injects a small base of popular regional languages (Hindi, Tamil, Korean, etc.) to prevent filter bubbles.

*Each candidate is assigned a "Source Bonus" (e.g., Watched Seed = +30 pts, Popular = +8 pts) reflecting the confidence level of that discovery method.*

---

## Part 3: Scoring & Ranking (Two-Pass Pipeline)

Once candidates are pooled, the system scores them to pick the top 60.

### Math Helper: Log-Normalization `NW(weight)`
To prevent dominant attributes (e.g., watching 50 "Action" movies) from monopolizing the score, weights are scaled logarithmically:
`NW(x) = x > 0 ? log10(x + 1) : (x < 0 ? -log10(-x + 1) : 0)`

### First Pass Scoring (Applied to ALL Candidates)
For each candidate:

1. **Base Quality:** `(Vote Average * 7.0)`. (Max ~70 pts)
2. **Popularity:** `Math.Log10(Popularity + 1) * 6.0`. (Max ~20 pts)
3. **Source Bonus:** Initial seed confidence (0 to +30 pts).
4. **Recency:** `Max(0, 15.0 - (YearsOld * 1.5))`. (Max +15 pts for this year's releases).
5. **Genre Match:** `NW(GenreProfileWeight) * 2.0`.
6. **Genre Recency:** Up to `+8.0` pts based on what the user watched in the last ~90 days.
7. **Director / Actor Match:** 
   - Bonus dynamically scaled based on profile affinity. `NW(PersonWeight) * 1.5` / `* 1.0` respectively. 
8. **Language Modifier:**
   - Multiplicative coefficient. `Base = 0.4x`, scaling up to `1.15x`. 
   - If the user watches lots of foreign movies, the base floor dynamically rises from `0.4` to `0.85`, ensuring foreign films aren't unjustly purged from their queue.

### Second Pass Scoring (Applied to Top 100 Candidates ONLY)
To integrate niche "Keywords" (e.g., cyberpunk, heist) without hitting TMDB API rate limits (which cap at 40 req/sec):
1. The engine sorts the hundreds of candidates by their First Pass score.
2. It takes the **Top 100**.
3. It makes parallel API calls to fetch the `keywords` exclusively for those 100 movies.
4. **Keyword Match:** `NW(KeywordProfileWeight) * 3.0`.
   *(Niche tags are highly predictive, so they are heavily rewarded).*

### Final Polish & Serialization
1. **Tiered Diversification:** The top 60 slots are filled using a round-robin tier selection process ensuring that top-heavy recommendations don't accidentally consist of 60 movies from the exact same franchise or director.
2. The final 60 are JSON serialized and sent to the Jellyfin UI.
