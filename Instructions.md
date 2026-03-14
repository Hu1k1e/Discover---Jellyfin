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

# 9. Push to GitHub — Rules

When pushing code:
- Ensure no hardcoded personal IP addresses, domain names, or API keys are leaked in the commit history.
- Use `git diff --staged` or review changed files before committing.