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

### Using the Included Build Automation (Windows)

1. Double-click the `build-release.bat` file in the root directory.
2. This will bundle `discoverPage.js` and other necessary files into a freshly generated `.zip` inside the `bin/Release/` directory.
3. The script also automatically computes the necessary MD5 checksum for the new zip and injects it into the matching `manifest.json`.
4. Your plugin is now ready to be deployed or updated in Jellyfin via the dashboard's "Repositories" feature.

If you don't use the `.bat` script, you must manually run the PowerShell script to generate the MD5 hash of your ZIP and paste it into `manifest.json`, otherwise Jellyfin will reject the installation with a "deserialization" or format error.

## Step 3: Configure the Native Frontend

Depending on where you want the Discover page to appear, follow one of the configurations below:

### Option A: Header Mode (Automatic KefinTweaks Style)
This option relies on the **Custom Tabs** and **JS Injector** plugins. Our plugin will automatically detect your Custom Tab and inject a "Discover" link into your Sidebar (which custom CSS themes then move into your Header).

1. Go to **Dashboard -> Plugins -> Custom Tabs**.
2. Add a new tab named **Discover**.
3. Set the HTML content of the tab exactly to:
   ```html
   <div class="sections upcoming-movies-plugin" style="padding: 1em;"></div>
   ```
   **CRITICAL WARNING:** Do NOT copy the iframe example (`<iframe src="YOUR_REQUEST_SERVICE_HERE">`) from KefinTweaks! That will cause a 404 error and a completely blank page. You MUST use the exact HTML `div` above so our Javascript can find the container and render the movies natively!
4. Go to **Dashboard -> Plugins -> JS Injector**.
5. Add a new Script named **Discover Plugin Script**.
6. Set the Script URL to the raw GitHub URL for `discoverPage.js` or copy the entire contents of `Jellyfin.Plugin.UpcomingMovies/Web/discoverPage.js` into the text box.
7. Restart Jellyfin or refresh your browser. The "Discover" link will automatically appear!

### Option B: Sidebar Mode (Manual Dashboard Routing)
If you don't use Custom Tabs, you can load the Discover page natively in the sidebar:

1. Go to **Dashboard -> Display -> Custom Menu Links JSON**.
2. Add a new entry like this:
   ```json
   {
       "name": "Discover",
       "icon": "explore",
       "url": "/UpcomingMovies/UI/Discover",
       "menuLocation": "Sidebar"
   }
   ```
3. Refresh the page. You'll now have a Discover button in the main sidebar that loads the full-page interface natively.

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
