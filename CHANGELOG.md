# Taco Stuffer — Complete Build Changelog


## 1.0.7
- Added extended Attributes to choose from (separating groups)
- Updated full Documentation

## 1.0.6
- Optimized realtime overlay items

## 1.0.5
- Added Reverse Trail button to toolbar
- Fixed flow direction of trail segment during split and merge operations
- Transition between toolbar and game is more direct and less "clunky"
- Fixed window layering order for toolbar and popups in testing mode

## 1.0.4
- Added Toolbar Overlay with extended controls
- Improved in-game marker and trail point handling of multiple points

## 1.0.3
- Import/Export consistency check and repairs for Lua round-tripping

## 1.0.2
- Fixed auto-deselect of trail segment points after making a move edit

## 1.0.1
- Corrected marker effective range ring for Fade attributes
- Added range ring to maps

## 1.0.0
- Minor UI instruction update for Outliner
- (Settings Option) Enable range indicator rings for selected markers
- Insert Trail Points tool (yellow text status when active)

## 0.1.4b
- Synced asset manager to file system at project level.
- Asset manager default folder buttons removed to clear clutter (now right click only)
- Outliner now has middle-click drag to parent and sibling option
- Project Settings allows 2 new alternate Pathing tool pack destinations
- Main window maximize now adjusts for task bar

## 0.1.13b
- Replaced the checkpoint Add option dropdown with a Reset checkbox, exposing the first-checkpoint GUID picker and optional reset message. cpReset remains a GUID string, not a Boolean.
- Unchecking Is Checkpoint removes local cpColor, cpPreColor, nextCheckCount, nextCheck and cpRadius XML values. Rechecking restores their authoring draft during the current project session. Disabled drafts are not retained after reopening.
- Preserved reset-only markers, ordinary trigger settings, shared reset messages and the independent Finish Line role. Checkpoint and finish checkbox edits write true/false.
- Added a left-edge resize grip and Float/Dock controls to the Play-in-App code window. Floating editors can move to another monitor without snapping back; the same document/editor remains synchronized.

## 0.1.12b
- Extended Project Assets to import multiple TRL files into the default trail folder, initially DATA. Added Default Trails buttons, context-menu actions and default-folder indicators.
- Added an indented category DisplayName picker, Make editable checkbox and Insert action. Insertion creates the Trail XML with its category type, source MapID, unique GUID/name and project-relative trailData path.
- Make editable converts original segments and points without resampling. Reference-only insertion retains the original binary; repeated insertion avoids shared output-path conflicts.
- Added asset-type filters and file-type icons for trails/documents. Individual TRL moves retarget XML and history references. Added fallback trail textures for reference-only exports and a trailData output-filename tooltip.

## 0.1.11b
- Right-click Rename and F2 now use the same batch naming rules as the attributes pane: top-to-bottom Outliner order, trailing-number starts, zero padding and project-wide internal-name collision avoidance.
- The batch dialog applies only edited name/displayName fields. Both fields validate before any rename is committed; unchanged dialogs do nothing.

## 0.1.10b
- Renamed Elements to Outliner. Normal expand reveals one level; Shift-click expands or collapses every descendant in a branch, including trail segments/points.
- Added indexed multi-asset name/displayName editing in hierarchy order. Internal names reject whitespace and skip occupied names; display names allow spaces. Existing numeric suffixes and padding are retained.
- Moved trailData below displayName and above MapID.
- Added Delete and Split Segment alongside ordinary Delete and Cancel. Selected point runs can leave real gaps and separate surviving segments; saved/compiled TRLs retain those boundaries.

