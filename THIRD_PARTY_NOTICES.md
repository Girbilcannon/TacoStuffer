# TACO STUFFER — THIRD-PARTY COMPONENTS AND DISTRIBUTION NOTICES
The original Taco Stuffer application uses a custom personal-use license; see
LICENSE.txt and LICENSE-INFO.md. That license does not replace the terms of
independently licensed components.

### SOURCE-REPOSITORY INVENTORY
The reviewed TacoStuffer.csproj targets net10.0-windows and uses WPF. It has no
explicit third-party PackageReference entries. The native graphics/window calls
use Windows APIs. This is a source-level inventory, not a complete inventory of
a published self-contained executable or of every asset a user might import.

### MICROSOFT .NET / WINDOWS DESKTOP RUNTIME
Self-contained builds embed .NET and Windows Desktop runtime components. Their
applicable distributed-binary terms and third-party notices remain in effect.
The Taco Stuffer license does not restrict rights under those runtime licenses.

Before distributing a self-contained binary, collect the full applicable LICENSE
and THIRD-PARTY-NOTICES files from the exact runtime packages used for that build.
Record the versions and include those texts in the release, for example under
THIRD_PARTY/. The exact runtime versions and their notice texts cannot be derived
from the target framework alone and have not been supplied with this source ZIP.
This file is therefore not a complete notice bundle for the published executable.

Use the release build's obj/project.assets.json, restored runtime packages and
build/publish output to identify those versions. See docs/RELEASING.md.
.NET source licensing and Microsoft's distributed Windows-binary terms are not
interchangeable; do not substitute a generic source MIT text for the package terms.

**Reference information:**
https://github.com/dotnet/core/blob/main/license-information.md
https://github.com/dotnet/core/blob/main/license-information-windows.md
https://dotnet.microsoft.com/en-us/dotnet_library_license.htm

### WINDOWS SYSTEM COMPONENTS
Windows supplies the WPF/Direct3D/composition/window-management system interfaces.
Calling these APIs does not grant permission to redistribute arbitrary Windows
system DLLs. Do not copy system DLLs into release packages to satisfy dependencies.

### GUILD WARS 2 AND USER MATERIALS
Guild Wars 2 and related marks and assets belong to their respective owners.
Taco Stuffer is independent and does not claim official endorsement. Imported
packs, textures and scripts retain their own terms; users are responsible for
permissions to use and distribute them. Record additional bundled third-party
libraries, fonts or images and include their required notices if added later.
