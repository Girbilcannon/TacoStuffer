using TacoStuffer.Models;

internal static class Final107Checks
{
    public static void Run()
    {
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        var defaults = new ProjectAssetPaths();
        var ordinary = AssetFolders.BrowseRoots(defaults, []);
        Check(!ordinary.Any(p => AssetFolders.Contains(p, "Other/Nested")), "Default Core XML root must not expose all folders");
        var added = AssetFolders.BrowseRoots(defaults, ["Other/Nested"]);
        Check(added.Any(p => AssetFolders.Contains("Other", p)), "Registered nested folders expose their ancestor navigation");
        Check(added.Any(p => AssetFolders.Contains(p, "Other/Nested/Child")), "Registered folders expose their physical descendants");
        Check(!added.Any(p => AssetFolders.Contains(p, "Unrelated")), "Adding a folder must not register unrelated siblings");
        var root = AssetFolders.BrowseRoots(defaults, ["."]);
        Check(root.Any(p => AssetFolders.Contains(p, "Other/Nested")), "Explicit Add Existing project root must expose its folders");
        Check(AssetFolders.BrowseRoots(defaults, ["TEXTURES", "textures"]).Count(p => p.Equals("TEXTURES", StringComparison.OrdinalIgnoreCase)) == 1, "Registration is case-insensitively distinct");

        var focus = new OverlayForegroundState();
        Check(focus.Observe(10, 1000) == 10, "Observe game/editor foreground");
        Check(focus.Observe(0, 1050) == 10, "A short activation gap must not hide the HUD");
        Check(focus.Observe(0, 1101) == 0, "Zero observations cannot indefinitely extend the grace period");
        Check(focus.Observe(20, 1110) == 20, "Unrelated applications hide the overlay immediately through normal foreground gating");
        Check(focus.Observe(0, 1111) == 20, "A gap must not resurrect the previous application");
        Check(new OverlayForegroundState().Observe(0, 1) == 0, "Startup cannot invent a foreground window");
        Console.WriteLine("1.0.7 final folder registration and foreground transition checks passed.");
    }
}