## 0.1.9b
- Added Test checkpoints / Cancel checkpoint test, with a persistent on-screen test indicator and temporary progression state.
- Implemented preview progression, fork choices, lookahead, root resets and finish contact. Cancellation restores the editing display without persisting test progress.
- Selected checkpoints preview cpColor; unselected checkpoints preview cpPreColor. Added color pickers with RGB/alpha controls, swatches and HEX editing.
- Added independent cpFinish authoring for markers and trails, including swept contact with two-point finish trails.
- Added optional resetMessage controls for cpReset and standard resetguid. Missing/empty messages produce no popup. Supplied a new Taquito runtime handoff; the Taco Stuffer patch itself did not modify Taquito.

## 0.1.8b
- Added standard resetguid authoring, multiple GUID targets, project-marker selection and preview behavior-state resets. Preserved unrelated GUIDs and other-character daily history.
- Added the Taquito-only Checkpoint group: isCheckpoint, cpColor, cpPreColor, nextCheckCount, nextCheck, cpRadius and optional cpReset.
- Added successor/root GUID pickers and validation. cpReset is independent of isCheckpoint and uses ordinary trigger/manual or automatic activation.
- Preserved custom fields on export and supplied the Taquito implementation plan. At this build, custom checkpoint progression and RGBA checkpoint rendering were authoring-only; testing arrived in 0.1.9b.

## 0.1.7b
- Added small yellow requirement alerts to attribute headings, with tooltip explanations that update as dependencies and preview settings change.
- Added checks for missing triggers, unused trigger settings, empty action targets/text, invalid behavior/range/GUID settings, unsupported modes and applicable preview permissions.
- Removed automatic Trigger insertion when adding action groups; users add Trigger explicitly when required. Alerts are advisory and do not block compilation or rewrite authored XML.

## 0.1.6b
- Moved inherited values into a collapsed-by-default section showing their source categories. Locally overridden inherited names are struck through.
- Added right-click Override / Remove override and retained per-child inherited values during multi-selection.
- Kept locally authored groups in the main pane and replaced repetitive enable/override rows with Add option controls. Checkpoint controls later received their own treatment in 0.1.13b.
- Separated triggerRange and autoTrigger into a Trigger group, fixing unrelated Missing required: copy alerts. This build initially supplied missing trigger defaults for action groups; 0.1.7b replaced that with explicit requirement alerts.

## 0.1.5b
- Fixed stretched GPU trails by correcting the draw-call vertex stride from 36 to 48 bytes, derived from the actual vertex structure. Recorded/exported geometry was not changed.
- Made Add Attributes resizable with a bounded starting height, scrolling contents and always-visible bottom buttons.
- Fixed Behavior dropdown labels and replaced difficult category paths with indented DisplayNames in Show/Hide, Toggle and other relevant choices.
- Added readable Lua file/folder choices and a simulated native draw-binding regression check.

## 0.1.4b
- Changed overlay category trees/recording dropdowns to DisplayNames with hierarchy indentation. Fixed asset-browser default stars shifting sibling folder labels.
- Added grouped authoring for Show/Hide, Toggle/Default Toggle, Tip, Mount, Wall Trails, Hidden Category, Bounce, Behavior/Invert Behavior/Reset Length, Can Fade and Script hooks.
- Added mount filtering, bounce/rise, wall-trail preview, hover tips, category defaults/menu hiding and optional soft fades. Added opt-in behavior/category interaction preview with project-scoped state.
- Preserved independent editor identities for shared-GUID marker pairs. Added Lua file/function selection, five script hooks and pack.lua loader assistance, without Lua execution.
- Updated source XML and compiled definitions together and supplied detailed Taquito attribute documentation.

## 0.1.3b
- Added XML, Lua, Markdown and plain-text document creation, organized into Scripts/Docs sections.
- Added Lua syntax highlighting, Markdown formatting tools and dark preview, plus optional documentation inclusion when compiling.
- Added document rename/delete actions and XML Add Comment Before / After.
- Introduced Project Assets with thumbnails, search, import, folder management, multi-selection moves and default icon/texture folders.
- Improved asset-browser styling, filesystem synchronization and reference updates after folder moves.
- Added a dim working-line highlight for XML navigation.
- Improved dialog visibility, overlay resizing, input focus and popup layering.
- Reduced overlay spacing, clarified recording-target selection and added optional minimized-overlay glow.

