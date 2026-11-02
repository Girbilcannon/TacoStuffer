TACO STUFFER — APP ICON ASSETS

TacoStuffer.ico : multi-size Windows icon (16, 24, 32, 48, 64, 128, 256 px).
TacoStuffer_Master_Transparent_1024.png : transparent master raster image.
PNG/ : individual PNG icons for Windows UI and packaging.

WINFORMS / C#:
1. Add TacoStuffer.ico to your project.
2. In Visual Studio, Project > Properties > Application > Icon and manifest, choose TacoStuffer.ico.
   (For SDK-style projects, add <ApplicationIcon>TacoStuffer.ico</ApplicationIcon> to the .csproj.)
3. To set a form icon explicitly: this.Icon = new System.Drawing.Icon("TacoStuffer.ico");
   Ideally use a project resource or copied asset path rather than relying on the working directory.
4. Rebuild and restart Windows Explorer or refresh pinned shortcuts if Windows caches the old icon.

The source was AI-generated with a black background. This pack uses a background-removal mask,
so please visually inspect edges at desired UI sizes.
