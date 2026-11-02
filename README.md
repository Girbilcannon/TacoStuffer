# Taco Stuffer · 1.0.7

**Guild Wars 2 marker-pack authoring suite** · by: Girbilcannon.8259

Create markers, record and edit trails, organize categories, and compile `.taco` packs. Work through the visual controls, XML, or both. Taco Stuffer is a Windows desktop application built with C#/.NET 10 and WPF.

[User guide](https://girbilcannon.github.io/TacoStuffer/) · [Discord](https://discord.gg/KU8jwACydZ) · [Downloads](https://github.com/Girbilcannon/TacoStuffer/releases)

## Get started

1. Download the published Windows x64 application from Releases. Run `TacoStuffer.exe`.
2. Create a project and choose a project location. Keep the `.cilantro` project, XML, images, documents and editable trail data together.
3. Start GW2 and enter a playable map. With a project open, choose **Open Game Overlay** and select the recording category/XML.
4. Add a marker or create a trail and start recording. Edit its position, appearance and attributes.
5. Save, then click **Stuff Taco Now!** to build your `.taco`. Test the result in each intended pathing reader.

A published self-contained build is a single EXE; the project you author is a separate folder. Do not run the loose EXE from an ordinary build output without its dependencies. See the publish command below for a distributable single-file build.

## Main features

- **Outliner:** categories, markers, trails, segments, points, XML, Scripts and Docs. Multi-selection, indexed batch renaming, middle-drag reordering and Shift-expand that stops at segments.
- **Quick Menu:** preview your pack's category hierarchy, display names, separators and visibility toggles as users navigate them in a pathing tool.
- **Attributes:** visual editing, inherited-value inspection, overrides, color pickers and custom definitions. Select a parent bundle for its complete controls or individual children for independent XML fields.
- **Game overlay and toolbar:** record markers/trails; select and manipulate items; insert/remove points; split, merge and reverse segments; test supported interactions and checkpoints. Tooltips explain the toolbar icons.
- **Editable trails:** retain authoring points and segments in the project; compile to TRL files. Import external TRLs, choose a category and optionally make them editable.
- **Project Assets:** browse physical project folders, import files, preview images, move/delete supported files/folders and choose default folder roles through the context menu. Add Existing Folder registers folders already inside the project; copy external folders into the project before registering them.
- **Code and documents:** XML working-line indication and comment actions; Lua syntax coloring; Markdown editing/preview; plain-text documents. Optional Markdown/text inclusion when compiling.
- **Pack import/export:** import `.taco` packs and build to a main output plus up to two alternate locations for testing in different readers.
- **Play in App:** experimental hosted game layout with adjustable/detachable code views. The standalone overlay remains available for normal authoring.

Taco Stuffer **does not execute Lua**. Scripts can be authored and packaged for a compatible reader such as Blish HUD Pathing. Preview support varies by attribute and reader. Taquito-specific checkpoints/sticky features require a reader implementing those attributes; they are not universal TacO/Blish features. See the [guide](https://girbilcannon.github.io/TacoStuffer/) for units, inheritance, interactions and checkpoint setup.

## Project folders

Defaults include `TEXTURES`, `DATA`, `DOCS`, `scripts`, and root XML documents. Manage default roles in **Project → Assets → right-click a folder → Make Default**. Internal editable-trail/custom-definition folders have separate purposes. Project Settings controls compiled output destinations and documentation inclusion.

Keep your editable project even after compiling: a `.taco` is the reader-facing result, not a replacement for the full authoring project. Back up before importing or performing large edits.

## Build from source

Install the **.NET 10 SDK** on Windows, then run from the repository root:

```powershell
dotnet build .\TacoStuffer\TacoStuffer.csproj -c Release
dotnet run --project .\Checks\TacoStuffer.Checks.csproj -c Release
```

Publish the Windows x64 single-file application:

```powershell
dotnet publish .\TacoStuffer\TacoStuffer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=embedded
```

The published EXE is under `TacoStuffer/bin/Release/net10.0-windows/win-x64/publish/`. Single-file publishing may extract native runtime files to temporary storage when launched. Retain the applicable release licenses/notices; see [release guidance](docs/RELEASING.md).

`Checks` is development regression-check source, not an end-user application dependency. Commit its `.cs` and `.csproj` files; exclude generated `bin` and `obj` directories.

## Documentation and support

**Help → Documentation**, **Help → Discord**, and **Help → About** provide quick access from the application. The online guide's source and screenshots live under `DOCUMENTATION/`; its GitHub Pages workflow is `.github/workflows/documentation.yml`.

Report normal bugs through repository Issues or Discord with the version, steps and a small example. Include marker/trail/point counts for performance reports. Review samples and logs before posting. Report security-sensitive issues privately using [SECURITY.md](SECURITY.md).

## License and notices

Taco Stuffer uses a **custom personal-use, source-available license**. It is not an open-source license. Personal noncommercial use and distribution of your own packs are permitted under the existing terms; code reuse, application redistribution, modifications and commercial use require permission as specified in [LICENSE.txt](LICENSE.txt). [LICENSE.md](LICENSE.md) is a readable copy, and [LICENSE-INFO.md](LICENSE-INFO.md) summarizes the terms.

[Privacy](PRIVACY.md) · [Security](SECURITY.md) · [Third-party notices](THIRD_PARTY_NOTICES.md)

Independent project; not affiliated with or endorsed by ArenaNet. Guild Wars 2 and related marks belong to their respective owners.