## 0.1.2b
- Fixed game-telemetry detection without requiring another addon and corrected overlay availability.
- Improved overlay layering, free movement and minimize/restore behavior in standalone and Play-in-App modes.
- Standardized marker/trail preview scaling across world, map and minimap views.
- Corrected the default marker-height preview to match TacO without changing saved coordinates.
- Added grouped Info and Copy attributes, proximity/automatic triggers, and HUD/clipboard interaction testing.

## 0.1.1b
- Released the initial open beta with project editing, marker/trail authoring, .taco import/export, previews and experimental Play in App.
- Added marker-name visibility settings, a live distance threshold and trail-selection preferences.
- Added output-folder changes in Project Settings and overlay marker/trail renaming.
- Preserved expanded trail branches while moving points.
- Simplified Add Attributes, limited optional Position to categories and updated Fade/stickyDistance defaults.
- Compiled built-in attribute definitions into the application and corrected publishing configuration.
- Improved import/conversion and shutdown reliability.
- Added the beta QuickStart guide, source cleanup, repository configuration and personal-use licensing.

## 0.0.52
- Widened the default Explorer and Attributes panes; Reset Layout uses the same sizes.
- Added explicit Standard Layout controls when no live/pending hosted game session remains; restored normal document tabs, comparisons and active editor without terminating a live game.

## 0.0.51
- Added presentation repair for native overlay visibility/stacking, startup settling and game-exit handling.
- Added exact-session Force Close Game and improved hosted-session shutdown handling.

## 0.0.50
- Used the center pane for the hosted game while hiding ordinary tab/comparison chrome.
- Added a translucent live code window over the right third of the viewport, with background-opacity control and shared XML/Lua editing state. Recording controls could move freely.

## 0.0.49
- Validated saved native parent/owner handles, provided desktop fallbacks and suspended host callbacks during release to avoid accidental reattachment.

## 0.0.48
- Rolled back the owned top-level docking approach introduced in 0.0.47.
- Returned to child hosting with actual-parent coordinate mapping and parent clipping, avoiding direct modification of the game window region.

## 0.0.47
- Introduced a shared editor-owned top-level attachment path for launched and manually attached game windows, with consistent screen coordinates.
- This experimental approach was rolled back in 0.0.48 after a game-freeze report.

## 0.0.46
- Simplified the Game toolbar and renamed Recording Controls to Game Overlay.
- Adjusted non-client border handling, viewport overlap and position-only updates.

## 0.0.45
- Removed the initial 1×1 game resize, required valid viewport sizes and debounced later buffer resizes.
- Adjusted client/frame fitting and retained release-time restoration data.

## 0.0.44
- Created the native viewport lazily with an explicit dark background, avoiding a blank native surface over startup UI.
- Detected the actual GW2 render window and improved attachment diagnostics and assumptions.

## 0.0.43
- Added experimental native GW2 hosting, Launch Windowed, Attach Running Game and Return to Desktop controls.
- Added a saved executable-path preference. This was a hosting experiment, not a claim of finished game compatibility.

## 0.0.42
- Made Quick Menu checks update cached preview visibility without reopening documents or rebuilding attribute controls.
- Retained GPU meshes/textures, created Quick Menu branches lazily and targeted segment-density refreshes.

## 0.0.41
- Cached hierarchy/search data, moved search matching to a cancellable worker, reduced selection/history overhead and added lazy overlay branches and virtualized scrolling.
- Follow-up build fix corrected colliding local variable names in MainWindow.Game.cs.

## 0.0.40
- Bound open XML/Lua views to five, retained closed source buffers and formatted document views on demand.
- Added closed-document organization and on-demand trail editing to reduce eager work with large imported packs.

