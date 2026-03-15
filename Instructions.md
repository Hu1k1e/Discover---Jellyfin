# Agent Operating Guide

This document defines how the AI agent must operate when modifying or building this project. It contains general rules, structural guidelines, and formatting requirements.

The agent must always follow both:
- `instructions.md` → defines how the system should operate
- `project_specs.md` → defines what the specific project is and its current state

The agent must read both files before taking any action.

---

# 1. Always Understand the Project First

Before writing any code, the agent must:
1. Read `project_specs.md` to understand the current phase and goal.
2. Inspect the repository structure.
3. Identify the backend architecture, frontend injection methods, and state management.
4. Explain how the system currently works or how the new feature will integrate.
5. Wait for approval before performing major architectural changes.

The agent must never start coding blindly.

---

# 2. General Project Structure

This project follows a standard Jellyfin Plugin structure:

Plugin/
    Configuration/
        PluginConfiguration.cs
    Api/
        [API Controllers]
    Web/
        [Injected JS/HTML for the UI]
    Plugin.cs
    ServerEntryPoint.cs
    [Other C# source files]

The backend (C#) handles the Jellyfin server lifecycle, user settings, and API routing. The frontend (JS/HTML) handles UI injection into the Jellyfin Web Client.

---

# 3. Architecture Principles

The system follows a native integration approach:

Jellyfin Web Client (Injected UI)  
↓  
Jellyfin Plugin API Endpoints (C#)  
↓  
External APIs & Internal Jellyfin Database  

The agent must preserve this architecture. Do not introduce external dependencies that conflict with Jellyfin's core build.

---

# 4. Development Rules

## Rule 1 — Read First
Before modifying code, always read the specs, the relevant Jellyfin plugin documentation, and the files you intend to change.

## Rule 2 — Do Not Mix Responsibilities
Backend controllers must never contain UI rendering logic. Frontend scripts must avoid hardcoding API keys; all sensitive data must be routed securely through the plugin's configuration layer.

## Rule 3 — Modify the Smallest Scope
1. Identify the minimal change required.
2. Implement it.
3. Verify that existing Jellyfin behavior is not disrupted.

## Rule 4 — Build in Small Steps
Never implement multiple major systems at once. Build one feature, test it, validate it, and move to the next.

## Rule 5 — Configuration Handling
All customizable variables (API keys, display preferences) must be exposed on the plugin's settings page and stored in the configuration file. Hardcoding is strictly forbidden.

---

# 5. Deployment Environment

The system runs as a dynamically loaded `.dll` inside a Jellyfin server environment. The agent must ensure the project builds against the correct target framework for modern Jellyfin releases and that web assets are correctly embedded or served.

---

# 6. When Something Breaks

If an error occurs:
1. Identify the root cause (e.g., API version mismatch, CORS issue).
2. Fix the underlying issue.
3. Prevent the failure from recurring.
4. Test again.

Never apply superficial fixes.

---

# 7. Response Format

When responding, always follow this format:

Plan  
(3–7 bullet points explaining the approach)

What I need from you  
(only if something is required)

Next Action  
(one clear next step)

Errors  
(explain clearly if something failed)

---

# 8. Documenting Progress

The agent is responsible for keeping `project_specs.md` up to date. After completing a significant phase or feature, the agent must update the "Implementation History" and "Current Phase" sections in `project_specs.md`.

---

# 9. Push to GitHub & Release Process

## Basic Rules
- Ensure no hardcoded personal IP addresses, domain names, or API keys are leaked in the commit history.
- Use `git diff --staged` or review changed files before committing.

## How to Release a New Version

1. **Commit all code changes** to the local `main` branch normally.
2. **Update the version number** in `project_specs.md` (Version Numbering Convention line at the bottom).
3. **Tag the release** — the tag name determines the version number:
   ```
   git tag v1.0.XX
   ```
4. **Push both branch and tag** (push them separately — PowerShell doesn't support `&&`):
   ```
   git push --force-with-lease origin main
   git push origin v1.0.XX
   ```

When the tag is pushed, GitHub Actions (`.github/workflows/build-release.yml`) automatically:
- Builds the `.dll` in Release mode
- Packages it as `jellyfin-plugin-upcoming-movies_v1.0.XX.zip`
- Computes the MD5 checksum
- **Prepends a new entry to `manifest.json`** on the `main` branch using `git push --force-with-lease`
- Creates a GitHub Release with the ZIP as a downloadable asset

Jellyfin reads **`manifest.json` on the `main` branch** to show available versions in the plugin catalog. If `manifest.json` is not updated, the version **will not appear in Jellyfin** even if the GitHub Release ZIP exists.

## Version Numbering

Always increment the **third part** by 1: `1.0.52` → `1.0.53` → `1.0.54`.  
The current version is always at the bottom of `project_specs.md` under "Version Numbering Convention".

## When a Version Doesn't Appear in Jellyfin (Recovery)

If Jellyfin doesn't show a new version after a release:

1. **Check if CI succeeded** — look at the Actions tab on GitHub
2. **Check if manifest.json was updated** — visit:
   `https://raw.githubusercontent.com/Hu1k1e/Discover---Jellyfin/main/manifest.json`
3. **Check if the GitHub Release has the ZIP asset**

**If manifest.json is missing the version (manual fix):**
- Get the MD5 checksum from the GitHub release page body text (`**ZIP MD5:** \`...\``)
  — OR call `https://api.github.com/repos/Hu1k1e/Discover---Jellyfin/releases` via the read_url_content tool
- Manually prepend the missing version(s) to `manifest.json` (newest first), commit, and push `main`

## What NOT to Do
- ❌ Do NOT retag and re-push without fixing the manifest — CI will create a duplicate release
- ❌ Do NOT push a tag without committing code first — CI builds from the tagged HEAD
- ❌ Do NOT edit `manifest.json` with a made-up or guessed MD5 — use the real checksum from the built ZIP


> [!IMPORTANT]
> **Read the Deployment Process section below before pushing any release.**

---

# DEPLOYMENT PROCESS — How to Release a New Version

This is the exact procedure to push a change and have it appear in Jellyfin's plugin catalog. **Follow every step.** Past failures came from skipping steps or doing them out of order.

## Step 1 — Make + Commit Your Changes

```powershell
git add <changed files>
git commit -m "fix: description of what changed"
```

## Step 2 — Push the Main Branch

```powershell
git push origin main
```

> If the push is rejected ("non-fast-forward"), pull and rebase first:
> ```powershell
> git pull --rebase origin main
> git push origin main
> ```
> PowerShell may show exit code 1 even on success — check the output for `main -> main` to confirm.

## Step 3 — Create + Push a Version Tag

The GitHub Actions workflow **only triggers on `v*` tags**, NOT on regular commits.

```powershell
git tag v1.0.XX    # replace XX with next version number (e.g. v1.0.48)
git push origin v1.0.XX
```

Confirm success: output should include `* [new tag] v1.0.XX -> v1.0.XX`

## Step 4 — Wait for GitHub Actions (~2 minutes)

The workflow (`.github/workflows/build-release.yml`) will automatically:
1. Build the plugin `.dll` 
2. Create a ZIP: `jellyfin-plugin-upcoming-movies_v1.0.XX.zip`
3. Compute the MD5 checksum
4. **Prepend a new version entry to `manifest.json`** and commit it to `main`
5. Create a GitHub Release with the ZIP as an asset

## Step 5 — Pull the Actions Bot Commit

After the workflow completes, the Actions bot commits an updated `manifest.json` to `main`. Pull it so local is in sync:

```powershell
git pull origin main
```

Verify `manifest.json` starts with `"version": "1.0.XX.0"` — that's the confirmation it worked.

## Step 6 — Verify the Release

Check: `https://api.github.com/repos/Hu1k1e/Discover---Jellyfin/releases?per_page=1`  
Should show the new release with `"tag_name": "v1.0.XX"` and a non-empty `assets` array.

---

## Troubleshooting: Manifest Not Updated (v not appearing in Jellyfin)

**Symptom:** GitHub release exists (ZIP uploaded) but Jellyfin catalog still shows old version.  
**Cause:** The Actions bot tried to push the manifest update but failed (usually due to a rebase conflict on `main`).  
**Fix:** Manually add the entry to `manifest.json`:

```json
{
  "version": "1.0.XX.0",
  "changelog": "Release 1.0.XX.0. See GitHub for details.",
  "targetAbi": "10.11.0.0",
  "sourceUrl": "https://github.com/Hu1k1e/Discover---Jellyfin/releases/download/v1.0.XX/jellyfin-plugin-upcoming-movies_v1.0.XX.zip",
  "checksum": "<MD5 from the GitHub release body>",
  "timestamp": "<timestamp from the GitHub release>"
}
```

Get the ZIP MD5 from the GitHub release description (it's printed as `**ZIP MD5:** `...``).  
Prepend this object at position `[0].versions[0]` (the very first entry in the array), then:

```powershell
git add manifest.json
git commit -m "chore: manually update manifest for v1.0.XX"
git push origin main
```

---