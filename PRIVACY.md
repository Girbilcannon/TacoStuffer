# Privacy and local data

Applies to **Taco Stuffer 1.0.7** · Updated November 2, 2026.

## Local editing and recording

The application code contains no application analytics, advertising, account sign-in, automatic crash uploads or online pack-download service. Project editing, recording and compiling use local files and Windows interfaces.

This does not mean every feature is offline: Help opens external websites, Markdown can reference remote images, and folders can be on network/cloud-synced storage. Windows, .NET, GW2, browsers and other software have separate behavior and policies.

## Data the application reads

- Selected projects, XML, editable trail data, TRLs, images, custom definitions, Lua, Markdown, plain text and imported `.taco` archives.
- GW2 MumbleLink telemetry, including character name, map/process identifiers, position, camera and compass/map data used for preview and connection checks. If necessary, Taco Stuffer creates the shared mapping with read/write access so GW2 can open its producer view; Taco Stuffer's telemetry view is read-only.
- Process/window information used to locate the game, manage the overlay and host the experimental Play in App view. Play in App can start GW2 with the configured launch options.
- Keyboard/mouse state and native mouse messages used for shortcuts, selection, camera-gesture detection and editing tools. A low-level mouse observer can receive desktop mouse events while active; feature consumers gate editing to the relevant game/editor context. The application does not keep a persistent keystroke or mouse-history log.

The inspected code uses telemetry and Windows APIs rather than game-process code injection or game-process memory patching. Lua is authored/packaged, not executed by Taco Stuffer.

## Data the application writes

The project folder contains your `.cilantro`, XML, editable trail data, images, documents and custom definitions. Folder registrations/defaults and project settings are saved with the project. Import, rename, move, delete and save commands can change physical project files. Supported asset deletions request the Windows Recycle Bin; keep independent backups.

**Stuff Taco Now!** saves/builds locally and writes the compiled pack to the main output plus configured alternate folders. Blank main output uses the project's `EXPORT` folder. Network or cloud-synced destinations are handled by your storage provider. Exported packs can contain character-placed coordinates, map IDs, GUIDs, labels, documents and scripts; inspect their contents before sharing.

Preferences and diagnostics are stored under:

```text
%LOCALAPPDATA%\TacoStuffer\
```

These include `preferences.json` and feature-dependent logs such as `performance.log`, `game-hosting.log` and `renderer.log`. Preferences can contain visual/interaction settings, shortcuts, the selected GW2 executable and remembered toolbar position. Logs can contain timestamps, timings, paths, process/window details and errors. Taco Stuffer does not upload these automatically.

A single-file .NET application may extract native runtime support files to temporary storage. That extraction is separate from project storage.

## Clipboard and interactions

Copy attributes and supported preview interactions can replace clipboard text. Automatic copy testing depends on the interaction test mode and clipboard permissions in Preferences. Modes include Always, Only when interacted with, and Never. The application reads clipboard text back to verify a requested copy. It does not maintain a clipboard-history database.

## Websites and Markdown preview

**Help → Documentation** opens `https://girbilcannon.github.io/TacoStuffer/` and **Help → Discord** opens `https://discord.gg/KU8jwACydZ` in your default browser. Opening these sends normal web requests to GitHub Pages or Discord under their policies; the app does not attach your project or local logs to those links.

Markdown preview disables raw Markdown HTML and filters links to supported schemes/project-local paths. HTTP(S) image references can still cause the embedded browser to request remote resources. Opening external links or using online resources exposes normal request information to those services. Previewing untrusted documents is not equivalent to a full content-security audit.

## Your controls

Close the application before deleting its preferences/log folder to reset settings or remove diagnostics. This does not delete projects stored elsewhere. Manage project backups and any network/cloud storage separately.

Bug-report attachments are voluntary. Review logs, screenshots and sample packs for personal paths, character names and private content before posting to GitHub or Discord. Use the private reporting route in [SECURITY.md](SECURITY.md) for vulnerabilities.

This document describes the inspected 1.0.7 code, not an independent audit or a promise about third-party applications. Update it when application data handling changes.
