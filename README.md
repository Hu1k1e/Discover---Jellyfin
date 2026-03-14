# Upcoming Movies & Recommendations — Jellyfin Plugin

A native Jellyfin Plugin that adds a **Discover** page with:
- 🎬 **Upcoming Movies** — sourced from TMDB
- ⭐ **Recommended For You** — personalised by your Jellyfin watch history genres
- 📋 **My Watchlist** — movies you have liked in Jellyfin (KefinTweaks-compatible)

Each card supports:
- **Request on Jellyseerr** — submit a download request to your Jellyseerr instance
- **▶ Stream Directly** — open in your configured streaming URL

The **Discover** nav link can be placed in either the **sidebar** or the **header tab bar** — configurable per user in plugin settings.

---

## ⚡ Quick Install via Plugin Repository (Recommended)

The easiest way: add this repo URL to Jellyfin and install from the catalog.

### Step 1 — Add the repository in Jellyfin

1. Open Jellyfin web client
2. Go to **☰ → Dashboard → Plugins → Repositories** (top right)
3. Click the **+** button
4. Enter **any name** (e.g. `H-TV Plugins`) and paste this URL:
   ```
   https://raw.githubusercontent.com/Hu1k1e/jellyfin-plugin-upcoming-movies/main/manifest.json
   ```
5. Click **Save**

### Step 2 — Install the plugin

1. Go to **Dashboard → Plugins → Catalog**
2. Search for **Upcoming Movies & Recommendations**
3. Click it → **Install**
4. **Restart Jellyfin** when prompted

### Step 3 — Configure the plugin

1. After restart, go to **Dashboard → Plugins → Upcoming Movies & Recommendations → Settings**
2. Fill in:

| Setting | Description |
|---|---|
| **TMDB API Key** | Get free at [themoviedb.org/settings/api](https://www.themoviedb.org/settings/api) |
| **Jellyseerr URL** | Your Jellyseerr instance URL, e.g. `https://jellyseerr.yourdomain.com` |
| **Jellyseerr API Key** | Jellyseerr → Settings → General → API Key |
| **Stream Base URL** | e.g. `https://stream.hulksmash.ca/movie/` — TMDB ID is appended |
| **Discover Link Location** | `Sidebar` (left drawer) or `Header` (top tab bar) |
| **Section toggles** | Show/hide Upcoming, Recommended, Watchlist independently |

3. Click **Save**
4. **Hard-refresh the browser** (Ctrl+Shift+R) to see the Discover link appear

---

## 🔧 Publishing a New Release (for developers)

The GitHub Actions workflow handles everything automatically.

### Step 1 — Push the code to GitHub

If this is the first push:
```powershell
git init
git add .
git commit -m "feat: initial plugin"
git remote add origin https://github.com/Hu1k1e/jellyfin-plugin-upcoming-movies.git
git push -u origin main
```

### Step 2 — Tag the release

```powershell
git tag v1.0.0
git push origin v1.0.0
```

### Step 3 — What happens automatically

The GitHub Actions workflow (`.github/workflows/build-release.yml`) will:
1. Check out the code and install .NET 9 SDK
2. Build `Jellyfin.Plugin.UpcomingMovies.dll` in Release mode
3. Create `jellyfin-plugin-upcoming-movies_v1.0.0.zip` (DLL only)
4. Compute the **SHA256 checksum** of the ZIP
5. Update `manifest.json` with the correct download URL, checksum, and timestamp
6. Commit the updated manifest back to `main`
7. Create a **GitHub Release** with the ZIP attached

### Step 4 — Verify the manifest URL is correct

After the release is published, visit:
```
https://raw.githubusercontent.com/Hu1k1e/jellyfin-plugin-upcoming-movies/main/manifest.json
```
and confirm the `sourceUrl` and `checksum` fields have been populated.

---

## 🏗️ Building Locally (Optional)

Requires: [.NET 9 SDK](https://dotnet.microsoft.com/download)

```powershell
cd Jellyfin.Plugin.UpcomingMovies
dotnet build --configuration Release -o ../publish
```

To install manually:
1. Copy `Jellyfin.Plugin.UpcomingMovies.dll` to your Jellyfin plugins directory:
   - Linux: `~/.local/share/jellyfin/plugins/UpcomingMovies/`
   - Docker: map a volume to the container's `/config/plugins/UpcomingMovies/`
   - Windows: `%APPDATA%\Jellyfin\plugins\UpcomingMovies\`
2. Restart Jellyfin

---

## 🧱 Architecture

```
Browser JS (discoverPage.js)
    ↓  fetches navPlacement + section visibility
    ↓  fetches movie data
Plugin API Controllers (C#)
    ↓  TMDB proxy with server-side API key
    ↓  Jellyseerr proxy with server-side API key
External APIs (TMDB, Jellyseerr)
```

**API keys are stored exclusively in Jellyfin's plugin configuration and never sent to the browser.**

---

## 🔗 Watchlist Compatibility

Uses `GET /Items?Filters=IsLiked` — the same endpoint KefinTweaks uses for its Watchlist. Adding items via KefinTweaks will appear in the Discover Watchlist section and vice versa.
