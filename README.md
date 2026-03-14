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
   https://raw.githubusercontent.com/Hu1k1e/Discover---Jellyfin/main/manifest.json
   ```
5. Click **Save**

### Step 2 — Install the plugin

1. Go to **Dashboard → Plugins → Catalog**
2. Search for **Upcoming Movies & Recommendations**
3. Click it → **Install**
4. **Restart Jellyfin** when prompted

### Step 2 — Configure the Plugin

1. Go to **Dashboard → Plugins → Upcoming Movies & Recommendations → Settings**
2. Fill out all the necessary API configuration (TMDB, Jellyseerr, Stream Base URL).
3. Choose your preferred **Nav Placement** (Sidebar or Header).
4. Click **Save**.

### Step 3 — Setup the UI Integrations (CRITICAL)

Because this is a native C# plugin without invasive core-file patching, you must configure Jellyfin to display the "Discover" tab using your preferred method.

**First, load the background script:**
1. Install the **JS Injector** Jellyfin plugin.
2. In JS Injector settings, add a new script with the following URL:
   `/web/ConfigurationPage?name=discoverPage.js`

**Next, choose your display location:**

#### Option A: Sidebar (Native)
1. Go to **Dashboard → Display** in Jellyfin.
2. Under **Custom Menu Links JSON**, add a new link pointing to:
   `#!/configurationpage?name=discoverPage`
3. Save and refresh.

#### Option B: Header (via Custom Tabs)
1. Install the **Custom Tabs** and **File Transformation** plugins.
2. In Custom Tabs, create a new tab named **Discover**.
3. Set the HTML content of the tab to:
   `<div class="sections upcoming-movies-plugin"></div>`
4. The plugin will automatically detect this tab and render the movie grid inside it when clicked.

| Setting | Description |
|---|---|
| **TMDB API Key** | Get free at [themoviedb.org/settings/api](https://www.themoviedb.org/settings/api) |
| **Jellyseerr URL** | Your Jellyseerr instance URL, e.g. `https://jellyseerr.yourdomain.com` |
| **Jellyseerr API Key** | Jellyseerr → Settings → General → API Key |
| **Stream Base URL** | e.g. `https://stream.hulksmash.ca/movie/` — TMDB ID is appended |
| **Section toggles** | Show/hide Upcoming, Recommended, Watchlist independently |

---

## 🔧 Publishing a New Release (for developers)

The GitHub Actions workflow handles everything automatically.

### Step 1 — Push the code to GitHub

If this is the first push:
```powershell
git init
git add .
git commit -m "feat: initial plugin"
git remote add origin https://github.com/Hu1k1e/Discover---Jellyfin.git
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
2. Zip the compiled `.dll`
3. Calculate its **MD5 checksum**
4. Create `jellyfin-plugin-upcoming-movies_v1.0.0.zip` (DLL only)
5. Compute the **MD5 checksum** of the ZIP
6. Update `manifest.json` with the correct download URL, checksum, and timestamp
7. Commit the updated manifest back to `main`
8. Create a **GitHub Release** with the ZIP attached

### Step 4 — Verify the manifest URL is correct

After the release is published, visit:
```
https://raw.githubusercontent.com/Hu1k1e/Discover---Jellyfin/main/manifest.json
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
1. Extract `Jellyfin.Plugin.UpcomingMovies.dll` and place it in your plugins directory. The GitHub Release also contains an MD5 checksum for verification.
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