## 0.0.39
- Added per-project icon, texture and TRL folder paths, defaulting to TEXTURES, TEXTURES and DATA.
- Added recursive image pickers while preserving safe existing references outside their picker roots.

## 0.0.38
- Converted supported trails individually instead of aborting the entire import on a bad trail.
- Retained unconverted binaries and XML paths, and reported missing/invalid/unsupported conversions.

## 0.0.37
- Added .taco-to-project import with original asset layout and optional editable TRL conversion.
- Preserved original source archives, retained GUID/path relationships and added Lua code tabs for authoring/packaging.

## 0.0.36
- Tripled the then-current internal map/minimap trail base width while keeping independent preference multipliers. World widths and exported XML were unchanged.

## 0.0.35
- Added six saved visual multipliers for world/map/minimap marker sizes and trail widths, plus Defaults.
- Doubled the then-current map/minimap trail base width; later normalization changed the preview formulas again.

## 0.0.34
- Rendered map/minimap ribbons with their trail textures, tint, alpha and animation in GPU and fallback paths.
- Removed the redundant Visibility entry from Add Attributes while retaining existing visibility controls.

## 0.0.33
- Expanded live visual previews for animation, tint/alpha, rotation, Sticky behavior and map trails.
- Displayed the application build number from assembly metadata.

## 0.0.32
- Removed automatic Fade insertion from new trails and exports; explicit and inherited values remained as authored.
- Added the Fade compatibility note link. Manual defaults remained 5000/5200 at this point.

## 0.0.31
- Changed the temporary new/exported trail fade defaults to 5000/5200, preserving explicit/inherited values.
- Automatic insertion was removed in 0.0.32.

## 0.0.30
- Added 3500/3700 defaults for new trails and export fallbacks when effective Fade settings were missing.
- Superseded by 0.0.31 and then removed in 0.0.32.

## 0.0.29
- Implemented shared project Undo/Redo, history browsing, coalesced XML edits and recording/drag action boundaries.
- Added persistent custom keybinds and live hierarchy search.

## 0.0.28
- Audited editable-trail GUID/XML matching, output paths, source-map consistency and standard TRL serialization.
- Added export safeguards while retaining editable JSON outside compiled packs.

## 0.0.27
- Corrected native GPU surface styles so the preview would not obstruct mouse input to the separate game process.
- Follow-up patch corrected Y/Z rotation drag direction and added manipulator hover/drag highlighting.

## 0.0.26
- Introduced Direct3D 11 / DirectComposition trail rendering, cached meshes/textures and incremental recording uploads, with a fallback rendering path.
- Follow-up build fix removed an invalid ExactSpelling argument from StructLayout.

## 0.0.25
- Added the missing Services namespace import and corrected unsigned native window-style flag handling.
- Documented the planned GPU rendering direction; the renderer itself arrived in 0.0.26.

## 0.0.24
- Added Delete handling and confirmation ownership for overlay editing.
- Clipped trail ribbon triangles against camera/viewport planes and refined texture projection across long edges.

## 0.0.23
- Set new recording density to level 8 and retained density per segment across resume/save/reload.
- Restricted stopped-trail density edits to explicitly selected segments, assigned new trails directly to their chosen category and reduced rendering/hierarchy refresh work.

## 0.0.22
- Separated ribbon visibility from selected point handles. Selecting a trail/segment no longer implicitly selected all its points.
- Added post-recording segment-density adjustments and green asset labels.

## 0.0.21
- Showed world point handles only for selected points and collapsed segments by default.
- Added the 1–10 recording smoothness slider, initially defaulting to level 9; 0.0.23 changed the new-session default to 8.

## 0.0.20
- Added trail creation, recording, pause/continue/new-segment/stop and resumable segment workflows.
- Stored persistent editable JSON trails and compiled version-0 TRLs with true segment separators. Added segment/point selection and geometry manipulation, XML deletion and map/minimap marker preview.

## 0.0.19
- Reduced menu-bar and build-button padding to reclaim vertical editing space without changing behavior.

