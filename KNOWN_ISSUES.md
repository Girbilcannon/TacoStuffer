# Known issues and beta limitations

Applies to the 0.1.3b beta documentation baseline. Reports may reveal additional
issues; a source change does not establish that a published EXE was tested.

| Area | Limitation / guidance |
| --- | --- |
| Play in App | Experimental. Client initialization, DPI/display layout, graphics drivers, and game-window lifecycle need broader testing. Prefer the standalone overlay when encountering hosting problems. |
| Compiled built-in attributes | Final single-file release must pass the catalog check: open an existing project and confirm Attributes/Add Attributes works without external definition files. Empty controls should be reported with the exact build/download filename. |
| Reader compatibility | Previewed XML attributes and menus can differ from TacO, Blish HUD, Taimi HUD, and other parsers. Test each intended target. |
| Fade | TacO visibility may require Fade on applicable elements/categories. It is user-controlled; the editor does not insert it automatically. |
| Sticky | The bundled Sticky group targets Taquito. It is not a universal reader feature. |
| Lua | Imported Lua is editable text. It is not executed or visually simulated. |
| Missing assets | Export can substitute reported placeholders. Empty trail headers do not reconstruct missing routes. Inspect the build report. |
| Large packs | Import, trail conversion/resampling, export, and extensive histories may take time or memory. Report measured delays and pack/point counts. |
| History | Undo/redo is session-local and covers supported project edits. Saved backups remain important. |
| Imported binary trails | Keep original files until conversion is needed. Unsupported/corrupt sources cannot be made editable. |

Built-in attribute definitions belong inside the application. Adding an external
Attributes folder is not the intended beta deployment or a permanent workaround.
Project CUSTOM_ATTRIBUTES is intentionally external and editable.

Please report reproducible problems through Issues. For data-loss reports, keep
an untouched copy of the affected project and redact any private material before
sharing. Use [SECURITY.md](SECURITY.md) for vulnerability reports.
