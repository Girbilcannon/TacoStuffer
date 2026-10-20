# Privacy and Local Data
**Documentation baseline: Taco Stuffer 0.1.3b, October 2026.**

### Local operation
The current application code has no application analytics, advertising,
automatic crash upload, account sign-in, or remote pack-download service.
Project editing and recording are local. GitHub handles information you submit
through its website under GitHub's own policies. Windows, the .NET runtime, GW2,
and other overlays can have separate network or diagnostic behavior.

### Information read
- Project .cilantro/XML files, editable trail JSON, binary .trl files, textures,
  custom attribute definitions, and .taco archives selected for import.
- GW2 MumbleLink shared memory, opened for read access. Its data includes character
  identity/name, map/process information, character position, camera orientation,
  field of view, and compass/map data used for preview and connection checks.
- Windows process/window state used to find the GW2 client and position overlays.
- Keyboard/mouse state used for configured shortcuts and manipulation while the
  relevant editor/game context is active. This is not a persistent keystroke log.

The current implementation uses MumbleLink and Windows APIs; it does not implement
game-process code injection or game-process memory patching.

### Information written
Project data is written to the project location you choose. Compiled .taco output
is written to the configured output folder or the project's EXPORT folder.
Recorded XML/trails contain map IDs, positions, identifiers, and user labels.

Application preferences and local diagnostics are stored under:

```text
%LOCALAPPDATA%\TacoStuffer\
```

This can contain preferences.json, performance.log (and a rotated previous log),
game-hosting.log, and renderer.log. Preferences include visual multipliers,
custom shortcuts, and the chosen GW2 executable path. Diagnostics can contain
timestamps, timings, process/window details, machine paths, and error messages.
They are not uploaded automatically by Taco Stuffer.

The single-file .NET bundle may extract native runtime files under the user's
temporary directory. This is runtime support data, not an uploaded project.

### Controls and reports
Close the application before deleting its preferences/log folder to reset local
settings or remove those diagnostics. Deleting it does not remove projects saved
elsewhere. Back up and manage projects through your chosen storage folders.

When reporting a bug, you decide whether to attach logs, screenshots, or sample
packs. Review them for personal paths, character names, private asset content,
and other sensitive information first. Public GitHub issues and attachments can
be visible to everyone; use private reporting for security-sensitive information.

Changes to application data collection should be reflected in this document and
release notes. This document describes the current code baseline, not an
independent security audit or a promise about third-party software.