## 0.0.18
- Added click-off deselection while leaving empty game clicks available to GW2.
- Improved mouse capture release and updated drag previews without rebuilding/parsing the whole workspace continuously; committed XML on release.

## 0.0.17
- Filtered separator headings out of recording targets and fixed category double-click targeting.
- Added shared cached marker/default-trail preview artwork and clarified coordinate handling.

## 0.0.16
- Previewed effective inherited marker styling and added marker selection, XYZ translation handles, camera-plane movement and reposition-at-character.
- Added the selected-name preference, compact overlay layout and redraw optimizations.

## 0.0.15
- Added live POI recording into the selected XML/category with GUID, MapID, raw Mumble XYZ, name and displayName.
- Added continuous saved marker numbering and purple world previews using the inspected camera-projection approach.

## 0.0.14
- Added Markers/Trails tabs, category controls and the recorded-assets sidebar synchronized with the main editor.
- Connected MumbleLink readiness and game-aware visibility/lifecycle handling. Actual recording/3D preview followed in later builds.

## 0.0.13
- Added Ctrl+N/O/S, renamed Load to Open and introduced a Preferences/keybind placeholder. Undo/Redo shortcuts were reserved until history arrived in 0.0.29.
- A separate installer-planning note described future .cilantro association/icon work; it did not implement an installer or file association.

## 0.0.12
- Removed whitespace-only content when a category lost its last child, restoring self-closing tags in editor and exported XML while preserving comments/meaningful content.

## 0.0.11
- Added multi-target attribute editing and a collapsible Elements hierarchy.
- Changed category/asset resolution policy at this stage; 0.0.16 subsequently restored ancestor-category traversal for marker/trail styling.

## 0.0.10
- Reduced Quick Menu row spacing, styled separators as green headings without checkboxes and removed storage/asset-folder rows from Elements.

## 0.0.9
- Removed demonstration content from fresh startup and required an active project for authoring operations.
- Changed Quick Menu dragging to the middle button and removed redundant empty category copies used for cross-XML ordering.

## 0.0.8
- Persisted canonical category/XML order and added middle-drag reordering with drop guides.
- Normalized shared category ordering while retaining source attributes and comments.

## 0.0.7
- Implemented .cilantro manifests, blank starter projects, tracked XML files, asset folders and configurable .taco output.
- Added save/load, rename cleanup, unsaved-change prompts and save-before-build behavior.

## 0.0.6
- Added F2/context-menu XML renaming with extension handling, Windows filename validation and collision checks. Updated tabs, detached captions, move targets and exports.

## 0.0.5
- Made Stuff Taco Now build a .taco ZIP from in-memory XML and existing assets, with explicit placeholders/reports for missing assets.
- Added multi-selection moves between XML/category destinations and confirmed deletion. This preceded project persistence and real trail recording.

## 0.0.4
- Grouped iconFile/iconSize and texture/trailScale controls; labeled Sticky as Taquito-only.
- Improved XML indentation after category edits and added the + Category action.

## 0.0.3
- Added closable/reopenable XML views, comparison panes and detached editors sharing the same source buffer.
- Added XML-to-hierarchy selection, category reparenting/rename, image import and asset applicability checks.
- Follow-up build fix added the Documents namespace import and avoided capturing an out parameter in a lambda.

## 0.0.2
- Added explicit dark control/dialog templates, author credit, editable category names, multi-selection, grouped attributes and custom definition handling.
- Added delayed valid-XML synchronization and category/type-path rename propagation.
- Follow-up build fix explicitly included missing model sources and resupplied runtime definitions.

## 0.0.1
- Introduced the Windows/.NET 10 WPF three-pane layout, sample XML editors, Elements/Quick Menu tabs, attribute controls and overlay recording-state demonstration.
- Included an offline browser layout companion. Game integration, project persistence, recording and pack building were still placeholders.

---
