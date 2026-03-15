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
- **Never use `git stash` during a release** — it causes merge conflicts with `manifest.json` that silently revert code fixes.

## How to Release a New Version

1. **Commit all code changes** to the local `main` branch normally.
2. **Update the version number** in `project_specs.md` (the "Version Numbering Convention" line at the bottom).
3. **Stage and commit:**
   ```
   git add <changed files>
   git commit -m "feat/fix: description v1.0.XX"
   ```
4. **Sync with remote first** (avoids force-push / detached HEAD issues):
   ```
   git pull --rebase origin main
   git push origin main
   ```
5. **Tag and push tag:**
   ```
   git tag v1.0.XX
   git push origin v1.0.XX
   ```
6. **⚠️ VERIFY manifest.json was updated** — wait ~3 minutes for CI to finish, then open:
   ```
   https://raw.githubusercontent.com/Hu1k1e/Discover---Jellyfin/main/manifest.json
   ```
   Confirm the new version is the **first entry** in `versions[]`.  
   **If it is missing, run the Recovery procedure below before telling the user the release is done.**

When the tag is pushed, GitHub Actions (`.github/workflows/build-release.yml`) automatically:
- Builds the `.dll` in Release mode and packages it as a ZIP
- Computes the MD5 checksum
- **Prepends a new entry to `manifest.json`** on `main` (3-attempt retry loop handles concurrent builds)
- Creates a GitHub Release with the ZIP as a download

Jellyfin reads `manifest.json` on the **`main` branch** for its plugin catalog. Version will not appear in Jellyfin if `manifest.json` is not updated.

## Version Numbering

Always increment the **third part** by 1: `1.0.55` → `1.0.56` → `1.0.57`.  
Current version is always at the bottom of `project_specs.md`.

## When a Version Doesn't Appear in Jellyfin (Recovery)

1. Check if CI succeeded — look at the Actions tab on GitHub
2. Check `https://raw.githubusercontent.com/Hu1k1e/Discover---Jellyfin/main/manifest.json`
3. Get the real MD5 from the release body: `**ZIP MD5:** \`...\`` — OR call `https://api.github.com/repos/Hu1k1e/Discover---Jellyfin/releases` via the `read_url_content` tool
4. Manually prepend the missing version entry to `manifest.json` (newest first), commit and push

## What NOT to Do
- ❌ Do NOT use `git stash` — causes merge conflicts that silently revert code fixes
- ❌ Do NOT force-push without pulling first — creates detached HEAD state
- ❌ Do NOT push multiple tags without verifying manifest after each one
- ❌ Do NOT retag and re-push without fixing the manifest — CI creates a duplicate release
- ❌ Do NOT edit `manifest.json` with a guessed MD5 — always use the real checksum

## ⚠️ Fragile Code Warning — discoverPage.js

Around **line 1354** of `Web/discoverPage.js` there is a critical brace structure:
```js
                }   // end if (btnMore)
            }   // end else if (rec) — CRITICAL: closes success branch before the else
            else { rowRec.innerHTML = '<div class="discover-error">...</div>'; }
```
This has been lost **twice** due to git merges overwriting it. The `}` on line 2 above closes the entire recommendations success path (`else if (rec)`). If it is missing, the `else { "Failed to load" }` incorrectly pairs with `if (btnMore)` and wipes the loaded content. **Do NOT let git merges silently overwrite this file.**