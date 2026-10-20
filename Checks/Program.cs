using System.Xml.Linq;
using TacoStuffer.Models;
// Standalone regression checks link the production model files directly.
// Run with: dotnet run --project Checks/TacoStuffer.Checks.csproj -c Release
// These checks do not exercise WPF, native window hosting or GPU presentation.
static void Check(bool condition, string reason)
{
    if (!condition) throw new Exception(reason);
}
static XDocument Doc(string inner, string items = "") => XDocument.Parse($"<OverlayData><MarkerCategory name=\"pack\" DisplayName=\"Pack\">{inner}</MarkerCategory><POIs>{items}</POIs></OverlayData>");
var source = Doc("<MarkerCategory name=\"a\"><MarkerCategory name=\"child\"/></MarkerCategory>", "<POI GUID=\"marker\" type=\"pack.a.child\"/>");
var target = Doc("<MarkerCategory name=\"b\" iconSize=\"2\"/>", "<Trail GUID=\"trail\" type=\"pack.a\"/>");
var moved = CategoryOperations.Move(new[] { source, target }, "pack.a", "pack.b", CategoryDropPosition.Child, out string path);
Check(path == "pack.b.a", "Reparent path");
Check(moved[0].Descendants("POI").Single().Attribute("type")!.Value == "pack.b.a.child", "Descendant marker type must follow move");
Check(moved[1].Descendants("Trail").Single().Attribute("type")!.Value == "pack.b.a", "Cross-document reference must follow move");
Check(moved[0].Descendants("MarkerCategory").Any(c => CategoryOperations.PathOf(c) == "pack.b.a.child"), "Parent chain and child must be retained in original XML");
Check(source.Descendants("POI").Single().Attribute("type")!.Value == "pack.a.child", "Original input must not mutate");
try
{
    CategoryOperations.Move(new[] { source }, "pack.a", "pack.a.child", CategoryDropPosition.Child, out _);
    throw new Exception("Cycle accepted");
}
catch (InvalidOperationException)
{
}
var conflict = Doc("<MarkerCategory name=\"a\"/><MarkerCategory name=\"b\"><MarkerCategory name=\"a\"/></MarkerCategory>");
try
{
    CategoryOperations.Move(new[] { conflict }, "pack.a", "pack.b", CategoryDropPosition.Child, out _);
    throw new Exception("Collision accepted");
}
catch (InvalidOperationException)
{
}
var ordered = Doc("<MarkerCategory name=\"a\"/><MarkerCategory name=\"b\"/>");
var reorder = CategoryOperations.Move(new[] { ordered }, "pack.b", "pack.a", CategoryDropPosition.Before, out _);
Check((string?)reorder[0].Root!.Element("MarkerCategory")!.Elements("MarkerCategory").First().Attribute("name") == "b", "Sibling order must be XML order");
string xml = """
<OverlayData>
  <MarkerCategory name="pack">
    <MarkerCategory name="child" DisplayName="A > B" />
  </MarkerCategory>
  <POIs>
    <POI GUID="test"
         xpos="1" ypos="2" zpos="3" />
  </POIs>
</OverlayData>
""";
var poi = XmlSourceIndex.FindElement(xml, xml.IndexOf("xpos", StringComparison.Ordinal));
Check(poi?.Name == "POI" && (string?)poi.Attribute("GUID") == "test", "Multi-line attributes must resolve to their element");
var category = XmlSourceIndex.FindElement(xml, xml.IndexOf("    <MarkerCategory", StringComparison.Ordinal));
Check((string?)category?.Attribute("name") == "child", "Indented whole-line selection must resolve to nested category");
var end = XmlSourceIndex.FindElement(xml, xml.IndexOf("</MarkerCategory>", StringComparison.Ordinal));
Check((string?)end?.Attribute("name") == "pack", "Closing tag must resolve to category");

static PackSelection Pick(IReadOnlyList<XDocument> docs, int document, Func<XElement, bool> predicate) => new(document, docs[document].Descendants().ToList().FindIndex(e => predicate(e)));
var moveDocs = new[] { Doc("<MarkerCategory name=\"a\"><MarkerCategory name=\"child\"/></MarkerCategory><MarkerCategory name=\"b\"/>", "<POI GUID=\"one\" type=\"pack.a.child\"/><Trail GUID=\"two\" type=\"pack.b\"/>"), Doc("<MarkerCategory name=\"a\"/>") };
var branch = Pick(moveDocs, 0, e => (string?)e.Attribute("name") == "a");
var owned = Pick(moveDocs, 0, e => (string?)e.Attribute("GUID") == "one");
var loose = Pick(moveDocs, 0, e => (string?)e.Attribute("GUID") == "two");
var transferred = PackEditing.Move(moveDocs, new[] { branch, owned, loose }, "xml", "other.xml", 1);
Check(!transferred[0].Descendants("MarkerCategory").Any(c => CategoryOperations.PathOf(c) == "pack.a"), "XML move removes selected source branch");
Check(transferred[0].Descendants("POI").Count() == 0 && transferred[0].Descendants("Trail").Count() == 0, "Assigned and selected elements leave source XML");
Check(transferred[1].Descendants("POI").Count() == 1 && transferred[1].Descendants("Trail").Count() == 1, "Overlapping selection moves each item once");
Check(transferred[1].Descendants("MarkerCategory").Count(c => CategoryOperations.PathOf(c) == "pack.a") == 1, "Compatible destination category is merged");
Check(moveDocs[0].Descendants("POI").Count() == 1, "Move inputs remain untouched");
var retagged = PackEditing.Move(moveDocs, new[] { branch, owned, loose }, "category", "pack.b");
Check((string?)retagged[0].Descendants("POI").Single().Attribute("type") == "pack.b.a.child", "Selected owned marker follows selected category without being reassigned twice");
Check((string?)retagged[0].Descendants("Trail").Single().Attribute("type") == "pack.b", "Unrelated selected trail uses destination category");
var deleted = PackEditing.Delete(moveDocs, new[] { branch, owned });
Check(!deleted.SelectMany(d => d.Descendants("MarkerCategory")).Any(c => CategoryOperations.PathOf(c).StartsWith("pack.a", StringComparison.Ordinal)), "Deletion removes shared branch declarations");
Check(deleted[0].Descendants("POI").Count() == 0 && deleted[0].Descendants("Trail").Count() == 1, "Deletion removes branch items and retains unrelated items");
var duplicate = new[] { moveDocs[0], Doc("<MarkerCategory name=\"a\"/>", "<POI GUID=\"one\" type=\"pack.a\"/>") };
try
{
    PackEditing.Move(duplicate, new[] { branch }, "xml", "other.xml", 1);
    throw new Exception("Duplicate GUID accepted");
}
catch (InvalidOperationException)
{
}
Check(duplicate[0].Descendants("POI").Count() == 1, "Rejected move is atomic");
string testFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TacoStufferChecks_" + Guid.NewGuid().ToString("N"));
System.IO.Directory.CreateDirectory(System.IO.Path.Combine(testFolder, "TEXTURES"));
try
{
    byte[] originalAsset = { 1, 2, 3, 4 };
    System.IO.File.WriteAllBytes(System.IO.Path.Combine(testFolder, "TEXTURES", "real.dds"), originalAsset);
    string packXml = "<OverlayData><MarkerCategory name=\"pack\" iconFile=\"TEXTURES/missing.jpg\"/><POIs><Trail MapID=\"1438\" trailData=\"DATA/empty.trl\" texture=\"TEXTURES/real.dds\"/></POIs></OverlayData>";
    string output = System.IO.Path.Combine(testFolder, "test.taco");
    var result = TestPackBuilder.Build(output, new[] { ("first.xml", packXml), ("second.xml", "<OverlayData><POIs/></OverlayData>") }, testFolder);
    Check(result.XmlFiles == 2 && result.Placeholders.Count == 2, "Test build includes every XML and reports generated assets");
    using (var archive = System.IO.Compression.ZipFile.OpenRead(output))
    {
        Check(archive.GetEntry("first.xml") != null && archive.GetEntry("second.xml") != null, "XMLs are stored at pack root");
        using var xmlReader = new System.IO.StreamReader(archive.GetEntry("first.xml")!.Open());
        var exported = XDocument.Parse(xmlReader.ReadToEnd());
        Check((string?)exported.Descendants("MarkerCategory").Single().Attribute("iconFile") == "TEXTURES/missing.jpg.placeholder.png", "Missing non-PNG path rewritten only in exported snapshot");
        using var pngStream = archive.GetEntry("TEXTURES/missing.jpg.placeholder.png")!.Open();
        byte[] signature = new byte[8];
        pngStream.ReadExactly(signature);
        Check(signature.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "Placeholder is actual PNG data");
        using var trailReader = new System.IO.BinaryReader(archive.GetEntry("DATA/empty.trl")!.Open());
        Check(trailReader.ReadInt32() == 0 && trailReader.ReadInt32() == 1438 && archive.GetEntry("DATA/empty.trl")!.Length == 8, "Empty trail has version/map header only");
        using var real = new System.IO.MemoryStream();
        using var asset = archive.GetEntry("TEXTURES/real.dds")!.Open();
        asset.CopyTo(real);
        Check(real.ToArray().SequenceEqual(originalAsset), "Existing assets copied without alteration");
    }
    byte[] beforeRejected = System.IO.File.ReadAllBytes(output);
    try
    {
        TestPackBuilder.Build(output, new[] { ("bad.xml", "<OverlayData><POI iconFile=\"../outside.png\"/></OverlayData>") }, null);
        throw new Exception("Traversal accepted");
    }
    catch (InvalidOperationException)
    {
    }
    Check(System.IO.File.ReadAllBytes(output).SequenceEqual(beforeRejected), "Failed build retains prior output");
}
finally
{
    System.IO.Directory.Delete(testFolder, true);
}
string projectTestRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CilantroChecks_" + Guid.NewGuid().ToString("N"));
System.IO.Directory.CreateDirectory(projectTestRoot);
try
{
    var snapshot = new ProjectSnapshot("My Markers", "", new() { new ProjectXml("markers.xml", "<OverlayData><MarkerCategory name=\"my_markers\" DisplayName=\"My Markers\"/><POIs/></OverlayData>") }, new());
    string cilantro = ProjectStore.Create(snapshot, projectTestRoot);
    string folder = System.IO.Path.GetDirectoryName(cilantro)!;
    Check(System.IO.Directory.Exists(System.IO.Path.Combine(folder, "DATA")) && System.IO.Directory.Exists(System.IO.Path.Combine(folder, "TEXTURES")) && System.IO.Directory.Exists(System.IO.Path.Combine(folder, "CUSTOM_ATTRIBUTES")), "New project creates required folders");
    Check(!System.IO.Directory.Exists(System.IO.Path.Combine(folder, "EXPORT")), "EXPORT is deferred until fallback build");
    Check(ProjectStore.Load(cilantro).Documents.Count == 1, "New project has one XML");
    Check(ProjectStore.ExportPath(cilantro, snapshot) == System.IO.Path.Combine(folder, "EXPORT", "MyMarkers.taco"), "Blank output uses EXPORT and no-space name");
    var renamed = snapshot with { Documents = new() { new ProjectXml("renamed.xml", snapshot.Documents[0].Content, false) } };
    ProjectStore.Save(cilantro, renamed);
    string unchangedFile = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(cilantro)!, renamed.Documents[0].Name);
    var oldTimestamp = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    System.IO.File.SetLastWriteTimeUtc(unchangedFile, oldTimestamp);
    ProjectStore.Save(cilantro, renamed);
    Check(System.IO.File.GetLastWriteTimeUtc(unchangedFile) == oldTimestamp, "Saving identical XML preserves the file timestamp");
    Check(!System.IO.File.Exists(System.IO.Path.Combine(folder, "markers.xml")) && System.IO.File.Exists(System.IO.Path.Combine(folder, "renamed.xml")), "Save reconciles renamed tracked XMLs");
    var loaded = ProjectStore.Load(cilantro);
    Check(loaded.Documents[0].Name == "renamed.xml" && !loaded.Documents[0].Open, "Load retains renamed closed XML");
    string customOutput = System.IO.Path.Combine(projectTestRoot, "ReaderPacks");
    var external = renamed with { OutputFolder = customOutput, NextMarkerNumber = 87 };
    ProjectStore.Save(cilantro, external);
    Check(ProjectStore.Load(cilantro).OutputFolder == customOutput && ProjectStore.ExportPath(cilantro, external) == System.IO.Path.Combine(customOutput, "MyMarkers.taco"), "Custom output persists");
    Check(!System.IO.Directory.Exists(customOutput), "Saving settings does not create custom output directories");
    Check(ProjectStore.Load(cilantro).NextMarkerNumber == 87, "Marker counter survives project save/load independently of marker names");
    Check(!System.IO.Directory.Exists(System.IO.Path.Combine(folder, "EXPORT")), "Custom output never creates EXPORT");
    System.IO.Directory.CreateDirectory(System.IO.Path.Combine(folder, "EXPORT"));
    System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "EXPORT", "do-not-package.txt"), "exclude me");
    string testOutput = System.IO.Path.Combine(folder, "EXPORT", "MyMarkers.taco");
    TestPackBuilder.Build(testOutput, loaded.Documents.Select(d => (d.Name, d.Content)).ToList(), folder);
    using (var archive = System.IO.Compression.ZipFile.OpenRead(testOutput)) Check(!archive.Entries.Any(e => e.FullName.StartsWith("EXPORT/", StringComparison.OrdinalIgnoreCase)), "Export folder is never packaged");
    var beforeManifest = System.IO.File.ReadAllBytes(cilantro);
    try
    {
        ProjectStore.Create(snapshot, projectTestRoot);
        throw new Exception("Existing project folder overwritten");
    }
    catch (InvalidOperationException)
    {
    }
    Check(System.IO.File.ReadAllBytes(cilantro).SequenceEqual(beforeManifest), "Creation refuses existing project without mutation");
    System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "foreign.xml"), "<OverlayData/>");
    try
    {
        ProjectStore.Save(cilantro, renamed with { Documents = new() { new ProjectXml("foreign.xml", snapshot.Documents[0].Content) } });
        throw new Exception("Untracked file overwritten");
    }
    catch (InvalidOperationException)
    {
    }
    Check(System.IO.File.ReadAllBytes(cilantro).SequenceEqual(beforeManifest), "Rejected save retains manifest");
}
finally
{
    System.IO.Directory.Delete(projectTestRoot, true);
}
var inconsistent = new[] {
    Doc("<MarkerCategory name=\"heading\"/><MarkerCategory name=\"portals\"/><MarkerCategory name=\"ziplines\"/>", "<POI type=\"pack.heading\"/><POI type=\"pack.portals\"/><Trail type=\"pack.ziplines\"/>"),
    Doc("<MarkerCategory name=\"ziplines\"/><MarkerCategory name=\"heading\"/><MarkerCategory name=\"portals\"/>", "<POI type=\"pack.heading\"/><POI type=\"pack.portals\"/><Trail type=\"pack.ziplines\"/>")
};
var canonical = CategoryOrder.Collect(inconsistent);
var consistent = CategoryOrder.Normalize(inconsistent, canonical);
Check(consistent[0].Descendants("MarkerCategory").Select(CategoryOperations.PathOf).SequenceEqual(consistent[1].Descendants("MarkerCategory").Select(CategoryOperations.PathOf)), "Shared sibling order must match across XMLs");
Check(inconsistent[1].Descendants("MarkerCategory").ElementAt(1).Attribute("name")!.Value == "ziplines", "Normalization must not mutate input");
var reorderedDocs = CategoryOrder.Normalize(new[] { consistent[1], consistent[0] }, canonical);
Check(reorderedDocs[0].Descendants("MarkerCategory").Select(CategoryOperations.PathOf).SequenceEqual(consistent[0].Descendants("MarkerCategory").Select(CategoryOperations.PathOf)), "XML order changes must retain saved category order");
var sparse = new[] { Doc("<MarkerCategory name=\"a\"/><MarkerCategory name=\"b\"/><MarkerCategory name=\"c\"/>", "<POI type=\"pack.a\"/>"), Doc("<MarkerCategory name=\"a\"><MarkerCategory name=\"latechild\"/></MarkerCategory>") };
var sparseOrder = CategoryOrder.Collect(sparse);
var afterA = CategoryOperations.Move(sparse, "pack.c", "pack.a", CategoryDropPosition.After, out _, sparseOrder);
Check(afterA[0].Root!.Element("MarkerCategory")!.Elements("MarkerCategory").Select(c => (string?)c.Attribute("name")).SequenceEqual(new[] { "a", "c", "b" }), "After drop belongs immediately after target even when target children appear in a later XML");
var partial = CategoryOrder.Normalize(new[] { Doc("<MarkerCategory name=\"ziplines\"/><MarkerCategory name=\"portals\"/>") }, canonical);
Check(partial[0].Descendants("MarkerCategory").Count() == 3, "Normalization must not synthesize absent declarations");
Check(partial[0].Root!.Element("MarkerCategory")!.Elements("MarkerCategory").First().Attribute("name")!.Value == "portals", "Subset order respects canonical siblings");
var separateFiles = new[] { Doc("<MarkerCategory name=\"a\"/>", "<POI type=\"pack.a\"/>"), Doc("<MarkerCategory name=\"b\"/>", "<Trail type=\"pack.b\"/>") };
var noCopies = CategoryOperations.Move(separateFiles, "pack.b", "pack.a", CategoryDropPosition.Before, out _);
Check(!noCopies[0].Descendants("MarkerCategory").Any(c => CategoryOperations.PathOf(c) == "pack.b") && !noCopies[1].Descendants("MarkerCategory").Any(c => CategoryOperations.PathOf(c) == "pack.a"), "Cross-XML ordering must not copy sibling declarations");
var oldCopies = new[] { Doc("<MarkerCategory name=\"a\"/><MarkerCategory name=\"b\"/>", "<POI type=\"pack.a\"/>"), Doc("<MarkerCategory name=\"b\"/>", "<Trail type=\"pack.b\"/>") };
var cleaned = CategoryOrder.Normalize(oldCopies);
Check(!cleaned[0].Descendants("MarkerCategory").Any(c => CategoryOperations.PathOf(c) == "pack.b") && cleaned[1].Descendants("MarkerCategory").Any(c => CategoryOperations.PathOf(c) == "pack.b"), "Empty ordering copy is removed while assigned trail category stays");
Check(cleaned.All(d => d.Root!.Elements("MarkerCategory").Any(c => (string?)c.Attribute("name") == "pack")), "Every XML retains its pack heading");
var standalone = CategoryOrder.Normalize(new[] { Doc("<MarkerCategory name=\"separator\" isSeparator=\"1\"/>") });
Check(standalone[0].Descendants("MarkerCategory").Count() == 2, "Unique empty category/separator must remain");
var parentCategory = XElement.Parse("<MarkerCategory name=\"pack\" alpha=\"0.25\"><MarkerCategory name=\"child\"/></MarkerCategory>");
var childCategory = parentCategory.Element("MarkerCategory")!;
var inheritedCategories = new Dictionary<string, XElement> { ["pack"] = parentCategory, ["pack.child"] = childCategory };
Check(AttributeRules.Effective(childCategory, "alpha", "1", inheritedCategories) == "1", "Child category must not inherit parent alpha");
childCategory.SetAttributeValue("alpha", "0.75");
Check(AttributeRules.Effective(new XElement("POI", new XAttribute("type", "pack.child")), "alpha", "1", inheritedCategories) == "0.75", "Marker inherits its assigned category");
Check(AttributeRules.Effective(new XElement("Trail", new XAttribute("type", "pack.child")), "alpha", "1", inheritedCategories) == "0.75", "Trail inherits its assigned category");
Check(AttributeRules.Effective(new XElement("POI", new XAttribute("type", "pack.child"), new XAttribute("alpha", "0.9")), "alpha", "1", inheritedCategories) == "0.9", "Local element value wins");
childCategory.Attribute("alpha")!.Remove();
Check(AttributeRules.Effective(new XElement("POI", new XAttribute("type", "pack.child")), "alpha", "1", inheritedCategories) == "0.25", "Marker resolves ancestor appearance without category inheritance");
var nineCategories = Enumerable.Range(0, 9).Select(i => new XElement("MarkerCategory", new XAttribute("name", "c" + i), new XAttribute("alpha", i.ToString()))).ToList();
AttributeRules.Apply(nineCategories, new Dictionary<string, string?> { ["alpha"] = "0.5", ["color"] = "ff00ff" }, (_, _) => true);
Check(nineCategories.All(c => (string?)c.Attribute("alpha") == "0.5" && (string?)c.Attribute("color") == "ff00ff"), "Batch sets identical values on all nine categories");
var incompatible = new[] { new XElement("POI"), new XElement("Trail") };
try
{
    AttributeRules.Apply(incompatible, new Dictionary<string, string?> { ["iconFile"] = "TEXTURES/test.png" }, (e, _) => e.Name != "Trail");
    throw new Exception("Mixed unsupported batch accepted");
}
catch (InvalidOperationException)
{
}
Check(incompatible.All(e => e.Attribute("iconFile") == null), "Rejected mixed batch leaves every element unchanged");
AttributeRules.Apply(nineCategories, new Dictionary<string, string?> { ["alpha"] = null }, (_, _) => true);
Check(nineCategories.All(c => c.Attribute("alpha") == null), "Batch removal affects all targets");
byte[] MumbleFrame()
{
    var bytes = new byte[MumbleSnapshot.FrameSize];
    void U(int offset, uint value) => System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);
    void F(int offset, float value) => System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), BitConverter.SingleToInt32Bits(value));
    U(0, 2);
    U(4, 100);
    U(1104, 48);
    U(1136, 15);
    U(1140, 5);
    U(1156, 8);
    U(1188, 1234);
    F(8, 1.25f);
    F(12, 2.5f);
    F(16, -3.75f);
    F(556, 4);
    F(568, 1);
    F(584, 1);
    System.Text.Encoding.Unicode.GetBytes("{\"name\":\"Test Character\",\"map_id\":15,\"fov\":1.2}").CopyTo(bytes, 592);
    return bytes;
}
var mumbleBytes = MumbleFrame();
Check(MumbleSnapshot.MappingSize == 5460 && MumbleSnapshot.FrameSize == 1364, "Mumble allocation includes the full description while decoding only the telemetry prefix");
if (OperatingSystem.IsWindows())
{
    // A private mapping simulates a fresh install without touching the game's real link.
    string testLink = "TacoStufferMumbleCheck_" + Guid.NewGuid().ToString("N");
    using var consumer = new TacoStuffer.Services.MumbleLinkReader(testLink);
    Check(consumer.Read() == null && consumer.Error == null, "Reader creates an empty link and waits for genuine telemetry");
    using var producer = System.IO.MemoryMappedFiles.MemoryMappedFile.OpenExisting(testLink, System.IO.MemoryMappedFiles.MemoryMappedFileRights.ReadWrite);
    using var producerView = producer.CreateViewAccessor(0, MumbleSnapshot.MappingSize, System.IO.MemoryMappedFiles.MemoryMappedFileAccess.ReadWrite);
    producerView.Write(MumbleSnapshot.MappingSize - 1, (byte)0x5a);
    var published = MumbleFrame();
    BitConverter.GetBytes((uint)123).CopyTo(published, 4);
    producerView.WriteArray(0, published, 0, published.Length);
    Check(consumer.Read()?.Tick == 123, "GW2-style producer can populate a link created first by Taco Stuffer");
    using var secondConsumer = new TacoStuffer.Services.MumbleLinkReader(testLink);
    Check(secondConsumer.Read()?.Tick == 123 && producerView.ReadByte(MumbleSnapshot.MappingSize - 1) == 0x5a, "An additional consumer preserves existing telemetry and description bytes");
    BitConverter.GetBytes((uint)124).CopyTo(published, 4);
    producerView.WriteArray(0, published, 0, published.Length);
    Check(consumer.Read()?.Tick == 124, "Created link continues receiving fresh game frames");
    consumer.Dispose();
    Check(consumer.Read()?.Tick == 124 && producerView.ReadByte(MumbleSnapshot.MappingSize - 1) == 0x5a, "Reconnect reuses the producer mapping without resetting it");
}
var frame = MumbleSnapshot.Decode(mumbleBytes)!;
Check(frame != null && frame.ProcessId == 1234 && frame.MapId == 15 && frame.CharacterName == "Test Character", "Decode GW2 UTF-16 identity and extended context despite declared length 48");
Check(frame.AvatarPosition.X == 1.25f && frame.AvatarPosition.Z == -3.75f && frame.CameraPosition.X == 4, "Position offsets must match Windows MumbleLink layout");
Check(MumbleSnapshot.Decode(mumbleBytes.AsSpan(0, 1195)) == null, "Short memory frame rejected");
var mismatch = MumbleFrame();
System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(mismatch.AsSpan(1136, 4), 99);
Check(MumbleSnapshot.Decode(mismatch) == null, "Inconsistent identity/context maps rejected");
var invalidPosition = MumbleFrame();
BitConverter.GetBytes(float.NaN).CopyTo(invalidPosition, 8);
Check(MumbleSnapshot.Decode(invalidPosition) == null, "Nonfinite position rejected");
var inactivePosition = MumbleFrame();
Array.Clear(inactivePosition, 8, 12);
Check(MumbleSnapshot.Decode(inactivePosition) == null, "Zeroed inactive avatar telemetry rejected");
var noMap = MumbleFrame();
Array.Clear(noMap, 1136, 4);
Check(MumbleSnapshot.Decode(noMap) == null, "Zero map rejected");
var noProcess = MumbleFrame();
Array.Clear(noProcess, 1188, 4);
Check(MumbleSnapshot.Decode(noProcess) == null, "Unidentified writer process rejected");
var malformed = MumbleFrame();
malformed[592] = (byte)'!';
Check(MumbleSnapshot.Decode(malformed) == null, "Malformed identity rejected");
var gate = new MumbleSession();
gate.Observe(frame, 0);
gate.Observe(frame, 600);
Check(!gate.IsPlayable, "A stale first snapshot must never allow an overlay");
gate.Observe(frame with { Tick = 101 }, 650);
Check(gate.IsPlayable && gate.IsLive, "Stable character/map with advancing tick permits overlay");
gate.Observe(frame with { Tick = 101 }, 2200);
Check(!gate.IsPlayable, "Frozen tick expires readiness");
gate.Observe(frame with { Tick = 102 }, 2250);
Check(!gate.IsPlayable, "Recovery requires a fresh stability interval");
gate.Observe(frame with { Tick = 103 }, 2800);
Check(gate.IsPlayable, "Fresh telemetry recovers after stability interval");
gate.Observe(frame with { MapId = 99, Tick = 104 }, 2900);
Check(!gate.IsPlayable, "Map transition resets readiness");
gate.Observe(null, 3000);
Check(!gate.IsLive && !gate.IsPlayable, "Lost link resets readiness");
foreach (uint type in new uint[] { 0, 1, uint.MaxValue })
{
    gate.Reset();
    gate.Observe(frame with { MapType = type }, 0);
    gate.Observe(frame with { MapType = type, Tick = 101 }, 600);
    Check(!gate.IsPlayable, "Redirect, character-create and unknown map types cannot open the overlay");
}
gate.Reset();
gate.Observe(frame, 0);
gate.Observe(frame with { ProcessId = 9999, Tick = 101 }, 600);
Check(!gate.IsPlayable, "Changing the linked process must reset readiness");
var cameraFrame = frame with { CameraPosition = System.Numerics.Vector3.Zero, CameraFront = System.Numerics.Vector3.UnitZ, Fov = MathF.PI / 2 };
Check(CameraProjection.TryProject(cameraFrame, new(0, 0, 10), 1000, 500, out var center) && MathF.Abs(center.X - 500) < 0.001 && MathF.Abs(center.Y - 250) < 0.001, "Point straight ahead projects to viewport center");
Check(CameraProjection.TryProject(cameraFrame, new(2, 0, 10), 1000, 500, out var rightPoint) && MathF.Abs(rightPoint.X - 550) < 0.001, "Positive camera-right projects right using vertical FOV");
Check(CameraProjection.TryProject(cameraFrame, new(0, 2, 10), 1000, 500, out var upPoint) && MathF.Abs(upPoint.Y - 200) < 0.001, "Positive world Y projects upward, with no decoration coordinate conversion");
Check(!CameraProjection.TryProject(cameraFrame, new(0, 0, -1), 1000, 500, out _) && !CameraProjection.TryProject(cameraFrame, new(0, 0, 0.05f), 1000, 500, out _), "Behind-camera and near-plane points rejected");
var yawCamera = cameraFrame with { CameraFront = System.Numerics.Vector3.UnitX };
Check(CameraProjection.TryProject(yawCamera, new(10, 0, -2), 1000, 500, out var yawPoint) && MathF.Abs(yawPoint.X - 550) < 0.001, "Yaw follows Deco Tools cross-product handedness");
var movedCamera = cameraFrame with { CameraPosition = new(10, 20, 30) };
Check(CameraProjection.TryProject(movedCamera, new(10, 20, 40), 1000, 500, out var movedCenter) && MathF.Abs(movedCenter.X - 500) < 0.001 && MathF.Abs(movedCenter.Y - 250) < 0.001, "Camera translation is subtracted before projection");
var pitchedCamera = cameraFrame with { CameraFront = System.Numerics.Vector3.Normalize(new(0, 1, 1)) };
Check(CameraProjection.TryProject(pitchedCamera, pitchedCamera.CameraFront * 10, 1000, 500, out var pitchPoint) && MathF.Abs(pitchPoint.X - 500) < 0.001 && MathF.Abs(pitchPoint.Y - 250) < 0.001, "Pitched camera center follows forward vector");
var recordA = Doc("<MarkerCategory name=\"a\"/>");
var recordB = Doc("<MarkerCategory name=\"b\"/>");
var recordDocs = new[] { recordA, recordB };
var recordedFirst = MarkerRecording.Create(recordA, recordDocs, "pack.a", 15, new(1.25f, 2.5f, -3.75f), 1);
Check((string?)recordedFirst.Poi.Attribute("name") == "marker_00001" && (string?)recordedFirst.Poi.Attribute("DisplayName") == "Marker 00001", "Marker identity uses five-digit minimum padding");
Check((string?)recordedFirst.Poi.Attribute("type") == "pack.a" && (uint?)recordedFirst.Poi.Attribute("MapID") == 15 && (string?)recordedFirst.Poi.Attribute("ypos") == "2.5", "Record target category, map and unconverted XYZ");
Check(recordedFirst.Poi.Attributes().Count() == 8 && recordedFirst.Poi.Attribute("iconFile") == null && recordedFirst.Poi.Attribute("fadeNear") == null, "Only required identity/location attributes are emitted");
Check(Convert.FromBase64String(recordedFirst.Poi.Attribute("GUID")!.Value).Length == 16, "GUID uses TacO-compatible Base64 of 16 bytes");
recordA.Root!.Element("POIs")!.Add(recordedFirst.Poi);
recordedFirst.Poi.SetAttributeValue("name", "hello");
recordedFirst.Poi.SetAttributeValue("DisplayName", "Renamed");
var recordedSecond = MarkerRecording.Create(recordB, recordDocs, "pack.b", 15, new(2, 3, 4), recordedFirst.NextNumber);
Check((string?)recordedSecond.Poi.Attribute("DisplayName") == "Marker 00002" && recordedSecond.NextNumber == 3, "Category/XML change and renaming do not reset the project counter");
Check(recordedFirst.Poi.Attribute("GUID")!.Value != recordedSecond.Poi.Attribute("GUID")!.Value, "Markers have distinct GUIDs");
recordB.Root!.Element("POIs")!.Add(new XElement("POI", new XAttribute("DisplayName", "Marker 00042")));
Check(MarkerRecording.NextNumber(recordDocs, 3) == 43 && MarkerRecording.NextNumber(recordDocs, 87) == 87, "Existing manually typed names advance numbering while saved counter never decreases");
string beforeRejectedMarker = recordA.ToString();
try
{
    MarkerRecording.Create(recordA, recordDocs, "pack.b", 15, new(1, 2, 3), 1);
    throw new Exception("Wrong-XML category accepted");
}
catch (InvalidOperationException)
{
}
Check(recordA.ToString() == beforeRejectedMarker, "Wrong target rejected without mutation");
var previousCulture = System.Globalization.CultureInfo.CurrentCulture;
try
{
    System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");
    var invariantMarker = MarkerRecording.Create(recordA, recordDocs, "pack.a", 15, new(1.2345f, 2.5f, -3.75f), 87);
    Check(invariantMarker.Poi.Attribute("xpos")!.Value.Contains('.') && !invariantMarker.Poi.Attribute("xpos")!.Value.Contains(','), "Recorded decimals stay invariant under comma-decimal Windows locales");
}
finally
{
    System.Globalization.CultureInfo.CurrentCulture = previousCulture;
}
Check(Math.Abs(MarkerPreviewMath.Opacity(0.8f, 0.0254f * 360, 350, 370) - 0.4f) < 0.0001f, "Fade midpoint uses TacO game-distance units and multiplies alpha");
Check(MarkerPreviewMath.Opacity(1, 20, 350, 370) == 0 && MarkerPreviewMath.Opacity(0.5f, 20, -1, -1) == 0.5f, "Past-far markers fade out; absent fade stays opaque");
Check(MarkerPreviewMath.Opacity(1, 20, 350, 350) == 0, "Equal fade thresholds avoid division by zero");
Check(MarkerPreviewMath.AxisDelta(new(20, 100), System.Numerics.Vector2.UnitX, 0.1f, 0) == new System.Numerics.Vector3(2, 0, 0), "X handle ignores perpendicular pointer motion and leaves other coordinates fixed");
Check(MarkerPreviewMath.PlaneDelta(new(10, -20), System.Numerics.Vector3.UnitX, System.Numerics.Vector3.UnitY, 0.1f) == new System.Numerics.Vector3(1, 2, 0), "Center handle follows the camera plane in raw Y-up coordinates");
var inheritedPoi = new XElement("POI", new XAttribute("type", "pack.child"));
parentCategory.SetAttributeValue("iconFile", "TEXTURES/test.png");
string inheritedBefore = inheritedPoi.ToString();
Check(AttributeRules.Effective(inheritedPoi, "iconFile", "", inheritedCategories) == "TEXTURES/test.png" && inheritedPoi.ToString() == inheritedBefore, "Ancestor icon resolution does not materialize attributes on POIs");
Check(AttributeRules.Effective(childCategory, "iconFile", "", inheritedCategories) == "", "Categories stay local even while descendant POIs resolve ancestors");

// Sampling density is tied to changed positions, not idle/render-frame count.
for (int level = 1; level <= 10; level++)
{
    var sampler = new TrailSampler { Smoothness = level };
    int recorded = 0;
    for (int i = 0; i <= 512; i++) if (sampler.ShouldRecord(new(i + 1, 2, 3))) recorded++;
    Check(recorded == 1 + 512 / (1 << (10 - level)), "Each smoothness step halves changed-position recording density");
    Check(sampler.Latest == new System.Numerics.Vector3(513, 2, 3), "Downsampling retains the latest position for pause/stop endpoint flush");
    Check(!sampler.ShouldRecord(new(513, 2, 3)), "Idle frames do not consume the sampling interval");
    sampler.Reset();
    Check(sampler.ShouldRecord(new(600, 2, 3)), "Resume/new segment always records its first position");
}
var sparseSampler = new TrailSampler { Smoothness = 9 };
Check(sparseSampler.ShouldRecord(new(1, 2, 3)) && !sparseSampler.ShouldRecord(new(2, 2, 3)) && sparseSampler.Latest == new System.Numerics.Vector3(2, 2, 3), "Half-density sampling retains an unsampled final endpoint");
Check(!sparseSampler.ShouldRecord(System.Numerics.Vector3.Zero) && !sparseSampler.ShouldRecord(new(float.NaN, 2, 3)), "Invalid positions do not enter the sampler");

Check(new TrailSampler().Smoothness == 8 && new TrailSampler().Interval == 4, "New recording sessions default to quarter-density level 8");
var legacyTrail = new EditableTrail { Number = 1, MapId = 15, Guid = "legacy-guid" };
var legacyXml = XDocument.Parse("<OverlayData><MarkerCategory name='pack'><MarkerCategory name='trail_0001' DisplayName='Trail 0001'/></MarkerCategory><POIs><Trail GUID='legacy-guid' type='pack.trail_0001'/></POIs></OverlayData>");
Check(EditableTrails.RemoveLegacyCategories([legacyXml], [legacyTrail]) == 1 && (string?)legacyXml.Descendants("Trail").Single().Attribute("type") == "pack", "Untouched generated trail folders are removed and the trail returns to its requested parent");
var customizedLegacy = XDocument.Parse("<OverlayData><MarkerCategory name='pack'><MarkerCategory name='trail_0001' DisplayName='Trail 0001' alpha='0.5'/></MarkerCategory><POIs><Trail GUID='legacy-guid' type='pack.trail_0001'/></POIs></OverlayData>");
Check(EditableTrails.RemoveLegacyCategories([customizedLegacy], [legacyTrail]) == 0, "Customized trail categories retain their attributes and hierarchy");
var preparedProjection = new CameraProjection.Frame(cameraFrame, 1000, 500);
Check(preparedProjection.TryProject(new(2, 2, 10), out var preparedPoint) && System.Numerics.Vector2.Distance(preparedPoint, new(550, 200)) < .001f, "Prepared camera basis projects multiple vertices with the established camera math");

var clippingCamera = new CameraProjection.Frame(cameraFrame, 1000, 500);
Span<RibbonVertex> clippedRibbon = stackalloc RibbonVertex[12];
int clippedCount = RibbonProjection.Clip(clippingCamera, new(new(0, 0, -1), new(0, 0)), new(new(1, 1, 10), new(1, 1)), new(new(-1, 1, 10), new(0, 1)), clippedRibbon);
Check(clippedCount >= 3, "A triangle crossing behind the camera retains its visible polygon");
for (int i = 0; i < clippedCount; i++)
{
    var vertex = clippedRibbon[i];
    Check(clippingCamera.TryProject(vertex.Position, out var projectedClip) && projectedClip.X >= -.1f && projectedClip.X <= 1000.1f && projectedClip.Y >= -.1f && projectedClip.Y <= 500.1f, "Clipped ribbon vertices project into the viewport");
    Check(Math.Abs(vertex.Position.Z - (-1 + 11 * vertex.Uv.Y)) < .001f, "Clipping interpolates original distance UVs rather than restarting or stretching repeats");
}
Check(RibbonProjection.Clip(clippingCamera, new(new(-1, 0, -2), new(0, 0)), new(new(1, 0, -2), new(1, 0)), new(new(0, 1, -2), new(0, 1)), clippedRibbon) == 0, "Entirely behind-camera triangles are rejected");

var adjustable = new EditableTrail { Number = 8, MapId = 15, Guid = Convert.ToBase64String(Guid.NewGuid().ToByteArray()) };
var adjustableSegment = adjustable.AddSegment();
foreach (var position in new System.Numerics.Vector3[] { new(1, 2, 3), new(2, 4, 3), new(3, 2, 3), new(4, 4, 3), new(5, 2, 3) }) adjustableSegment.Add(position);
var originals = adjustableSegment.Points.Select(p => p.Position).ToList();
long preResampleRevision = adjustableSegment.GeometryRevision;
EditableTrails.Resample(adjustableSegment, 8);
Check(adjustableSegment.GeometryRevision == preResampleRevision + 1, "Smoothness changes invalidate cached segment geometry without a scene rebuild");
Check(adjustableSegment.Points.Select(p => p.Id).SequenceEqual(new long[] { 1, 3, 5 }), "Lowering recorded density retains source IDs and endpoints");
var savedAdjustment = EditableTrails.Deserialize(EditableTrails.Serialize(adjustable));
EditableTrails.Resample(savedAdjustment.Segments[0], 9);
Check(savedAdjustment.Segments[0].Points.Select(p => p.Position).SequenceEqual(originals), "Saved source restores original bends after reducing then restoring density");
EditableTrails.Resample(adjustableSegment, 10);
Check(adjustableSegment.Points.Count == 9 && adjustableSegment.Points[1].Position == (originals[0] + originals[1]) / 2, "Higher density inserts midpoint samples along the source polyline");
Check(adjustableSegment.Points.First().Position == originals.First() && adjustableSegment.Points.Last().Position == originals.Last() && adjustableSegment.Points.Select(p => p.Id).Distinct().Count() == 9, "Interpolation preserves endpoints and allocates distinct IDs");
Check(EditableTrails.Compile(adjustable).Length == 8 + 9 * 12, "TRL export uses the adjusted points, excluding the retained source copy");
adjustableSegment.ResetResampling();
Check(adjustableSegment.ResampleSource == null, "Manual editing can discard the adjustment baseline before rebasing");

var editableRoute = new EditableTrail { Number = 1, Guid = Convert.ToBase64String(Guid.NewGuid().ToByteArray()), MapId = 15 };
var routeA = editableRoute.AddSegment();
routeA.Add(new(1, 2, 3));
routeA.Add(new(2, 2, 3));
routeA.Add(new(3, 2, 3));
Check(!routeA.Add(new(3, 2, 3)) && routeA.Points.Count == 3, "Repeated telemetry coordinates do not duplicate trail samples");
routeA.Points.RemoveAt(1);
routeA.Add(new(4, 2, 3));
Check(routeA.Points.Select(p => p.Id).SequenceEqual(new long[] { 1, 3, 4 }), "Point IDs survive deletion and new points use the segment high-water mark");
var routeB = editableRoute.AddSegment();
routeB.Add(new(10, 4, 3));
routeB.Add(new(11, 4, 3));
Check(routeB.Id == 2 && routeB.Points[0].Id == 1, "Each trail has local segment indexes; points start again in a new segment");
string editableBeforeBuild = EditableTrails.Serialize(editableRoute);
byte[] officialTrail = EditableTrails.Compile(editableRoute);
using (var binary = new System.IO.BinaryReader(new System.IO.MemoryStream(officialTrail)))
{
    Check(binary.ReadInt32() == 0 && binary.ReadUInt32() == 15, "TRL version 0/map header uses TacO layout");
    for (int i = 0; i < 3; i++)
    {
        Check(binary.ReadSingle() == routeA.Points[i].X && binary.ReadSingle() == routeA.Points[i].Y && binary.ReadSingle() == routeA.Points[i].Z, "TRL first segment contains raw XYZ float triples");
    }
    Check(binary.ReadSingle() == 0 && binary.ReadSingle() == 0 && binary.ReadSingle() == 0, "Disconnected segments have a zero triple instead of a connecting edge");
    Check(binary.ReadSingle() == 10 && binary.ReadSingle() == 4 && binary.ReadSingle() == 3, "Second segment starts at its own recorded position");
}
Check(EditableTrails.Serialize(editableRoute) == editableBeforeBuild, "Compilation does not mutate editable data or counters");
var trlRoundTrip = EditableTrails.Import(officialTrail, 1, editableRoute.Guid);
Check(trlRoundTrip.Segments.Count == 2 && trlRoundTrip.Segments[1].Points.Count == 2 && trlRoundTrip.MapId == 15, "Official TRL imports as editable segments");
var jsonRoute = EditableTrails.Deserialize(editableBeforeBuild);
Check(jsonRoute.Segments[0].Points[1].Id == 3 && jsonRoute.Segments[0].NextPoint == 5, "Editable JSON preserves indexes and high-water marks");
try
{
    routeA.Add(System.Numerics.Vector3.Zero);
    throw new Exception("Zero sentinel accepted as a real point");
}
catch (InvalidOperationException)
{
}
var rigidPivot = EditableTrails.Pivot(new System.Numerics.Vector3[] { new(1, 2, 3), new(3, 2, 3) });
var rotatedA = EditableTrails.Rotate(new(1, 2, 3), rigidPivot, 1, MathF.PI / 2);
var rotatedB = EditableTrails.Rotate(new(3, 2, 3), rigidPivot, 1, MathF.PI / 2);
Check(System.Numerics.Vector3.Distance(EditableTrails.Pivot(new[] { rotatedA, rotatedB }), rigidPivot) < 0.0001f && Math.Abs(System.Numerics.Vector3.Distance(rotatedA, rotatedB) - 2) < 0.0001f, "Rigid group rotation preserves pivot and point spacing");
var mapFrame = frame with { AvatarPosition = new(1, 2, 3), UiState = 0, Compass = new CompassTelemetry(200, 200, 0, new(100, 100), new(100, 100), 1, 1) };
var mapRect = MapProjection.Viewport(mapFrame, 1920, 1080);
Check(mapRect.X == 1720 && mapRect.Y == 843, "Bottom minimap rectangle follows compass size and UI offset");
Check(MapProjection.TryProject(mapFrame, mapFrame.AvatarPosition, mapRect, 1920, 1080, out var mapPlayer) && System.Numerics.Vector2.Distance(mapPlayer, new(1820, 943)) < 0.001f, "Player-centered map places avatar at compass center");
Check(MapProjection.TryProject(mapFrame, mapFrame.AvatarPosition + new System.Numerics.Vector3(0.0254f * 24, 0, 0), mapRect, 1920, 1080, out var mapEast) && Math.Abs(mapEast.X - mapPlayer.X - 1) < 0.001f, "24 game inches convert to one continent unit horizontally");
var fullMapFrame = mapFrame with { UiState = 1, Compass = mapFrame.Compass! with { Center = new(102, 100) } };
var fullRect = MapProjection.Viewport(fullMapFrame, 1920, 1080);
Check(MapProjection.TryProject(fullMapFrame, fullMapFrame.AvatarPosition, fullRect, 1920, 1080, out var panned) && Math.Abs(panned.X - 958) < 0.001f, "Full-map panning moves the marker with the supplied map center");
string editableRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TacoStufferEditableChecks_" + Guid.NewGuid().ToString("N"));
System.IO.Directory.CreateDirectory(editableRoot);
try
{
    string routeXml = "<OverlayData><MarkerCategory name=\"route\"/><POIs><Trail GUID=\"" + editableRoute.Guid + "\" MapID=\"15\" type=\"route\" trailData=\"DATA/trail_0001.trl\"/></POIs></OverlayData>";
    var routeProject = new ProjectSnapshot("Editable Routes", "", [new ProjectXml("routes.xml", routeXml)], new(), Trails: [editableRoute], NextTrailNumber: 2);
    string projectPath = ProjectStore.Create(routeProject, editableRoot);
    string routeFolder = System.IO.Path.GetDirectoryName(projectPath)!;
    var reopened = ProjectStore.Load(projectPath);
    Check(reopened.Trails!.Count == 1 && reopened.Trails[0].Segments[0].Points.Count == 3 && reopened.NextTrailNumber == 2, "Project reopen restores editable trail state and global counter");
    byte[] savedSource = System.IO.File.ReadAllBytes(System.IO.Path.Combine(routeFolder, EditableTrails.Folder, EditableTrails.FileName(1)));
    string export = System.IO.Path.Combine(editableRoot, "routes.taco");
    TestPackBuilder.Build(export, [("routes.xml", routeXml)], routeFolder, reopened.Trails);
    using (var archive = System.IO.Compression.ZipFile.OpenRead(export))
    {
        using (var fadeXmlStream = archive.GetEntry("routes.xml")!.Open())
        {
            var fadeBuilt = XDocument.Load(fadeXmlStream).Descendants("Trail").Single();
            Check(fadeBuilt.Attribute("fadeNear") == null && fadeBuilt.Attribute("fadeFar") == null, "Export does not insert fade attributes into trails without user-authored fades");
            Check(!routeXml.Contains("fadeNear", StringComparison.Ordinal), "Export does not rewrite the editable XML source");
        }
        Check(!archive.Entries.Any(e => e.FullName.StartsWith(EditableTrails.Folder + "/")), "Editable project sources never enter the Taco archive");
        using var stream = archive.GetEntry("DATA/trail_0001.trl")!.Open();
        using var bytes = new System.IO.MemoryStream();
        stream.CopyTo(bytes);
        Check(bytes.ToArray().SequenceEqual(officialTrail), "Taco build embeds compiled route bytes with all editable segments");
    }
    Check(savedSource.SequenceEqual(System.IO.File.ReadAllBytes(System.IO.Path.Combine(routeFolder, EditableTrails.Folder, EditableTrails.FileName(1)))), "Taco export leaves the editable project file unchanged");
    var secondRoute = EditableTrails.Deserialize(editableBeforeBuild);
    secondRoute.Number = 2;
    secondRoute.Guid = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
    secondRoute.MapId = 1438;
    string firstExportXml = routeXml.Replace("DATA/trail_0001.trl", "DATA/custom_route.trl");
    string secondExportXml = $"<OverlayData><MarkerCategory name=\"route\"/><POIs><Trail GUID=\"{secondRoute.Guid}\" MapID=\"1438\" type=\"route\" trailData=\"{secondRoute.DataPath}\"/></POIs></OverlayData>";
    var allRoutes = new[] { reopened.Trails![0], secondRoute };
    TestPackBuilder.Build(export, [("routes.xml", firstExportXml), ("other.xml", secondExportXml)], routeFolder, allRoutes);
    byte[] validExport = System.IO.File.ReadAllBytes(export);
    using (var archive = System.IO.Compression.ZipFile.OpenRead(export))
    {
        foreach (string xmlName in new[] { "routes.xml", "other.xml" })
        {
            using var xmlStream = archive.GetEntry(xmlName)!.Open();
            var builtXml = XDocument.Load(xmlStream);
            var tag = builtXml.Descendants("Trail").Single();
            string dataPath = tag.Attribute("trailData")!.Value;
            var model = allRoutes.Single(t => t.Guid == tag.Attribute("GUID")!.Value);
            using var trlStream = archive.GetEntry(dataPath)!.Open();
            using var trlBytes = new System.IO.MemoryStream();
            trlStream.CopyTo(trlBytes);
            Check(trlBytes.ToArray().SequenceEqual(EditableTrails.Compile(model)), "Every XML trail reference resolves to all of its current recorded segments");
            using var header = new System.IO.BinaryReader(new System.IO.MemoryStream(trlBytes.ToArray()));
            Check(header.ReadInt32() == 0 && header.ReadUInt32() == (uint)tag.Attribute("MapID")!, "Each XML MapID matches its compiled TRL header");
        }
        Check(!archive.Entries.Any(e => e.FullName.StartsWith("EDITABLE_TRAILS/") || e.FullName.StartsWith("EXPORT/") || e.FullName.StartsWith("CUSTOM_ATTRIBUTES/") || e.FullName.EndsWith(".cilantro")), "Project/editable/output files are excluded from multi-XML exports");
    }
    void RejectExport((string Name, string Xml)[] docs, EditableTrail[] models, string reason)
    {
        bool rejected = false;
        try
        {
            TestPackBuilder.Build(export, docs, routeFolder, models);
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }
        Check(rejected, reason);
        Check(System.IO.File.ReadAllBytes(export).SequenceEqual(validExport), "Rejected export retains the last complete Taco file");
    }
    RejectExport([("routes.xml", firstExportXml)], allRoutes, "Unlinked recorded trails cannot silently disappear from export");
    RejectExport([("routes.xml", firstExportXml), ("duplicate.xml", firstExportXml)], [allRoutes[0]], "Duplicate XML references to one recorded GUID are rejected");
    RejectExport([("routes.xml", firstExportXml.Replace("MapID=\"15\"", "MapID=\"invalid\""))], [allRoutes[0]], "Invalid XML MapID produces a controlled build rejection");
    RejectExport([("routes.xml", firstExportXml), ("other.xml", secondExportXml.Replace(secondRoute.DataPath, "DATA/custom_route.trl"))], allRoutes, "Different recorded trails cannot overwrite the same TRL path");
    ProjectStore.Save(projectPath, reopened with { Documents = [] });
    Check(!System.IO.File.Exists(System.IO.Path.Combine(routeFolder, "routes.xml")) && ProjectStore.Load(projectPath).Documents.Count == 0, "Deleting the last XML is saveable and reloadable");
}
finally
{
    System.IO.Directory.Delete(editableRoot, true);
}
// GPU geometry stays in world space; sparse IDs from deleted points still
// connect directly and consume the actual world distance in repeating UVs.
var gpuRoute = new EditableTrail { Number = 42, Guid = Convert.ToBase64String(Guid.NewGuid().ToByteArray()), MapId = 15 };
var gpuSegment = gpuRoute.AddSegment();
gpuSegment.Add(new(1, 2, 3));
gpuSegment.Add(new(1, 2, 24));
gpuSegment.Points[1].Id = 15;
gpuSegment.NextPoint = 16;
var gpuVertices = GpuTrailGeometry.Build(gpuRoute, gpuSegment);
Check(System.Runtime.InteropServices.Marshal.SizeOf<TrailGpuVertex>() == 36, "GPU input stride includes independent world and projected-map distances");
Check(System.Runtime.InteropServices.Marshal.OffsetOf<TrailGpuVertex>(nameof(TrailGpuVertex.Uv)).ToInt32() == 12 && System.Runtime.InteropServices.Marshal.OffsetOf<TrailGpuVertex>(nameof(TrailGpuVertex.Center)).ToInt32() == 20, "GPU input offsets agree with the native layout");
Check(gpuVertices.Length == 4 && Math.Abs(gpuVertices[2].Uv.Y + 21) < .0001f, "A 1-to-15 point gap produces a full-length ribbon with distance-based UV repetition");
Check(Math.Abs(System.Numerics.Vector3.Distance(gpuVertices[0].Position, gpuVertices[1].Position) - 1.016f) < .0001f, "GPU ribbon width matches TacO's meter conversion");
Check(GpuTrailGeometry.Indices(2).SequenceEqual(new uint[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 }), "Each GPU ribbon edge has its own two indexed triangles");
long revision = gpuSegment.GeometryRevision;
int oldCount = gpuSegment.Points.Count;
gpuSegment.Add(new(1, 2, 29));
var appendedVertices = GpuTrailGeometry.Build(gpuRoute, gpuSegment, firstEdge: oldCount, initialLength: -gpuVertices[^1].Uv.Y);
Check(appendedVertices.Length == 4 && appendedVertices[0].Uv.Y == -21 && appendedVertices[2].Uv.Y == -26, "Streaming append preserves texture phase and only builds added edges");
Check(gpuSegment.GeometryRevision == revision + 1 && !EditableTrails.Serialize(gpuRoute).Contains("GeometryRevision", StringComparison.Ordinal), "GPU invalidation revision advances on edits and is excluded from editable JSON");
var gpuPreview = new Dictionary<string, System.Numerics.Vector3> { [$"point|42|{gpuSegment.Id}|15"] = new(5, 2, 24) };
var movedGpuVertices = GpuTrailGeometry.Build(gpuRoute, gpuSegment, gpuPreview);
Check((movedGpuVertices[2].Position + movedGpuVertices[3].Position) / 2 == gpuPreview[$"point|42|{gpuSegment.Id}|15"] && gpuSegment.Points[1].Position == new System.Numerics.Vector3(1, 2, 24), "GPU preview movement updates geometry without changing saved points");
gpuSegment.ResetResampling();
gpuSegment.Points.RemoveAt(1);
var deletedGpuVertices = GpuTrailGeometry.Build(gpuRoute, gpuSegment);
Check(deletedGpuVertices.Length == 4 && deletedGpuVertices[2].Uv.Y == -26, "Point deletion reconnects endpoints as one continuous repeating edge");
// Shared journal boundaries, branching and no-op Pause -> Stop behavior.
var historyTest = new HistoryJournal<string>(s => s, limit: 2);
historyTest.Reset("empty");
historyTest.Commit("run A", "Record trail length");
Check(!historyTest.Commit("run A", "Stop") && historyTest.UndoEntries.Count == 1, "Pause then Stop commits a run once");
historyTest.Commit("run A+B", "Record trail length");
Check(historyTest.Undo() == "run A" && historyTest.Redo() == "run A+B", "Continuing a segment is a separate reversible run");
historyTest.Undo();
Check(!historyTest.Commit("run A", "Selection") && historyTest.CanRedo, "Selection/no-op changes preserve redo");
historyTest.Commit("edited A", "Edit points");
Check(!historyTest.CanRedo, "A new edit replaces the redo branch");
historyTest.Commit("edited again", "Edit points");
Check(historyTest.UndoEntries.Count == 2, "History honors its action limit");
var keybindTest = new EditorPreferences();
Check(EditorPreferences.Actions.Count == 10 && keybindTest.Keybinds.Count == 0, "All ten recording/interaction shortcuts start unassigned");
var keybindChord = new KeyChord(75, 7, "K");
keybindTest.Keybinds["marker"] = keybindChord;
Check(keybindChord.Display == "Ctrl + Shift + Alt + K" && keybindTest.Conflict("trail", keybindChord) != null, "Modifiers are retained and duplicate bindings rejected");
Check(EditorPreferences.Reserved(new(90, 2, "Z")) && EditorPreferences.Reserved(new(90, 6, "Z")), "Fixed undo/redo shortcuts cannot be reassigned");
var keybindFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".json");
try
{
    System.IO.File.WriteAllText(keybindFile, "{\"showSelectedMarkerNames\":true}");
    Check(EditorPreferences.Load(keybindFile).ShowMarkerNames, "Old overlay preferences migrate");
    keybindTest.Save(keybindFile);
    var keybindLoaded = EditorPreferences.Load(keybindFile);
    Check(keybindLoaded.Keybinds["marker"] == keybindChord, "Bindings persist independently of projects");
}
finally
{
    System.IO.File.Delete(keybindFile);
}
var searchTestNodes = new[] { new SearchNode("pack", null, "Routes"), new SearchNode("category", "pack", "Portals"), new SearchNode("marker", "category", "Blue waypoint MapID 15"), new SearchNode("other", "pack", "Zipline") };
Check(ExplorerSearch.Find(searchTestNodes, "BLUE 15").SetEquals(new[] { "pack", "category", "marker" }), "Search preserves ancestors and matches every token without case sensitivity");
Check(ExplorerSearch.Find(searchTestNodes, "missing").Count == 0, "Search returns no unrelated results");
Check(ExplorerSearch.Find(new[] { new SearchNode("a", "b", "match"), new SearchNode("b", "a", "parent") }, "match").Count == 2, "Search handles cyclic parent data without looping");
Check(VisualAttributeMath.Color("#12Ab34") == 0xff12ab34 && VisualAttributeMath.Color("8012AB34") == 0x8012ab34, "Preview colors preserve RGB tint and optional ARGB alpha");
Check(VisualAttributeMath.Color("#0f0") == 0xff00ff00 && VisualAttributeMath.Color("invalid") == 0xffffffff, "Short colors expand and incomplete color input retains a safe preview");
Check(VisualAttributeMath.Enabled("TRUE") && !VisualAttributeMath.Enabled("False"), "Visibility handles case-insensitive XML booleans");
Check(VisualAttributeMath.Rotation("", "", "", "") == null && VisualAttributeMath.Rotation("10,20,30", "", "45", "") == new System.Numerics.Vector3(10, 45, 30), "Absent rotation keeps billboarding; explicit axis overrides the combined rotation");
Check(System.Numerics.Vector3.Distance(VisualAttributeMath.RotateMarker(System.Numerics.Vector3.UnitX, new(0, 0, 90)), -System.Numerics.Vector3.UnitZ) < .0001f, "Marker rotation maps its local plane into Mumble Y-up coordinates");
Check(VisualAttributeMath.AnimationOffset(5.5, 0) == 0 && Math.Abs(VisualAttributeMath.AnimationOffset(5.5, 1) - .5f) < .0001f && Math.Abs(VisualAttributeMath.AnimationOffset(5.5, -1) + .5f) < .0001f, "Trail animation supports stopped, forward and reverse texture phase");
Check(VisualAttributeMath.StickyInRange(System.Numerics.Vector3.Zero, new(0, 0, 8), 350) && !VisualAttributeMath.StickyInRange(System.Numerics.Vector3.Zero, new(0, 0, 10), 350), "Sticky distance uses player distance in game inches");
var stickyPreviewRect = VisualAttributeMath.StickyRect(1920, 1080, 200, 100, 100, 50, 1);
Check(stickyPreviewRect == new PreviewRect(960, 440, 200, 100), "Sticky uses center-origin pixel offsets, positive Y upward and source pixel size");
var fittedSticky = VisualAttributeMath.StickyRect(800, 600, 1600, 800, 900, 900, 1);
Check(fittedSticky.Width == 776 && fittedSticky.Height == 388 && fittedSticky.X == 12 && fittedSticky.Y == 12, "Oversize Sticky previews fit proportionally and clamp inside the client");
Check(gpuVertices[0].Center == gpuSegment.Points[0].Position && gpuVertices[2].Center == new System.Numerics.Vector3(1, 2, 24), "Ribbon vertices retain centerline positions for constant-width textured maps");
Check(MapRibbonProjection.TryBasis(mapFrame, 1920, 1080, out var mapRibbonTestBasis), "Map ribbon projection builds from compass telemetry");
var mapRibbonTestPosition = mapFrame.AvatarPosition + new System.Numerics.Vector3(12, 3, 45);
Check(MapProjection.TryProject(mapFrame, mapRibbonTestPosition, mapRect, 1920, 1080, out var mapRibbonExpected) && System.Numerics.Vector2.Distance(mapRibbonTestBasis.Project(mapRibbonTestPosition), mapRibbonExpected) < .02f, "GPU/map fallback affine basis agrees with point projection");
var mapRibbonSideA = mapRibbonTestBasis.ProjectSide(new(1, 2, 2.5f), new(1, 2, 3), 3);
var mapRibbonSideB = mapRibbonTestBasis.ProjectSide(new(1, 2, 3.5f), new(1, 2, 3), 3);
Check(Math.Abs(System.Numerics.Vector2.Distance(mapRibbonSideA, mapRibbonSideB) - 6) < .001f, "Map ribbon width stays six pixels independent of map zoom");
Check(MapRibbonProjection.UvScale(mapRibbonTestBasis, 6, 2) == MapRibbonProjection.UvScale(mapRibbonTestBasis, 6, 1), "Map UV conversion uses the resolved width exactly once");
// Handoff fixtures use production formulas, independent of image dimensions.
Check(ScaleNormalization.WorldHalf(900, 30, 1, 1, 5, 2048) == 30 && ScaleNormalization.WorldHalf(900, 30, 1, 2, 5, 2048) == 60, "World icon projection returns half extents without an extra half factor");
Check(ScaleNormalization.WorldHalf(900, 0, 1, 1, 5, 2048) == 0 && ScaleNormalization.WorldHalf(900, float.NaN, 1, 1, 5, 2048) == 0, "Invalid marker depth never produces geometry");
Check(ScaleNormalization.WorldHalf(900, 30, 1, 3, 5, 40) == 40 && ScaleNormalization.WorldHalf(900, 30, 0, 1, 5, 40) == 5, "Icon user scale is applied before physical-pixel clamps");
Check(ScaleNormalization.WorldWidth(1, 1) == 1.016f && ScaleNormalization.WorldWidth(2, 1) == 2.032f && ScaleNormalization.WorldWidth(-1, 1) == 0 && ScaleNormalization.WorldRepeatMeters == 1.016f, "World ribbon width and fixed repeat have distinct contracts");
Check(ScaleNormalization.MapIconHalf(20, 1, true, 1, 1) == 10 && ScaleNormalization.MapIconHalf(20, 2, true, 1, 1) == 5 && ScaleNormalization.MapIconHalf(20, 2, false, 1, 1) == 10, "Both map icon contexts use compass zoom only when enabled");
Check(ScaleNormalization.MapTrailWidth(20, 1, true, 1, 1, 1) == 20 && ScaleNormalization.MapTrailWidth(20, 2, true, 1, 1, 1) == 10 && ScaleNormalization.MapTrailWidth(20, 2, false, 1, 1, 1) == 20, "Textured map width and repeat match the supplied baseline");
Check(ScaleNormalization.MapTrailWidth(1000, 1, true, 1, 3, 4) == 100 && ScaleNormalization.MapTrailWidth(20, 1, true, 1, 1, 0) == 1 && ScaleNormalization.MapIconHalf(20, 0, true, 1, 1) == 0, "Map sizing clamps final geometry and rejects invalid compass scale");
Check(ScaleNormalization.SolidWidth(1, 1) == 4 && ScaleNormalization.SolidWidth(1, 2) == 8, "Solid map width excludes zoom and XML trail dimensions");
Check(ScaleNormalization.AverageColor(new byte[] { 0, 0, 255, 255, 255, 0, 0, 0 }) == 0xffff0000 && ScaleNormalization.AverageColor(new byte[] { 255, 255, 255, 0 }) == 0, "Texture sampling ignores transparent RGB and preserves fully transparent artwork");
Check(ScaleNormalization.AverageColor(new byte[] { 0, 0, 1, 1, 0, 0, 0, 1 }) == 0xff010000, "Sample color rounds midpoint channels upward");
Check(ScaleNormalization.SolidColor(0xffffffff, 0xff123456) == 0xff123456 && ScaleNormalization.SolidColor(0x80112233, 0xffabcdef) == 0x80112233, "Non-default XML color replaces rather than multiplies sampled color");
Check(System.Runtime.InteropServices.Marshal.OffsetOf<TrailGpuVertex>(nameof(TrailGpuVertex.MapDistance)).ToInt32() == 32, "Projected-map distance matches the shader input offset");
var slopeSegment = new TrailSegment { Id = 900, Points = [new() { Id = 1, Position = new(0, 0, 0) }, new() { Id = 2, Position = new(3, 12, 4) }] };
var slopeMesh = GpuTrailGeometry.Build(gpuRoute, slopeSegment);
Check(Math.Abs(slopeMesh[^1].Uv.Y + 13) < .001f && Math.Abs(slopeMesh[^1].MapDistance + 5) < .001f, "Sloped trails retain 3D world distance and planar map distance separately");
var slopeAppend = GpuTrailGeometry.Build(gpuRoute, slopeSegment, firstEdge: 1, initialLength: 13, initialMapLength: 5);
Check(slopeAppend[0].Uv.Y == -13 && slopeAppend[0].MapDistance == -5, "Appending geometry preserves both distance prefixes");
var visualPrefs = new EditorPreferences();
Check(visualPrefs.ShowMarkerNames && visualPrefs.OnlyShowForSelected && visualPrefs.TrailOverlaySelection == TrailSelectionMode.Trail, "Fresh preferences show selected marker names and select complete trails");
Check(visualPrefs.MarkerGameScale == 1 && visualPrefs.MarkerMapScale == 1 && visualPrefs.MarkerMinimapScale == 1 && visualPrefs.TrailGameWidth == 1 && visualPrefs.TrailMapWidth == 1 && visualPrefs.TrailMinimapWidth == 1, "All six preview multipliers default to one");
visualPrefs.GameExecutablePath = @"E:\Guild Wars 2\Gw2-64.exe";
visualPrefs.MarkerMapScale = 2;
visualPrefs.TrailMinimapWidth = 3;
visualPrefs.ShowMarkerNames = false;
visualPrefs.OnlyShowForSelected = false;
visualPrefs.MarkerNameDistanceThreshold = 125;
visualPrefs.TrailOverlaySelection = TrailSelectionMode.NearestPoint;
visualPrefs.Keybinds["marker"] = keybindChord;
var visualCopy = visualPrefs.Copy();
visualCopy.ResetDefaults();
Check(visualCopy.GameExecutablePath == visualPrefs.GameExecutablePath, "Visual/keybind Defaults retain the configured game installation");
visualCopy.GameExecutablePath = "";
Check(visualPrefs.GameExecutablePath.Length > 0, "Cancelling preference edits preserves the current executable path");
Check(visualCopy.MarkerMapScale == 1 && visualCopy.TrailMinimapWidth == 1 && visualCopy.ShowMarkerNames && visualCopy.OnlyShowForSelected && visualCopy.MarkerNameDistanceThreshold == EditorPreferences.DefaultMarkerNameDistance && visualCopy.TrailOverlaySelection == TrailSelectionMode.Trail && visualCopy.Keybinds.Count == 0 && visualPrefs.MarkerMapScale == 2 && visualPrefs.Keybinds.Count == 1, "Defaults in a preferences copy do not mutate current settings before Save");
Check(!visualPrefs.ShowMarkerNames && !visualPrefs.OnlyShowForSelected && visualPrefs.MarkerNameDistanceThreshold == 125 && visualPrefs.TrailOverlaySelection == TrailSelectionMode.NearestPoint, "Canceling copied visual settings preserves the saved name and selection choices");
var visualPrefsFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".json");
try
{
    visualPrefs.MapTrailStyle = MapTrailVisualStyle.BlishHud;
    visualPrefs.Save(visualPrefsFile);
    var roundtripVisuals = EditorPreferences.Load(visualPrefsFile);
    Check(roundtripVisuals.MapTrailStyle == MapTrailVisualStyle.BlishHud && roundtripVisuals.ScaleBaselineV1 == 1, "Trail style persists with the current scale baseline");
    Check(!roundtripVisuals.ShowMarkerNames && !roundtripVisuals.OnlyShowForSelected && roundtripVisuals.MarkerNameDistanceThreshold == 125 && roundtripVisuals.TrailOverlaySelection == TrailSelectionMode.NearestPoint, "Marker name settings and trail selection mode persist independently");
    Check(roundtripVisuals.GameExecutablePath == visualPrefs.GameExecutablePath, "Game executable path persists across launches, including spaces");
    Check(roundtripVisuals.MarkerMapScale == 2 && roundtripVisuals.TrailMinimapWidth == 3 && roundtripVisuals.MarkerMinimapScale == 1, "Independent visual multipliers persist across launches");
    System.IO.File.WriteAllText(visualPrefsFile, "{\"MarkerGameScale\":2,\"TrailMapWidth\":3,\"ShowMarkerInformation\":false,\"OnlyShowForSelected\":false,\"Keybinds\":{\"interact\":{\"VirtualKey\":75,\"Modifiers\":4,\"KeyName\":\"K\"}}}");
    var baselineMigration = EditorPreferences.Load(visualPrefsFile);
    Check(baselineMigration.MarkerGameScale == 1 && baselineMigration.TrailMapWidth == 1 && !baselineMigration.ShowMarkerInformation && !baselineMigration.OnlyShowForSelected && baselineMigration.Keybinds.ContainsKey("interact"), "One-time scale reset preserves interaction settings and keybinds");
    baselineMigration.MarkerGameScale = 2;
    baselineMigration.Save(visualPrefsFile);
    Check(EditorPreferences.Load(visualPrefsFile).MarkerGameScale == 2, "Later scale adjustments survive subsequent launches");
    Check(System.IO.File.ReadAllText(visualPrefsFile).Contains("ScaleBaselineV1"), "Migration writes its completion marker");
    System.IO.File.WriteAllText(visualPrefsFile, "{\"showSelectedMarkerNames\":true}");
    var migratedVisuals = EditorPreferences.Load(visualPrefsFile);
    Check(migratedVisuals.GameExecutablePath == "", "Older preferences migrate without a configured game executable");
    Check(migratedVisuals.ShowMarkerNames && migratedVisuals.TrailGameWidth == 1 && migratedVisuals.MarkerMapScale == 1, "Older preferences retain marker names and receive default multipliers");
    System.IO.File.WriteAllText(visualPrefsFile, "{\"showSelectedMarkerNames\":false}");
    var oldNamesOff = EditorPreferences.Load(visualPrefsFile);
    Check(!oldNamesOff.ShowMarkerNames && oldNamesOff.OnlyShowForSelected, "Existing name opt-outs survive migration while selected-only remains the default");
    System.IO.File.WriteAllText(visualPrefsFile, "{\"showSelectedMarkerNames\":false,\"ShowMarkerNames\":true,\"OnlyShowForSelected\":false}");
    var newNameSettings = EditorPreferences.Load(visualPrefsFile);
    Check(newNameSettings.ShowMarkerNames && !newNameSettings.OnlyShowForSelected, "New preference fields take priority over a legacy checkbox");
}
finally
{
    System.IO.File.Delete(visualPrefsFile);
}
Check(EditorPreferences.ValidMultiplier(float.NaN) == 1 && EditorPreferences.ValidMultiplier(0) == .25f && EditorPreferences.ValidMultiplier(10) == 3, "Malformed or out-of-range multipliers cannot collapse preview geometry");
Check(MapRibbonProjection.Width(20, 1) == 20, "Neutral map/minimap textured trail width is twenty physical pixels");
var importRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TacoImportChecks_" + Guid.NewGuid().ToString("N"));
System.IO.Directory.CreateDirectory(importRoot);
try
{
    string inputPack = System.IO.Path.Combine(importRoot, "original.taco");
    var importedRoute = new EditableTrail { Number = 1, Guid = Convert.ToBase64String(Guid.NewGuid().ToByteArray()), MapId = 15 };
    importedRoute.AddSegment().Add(new(10, 2, 10));
    importedRoute.Segments[0].Add(new(20, 2, 20));
    importedRoute.AddSegment().Add(new(30, 2, 30));
    importedRoute.Segments[1].Add(new(40, 2, 40));
    byte[] importTrl = EditableTrails.Compile(importedRoute);
    string importXml = $"<OverlayData><MarkerCategory name=\"pack\" DisplayName=\"Imported Heading\"/><POIs><Trail GUID=\"{importedRoute.Guid}\" MapID=\"15\" type=\"pack\" trailData=\"routes/original.trl\" texture=\"images/route.png\" customValue=\"keep\"/></POIs></OverlayData>";
    using (var zip = System.IO.Compression.ZipFile.Open(inputPack, System.IO.Compression.ZipArchiveMode.Create))
    {
        void Entry(string path, byte[] bytes)
        {
            using var stream = zip.CreateEntry(path).Open();
            stream.Write(bytes);
        }
        Entry("xml/routes.xml", System.Text.Encoding.UTF8.GetBytes(importXml));
        Entry("routes/original.trl", importTrl);
        Entry("images/route.png", new byte[] { 1, 2, 3 });
        Entry("scripts/example.lua", System.Text.Encoding.UTF8.GetBytes("return 1"));
        Entry("metadata/config.xml", System.Text.Encoding.UTF8.GetBytes("<Config version=\"1\"/>"));
    }
    byte[] originalPackBytes = System.IO.File.ReadAllBytes(inputPack);
    Check(TacoImporter.SuggestedName(inputPack) == "Imported Heading", "Import name is suggested from the main heading");
    var convertedImport = TacoImporter.Import(inputPack, "Editable Import", importRoot, "", true);
    var reopenedImport = ProjectStore.Load(convertedImport.ProjectFile);
    Check(reopenedImport.Trails!.Count == 1 && reopenedImport.Trails[0].Segments.Count == 2 && EditableTrails.Compile(reopenedImport.Trails[0]).SequenceEqual(importTrl), "TRL conversion preserves positions and segment breaks through project reopen");
    Check(reopenedImport.Documents[0].Name == "xml/routes.xml" && reopenedImport.Documents[0].Content.Contains("customValue=\"keep\""), "Nested XML paths and unknown attributes survive import");
    Check(reopenedImport.CodeDocuments!.Single().Name == "scripts/example.lua", "Lua is tracked separately from OverlayData XML");
    var editedImport = reopenedImport with { CodeDocuments = [new ProjectCode("scripts/example.lua", "return 2")] };
    ProjectStore.Save(convertedImport.ProjectFile, editedImport);
    string rebuiltImport = System.IO.Path.Combine(importRoot, "rebuilt.taco");
    TestPackBuilder.Build(rebuiltImport, editedImport.Documents.Select(d => (d.Name, d.Content)).ToList(), System.IO.Path.GetDirectoryName(convertedImport.ProjectFile), editedImport.Trails, editedImport.ImportedAssets);
    using (var zip = System.IO.Compression.ZipFile.OpenRead(rebuiltImport))
    {
        using var trl = zip.GetEntry("routes/original.trl")!.Open();
        using var trlBytes = new System.IO.MemoryStream();
        trl.CopyTo(trlBytes);
        Check(trlBytes.ToArray().SequenceEqual(importTrl), "Converted trails compile at their original XML-linked path");
        using var lua = new System.IO.StreamReader(zip.GetEntry("scripts/example.lua")!.Open());
        Check(lua.ReadToEnd() == "return 2" && zip.GetEntry("metadata/config.xml") != null, "Lua edits and non-OverlayData assets survive repackaging");
    }
    var binaryImport = TacoImporter.Import(inputPack, "Binary Import", importRoot, "", false);
    Check(binaryImport.Snapshot.Trails!.Count == 0 && System.IO.File.ReadAllBytes(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(binaryImport.ProjectFile)!, "routes/original.trl")).SequenceEqual(importTrl), "Unchecked conversion retains the exact original TRL bytes");
    Check(System.IO.File.ReadAllBytes(inputPack).SequenceEqual(originalPackBytes), "Import leaves the original archive untouched");
    string partialPack = System.IO.Path.Combine(importRoot, "partial.taco");
    using (var inputZip = System.IO.Compression.ZipFile.OpenRead(inputPack))
        using (var outputZip = System.IO.Compression.ZipFile.Open(partialPack, System.IO.Compression.ZipArchiveMode.Create))
        {
            foreach (var item in inputZip.Entries)
            {
                using var from = item.Open();
                using var to = outputZip.CreateEntry(item.FullName).Open();
                from.CopyTo(to);
            }
            string partialXml = "<OverlayData><POIs><Trail GUID=\"missing\" MapID=\"15\" type=\"pack\" trailData=\"routes/missing.trl\"/><Trail GUID=\"corrupt\" MapID=\"15\" type=\"pack\" trailData=\"routes/corrupt.trl\"/><Trail GUID=\"mismatch\" MapID=\"999\" type=\"pack\" trailData=\"routes/original.trl\"/><Trail GUID=\"duplicate\" MapID=\"15\" type=\"pack\" trailData=\"routes/original.trl\"/><Trail GUID=\"duplicate\" MapID=\"15\" type=\"pack\" trailData=\"routes/original.trl\"/></POIs></OverlayData>";
            using (var xmlStream = outputZip.CreateEntry("partial.xml").Open()) xmlStream.Write(System.Text.Encoding.UTF8.GetBytes(partialXml));
            using (var badStream = outputZip.CreateEntry("routes/corrupt.trl").Open()) badStream.Write(new byte[9]);
        }
    var partialImport = TacoImporter.Import(partialPack, "Partial Import", importRoot, "", true);
    Check(partialImport.ConvertedTrails == 1 && partialImport.SkippedTrails.Count == 5, "Bad references do not prevent valid trails from converting; all duplicate GUID references stay binary");
    string partialFolder = System.IO.Path.GetDirectoryName(partialImport.ProjectFile)!;
    Check(System.IO.File.ReadAllBytes(System.IO.Path.Combine(partialFolder, "routes/corrupt.trl")).SequenceEqual(new byte[9]) && System.IO.File.ReadAllBytes(System.IO.Path.Combine(partialFolder, "routes/original.trl")).SequenceEqual(importTrl), "Unconverted TRLs are imported with their original bytes and paths");
    var partialTags = XDocument.Parse(partialImport.Snapshot.Documents.Single(d => d.Name == "partial.xml").Content).Descendants("Trail").ToList();
    Check(partialTags[0].Attribute("trailData")!.Value == "routes/missing.trl" && partialTags[2].Attribute("MapID")!.Value == "999" && !System.IO.File.Exists(System.IO.Path.Combine(partialFolder, "routes/missing.trl")), "Skipped XML references remain intact; missing files are not fabricated during import");
    var convertedTag = XDocument.Parse(partialImport.Snapshot.Documents.Single(d => d.Name == "xml/routes.xml").Content).Descendants("Trail").Single();
    Check(convertedTag.Attribute("trailData")!.Value != "routes/original.trl", "Editable shared sources get independent output paths so binary references remain unchanged");
    Check(ProjectStore.Load(partialImport.ProjectFile).Trails!.Count == 1 && System.IO.File.Exists(System.IO.Path.Combine(partialFolder, "TacoStuffer_Import_Report.txt")), "Partial imports reopen normally and retain a readable conversion report");
    bool invalidTrl = false;
    try
    {
        TacoImporter.ReadTrail(new byte[9], 1, importedRoute.Guid);
    }
    catch (InvalidOperationException)
    {
        invalidTrl = true;
    }
    Check(invalidTrl, "Malformed TRL conversion is rejected");
    bool traversal = false;
    try
    {
        TacoImporter.PackPath("../outside.txt");
    }
    catch (InvalidOperationException)
    {
        traversal = true;
    }
    Check(traversal && !ProjectStore.ValidPackPath("C:/outside.txt"), "Import rejects unsafe relative paths");
}
finally
{
    System.IO.Directory.Delete(importRoot, true);
}
var pathDefaults = new ProjectAssetPaths();
Check(pathDefaults.MarkerIcons == "TEXTURES" && pathDefaults.TrailTextures == "TEXTURES" && pathDefaults.TrailFiles == "DATA", "New projects use the original asset folder defaults");
var inferredPaths = ProjectAssetPaths.Infer([XDocument.Parse("<OverlayData><MarkerCategory name=\"pack\" iconFile=\"pack/icons/a.png\" texture=\"pack/textures/b.png\"/><POIs><Trail trailData=\"pack/routes/a.trl\"/></POIs></OverlayData>")]);
Check(inferredPaths == new ProjectAssetPaths("pack/icons", "pack/textures", "pack/routes"), "Imported XML suggests separate asset folders without rewriting its paths");
Check(ProjectAssetPaths.FilePath(".", "trail_0001.trl") == "trail_0001.trl", "Project-root defaults produce valid pack paths");
var pathsTestRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AssetPaths_" + Guid.NewGuid().ToString("N"));
System.IO.Directory.CreateDirectory(pathsTestRoot);
try
{
    var customPaths = new ProjectAssetPaths("icons", "textures", "routes");
    var pathsSnapshot = new ProjectSnapshot("Paths", "", [new ProjectXml("markers.xml", "<OverlayData><MarkerCategory name=\"pack\"/><POIs><POI type=\"pack\" MapID=\"15\" iconFile=\"other/special.png\"/></POIs></OverlayData>")], new Dictionary<string, string>(), AssetPaths: customPaths);
    string pathsProject = ProjectStore.Create(pathsSnapshot, pathsTestRoot), pathsFolder = System.IO.Path.GetDirectoryName(pathsProject)!;
    System.IO.Directory.CreateDirectory(System.IO.Path.Combine(pathsFolder, "icons/sub"));
    System.IO.Directory.CreateDirectory(System.IO.Path.Combine(pathsFolder, "other"));
    System.IO.File.WriteAllBytes(System.IO.Path.Combine(pathsFolder, "icons/sub/icon.png"), new byte[] { 1 });
    System.IO.File.WriteAllBytes(System.IO.Path.Combine(pathsFolder, "other/special.png"), new byte[] { 2 });
    Check(ProjectStore.Load(pathsProject).AssetPaths == customPaths && !System.IO.Directory.Exists(System.IO.Path.Combine(pathsFolder, "TEXTURES")), "Folder settings persist and custom projects create their configured asset folders");
    Check(ProjectAssetPaths.Files(pathsFolder, "icons").Count() == 1, "Asset pickers recursively include configured subdirectories");
    string pathsExport = System.IO.Path.Combine(pathsTestRoot, "paths.taco");
    TestPackBuilder.Build(pathsExport, pathsSnapshot.Documents.Select(d => (d.Name, d.Content)).ToList(), pathsFolder, assetPaths: customPaths);
    using (var zip = System.IO.Compression.ZipFile.OpenRead(pathsExport))
        Check(zip.GetEntry("icons/sub/icon.png") != null && zip.GetEntry("other/special.png") != null && zip.GetEntry("Paths.cilantro") == null, "Build includes configured assets and explicit XML paths outside picker roots");
    System.IO.Directory.CreateDirectory(System.IO.Path.Combine(pathsFolder, "EXPORT"));
    System.IO.File.WriteAllText(System.IO.Path.Combine(pathsFolder, "EXPORT/skip.png"), "skip");
    Check(!ProjectAssetPaths.Files(pathsFolder, ".").Any(p => p.Contains("EXPORT")), "Root-folder scans exclude project export storage");
    bool invalidFolder = false;
    try
    {
        ProjectAssetPaths.Folder("../outside");
    }
    catch (InvalidOperationException)
    {
        invalidFolder = true;
    }
    Check(invalidFolder, "Asset folders cannot leave the project");
}
finally
{
    System.IO.Directory.Delete(pathsTestRoot, true);
}
var manyDocsRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TabImport_" + Guid.NewGuid().ToString("N"));
System.IO.Directory.CreateDirectory(manyDocsRoot);
try
{
    string pack = System.IO.Path.Combine(manyDocsRoot, "many.taco");
    using (var zip = System.IO.Compression.ZipFile.Open(pack, System.IO.Compression.ZipArchiveMode.Create))
    {
        for (int i = 0; i < 12; i++)
        {
            using var stream = zip.CreateEntry($"doc_{i:00}.xml").Open();
            stream.Write(System.Text.Encoding.UTF8.GetBytes($"<OverlayData><MarkerCategory name=\"cat_{i}\"/><POIs/></OverlayData>"));
        }
    }
    var result = TacoImporter.Import(pack, "Many Documents", manyDocsRoot, "", false);
    Check(result.Snapshot.Documents.Count == 12 && result.Snapshot.Documents.Count(d => d.Open) == 5 && result.ConvertedTrails == 0, "Large imports retain all XML while requesting only five open views and no bulk trail conversion");
    Check(ProjectStore.Load(result.ProjectFile).Documents.Count(d => d.Open) == 5, "The bounded initial document views survive project reopen");
}
finally
{
    System.IO.Directory.Delete(manyDocsRoot, true);
}


// Large-pack indexing must preserve positional IDs when source tags omit GUIDs.
var identityParent = new XElement("POIs", new XElement("POI"), new XElement("Trail"), new XElement("Trail"));
var identityTrail = identityParent.Elements().Last();
Check(ElementIdentity.Key(identityTrail) == "Trail:index:2", "Indexed fallback ID retains all preceding element types");
identityParent.Elements().First().Remove();
Check(ElementIdentity.Key(identityTrail) == "Trail:index:1", "Deleting a sibling invalidates fallback ordinals");
identityParent.AddFirst(new XElement("POI"));
Check(ElementIdentity.Key(identityTrail) == "Trail:index:2", "Adding a sibling invalidates fallback ordinals");
identityTrail.SetAttributeValue("GUID", "unique");
Check(ElementIdentity.Key(identityTrail) == "Trail:unique", "GUID takes precedence over positional ID");
identityTrail.SetAttributeValue("GUID", null);
Check(ElementIdentity.Key(identityTrail) == "Trail:index:2", "Removing a GUID restores positional ID");
var sourceLookup = XmlSourceIndex.Build(xml);
Check(sourceLookup(xml.IndexOf("xpos", StringComparison.Ordinal))?.Name == "POI", "Reusable source index selects multi-line POI");
Check(sourceLookup(xml.IndexOf("</MarkerCategory>", StringComparison.Ordinal))?.Name == "MarkerCategory", "Reusable source index selects category closing tag");
var inPlaceOrder = Doc("<MarkerCategory name=\"b\"/><MarkerCategory name=\"a\"/>");
var inPlacePoi = new XElement("POI", new XAttribute("type", "pack.a"));
inPlaceOrder.Root!.Element("POIs")!.Add(inPlacePoi);
var inPlaceResult = CategoryOrder.Normalize([inPlaceOrder], ["pack", "pack.a", "pack.b"], clone: false);
Check(ReferenceEquals(inPlaceOrder, inPlaceResult[0]) && ReferenceEquals(inPlacePoi, inPlaceResult[0].Descendants("POI").Single()), "In-place normalization preserves document and POI identity");
Check((string?)inPlaceResult[0].Descendants("MarkerCategory").Skip(1).First().Attribute("name") == "a", "In-place normalization still orders categories");



var hiddenPreview = new HashSet<string> { "pack.portals" };
Check(!PreviewCategories.Visible("pack.portals.city", hiddenPreview) && PreviewCategories.Visible("pack.portal", hiddenPreview), "Preview hide affects complete category paths, not similarly named siblings");
hiddenPreview.Add("pack.portals.city");
hiddenPreview.Remove("pack.portals");
Check(PreviewCategories.Visible("pack.portals", hiddenPreview) && !PreviewCategories.Visible("pack.portals.city", hiddenPreview), "Re-enabling a parent preserves independently hidden child state");
hiddenPreview.Clear();
Check(PreviewCategories.Visible("pack.portals.city", hiddenPreview), "Restoring preview visibility does not retain a stale hidden parent");
string malformedProjectFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TacoStufferMalformed_" + Guid.NewGuid().ToString("N"));
System.IO.Directory.CreateDirectory(malformedProjectFolder);
try
{
    string malformedProject = System.IO.Path.Combine(malformedProjectFolder, "Broken.cilantro");
    System.IO.File.WriteAllText(malformedProject, "<CilantroProject version=\"1\" name=\"Broken\"/>");
    bool rejected = false;
    try
    {
        ProjectStore.Load(malformedProject);
    }
    catch (InvalidOperationException ex)
    {
        rejected = ex.Message == "Project document list is missing.";
    }
    Check(rejected, "A missing project document list produces a recoverable load error");
}
finally
{
    System.IO.Directory.Delete(malformedProjectFolder, true);
}
var embeddedCatalog = new AttributeCatalog();
embeddedCatalog.Reload(null);
int embeddedDefinitionCount = 17;
Check(embeddedCatalog.Errors.Count == 0 && embeddedCatalog.Groups.Count == embeddedDefinitionCount - 2, "All compiled built-ins load without files or manifest resources");
Check(embeddedCatalog.Groups.Single(g => g.Key == "iconFile").Members.Any(m => m.Name == "iconSize")
    && embeddedCatalog.Groups.Single(g => g.Key == "texture").Members.Any(m => m.Name == "trailScale"), "Compiled definitions preserve Icon and Texture attribute bundles");
string customCatalogFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TacoStufferCatalog_" + Guid.NewGuid().ToString("N"));
System.IO.Directory.CreateDirectory(System.IO.Path.Combine(customCatalogFolder, "CUSTOM_ATTRIBUTES"));
try
{
    System.IO.File.WriteAllText(System.IO.Path.Combine(customCatalogFolder, "CUSTOM_ATTRIBUTES", "preview.xml"), "<AttributeGroup version=\"1\" key=\"betaPreview\" displayName=\"Beta Preview\" appliesTo=\"POI\"><Attribute name=\"betaPreviewValue\" default=\"7\" type=\"number\"/></AttributeGroup>");
    embeddedCatalog.Reload(customCatalogFolder);
    Check(embeddedCatalog.Errors.Count == 0 && embeddedCatalog.Groups.Single(g => g.Key == "betaPreview").IsCustom, "Project custom attribute files still load alongside embedded built-ins");
    embeddedCatalog.Reload(null);
    Check(embeddedCatalog.Groups.All(g => !g.IsCustom) && embeddedCatalog.Groups.Count == embeddedDefinitionCount - 2, "Reloading clears project custom definitions without duplicating built-ins");
}
finally
{
    System.IO.Directory.Delete(customCatalogFolder, true);
}
var nameVisibilityPrefs = new EditorPreferences { MarkerNameDistanceThreshold = 100 };
Check(nameVisibilityPrefs.ShouldShowMarkerName(true, System.Numerics.Vector3.Zero, new(0, 0, 100))
    && !nameVisibilityPrefs.ShouldShowMarkerName(false, System.Numerics.Vector3.Zero, new(0, 0, 10))
    && !nameVisibilityPrefs.ShouldShowMarkerName(true, System.Numerics.Vector3.Zero, new(0, 0, 101)), "Selected-only names obey the inclusive distance threshold in meters");
nameVisibilityPrefs.OnlyShowForSelected = false;
Check(nameVisibilityPrefs.ShouldShowMarkerName(false, new(0, 0, 10), new(0, 0, 105)), "All-name mode uses player distance rather than distance from the world origin");
nameVisibilityPrefs.ShowMarkerNames = false;
Check(!nameVisibilityPrefs.ShouldShowMarkerName(true, System.Numerics.Vector3.Zero, new(0, 0, 1)), "Disabling names overrides selection and distance");
nameVisibilityPrefs.ShowMarkerNames = true;
Check(!nameVisibilityPrefs.ShouldShowMarkerName(true, System.Numerics.Vector3.Zero, new(float.NaN, 0, 0)), "Non-finite marker positions never produce name labels");
nameVisibilityPrefs.MarkerNameDistanceThreshold = -1;
nameVisibilityPrefs.TrailOverlaySelection = (TrailSelectionMode)99;
nameVisibilityPrefs.NormalizeVisuals();
Check(nameVisibilityPrefs.MarkerNameDistanceThreshold == 0 && nameVisibilityPrefs.TrailOverlaySelection == TrailSelectionMode.Trail
    && EditorPreferences.ValidNameDistance(float.NaN) == EditorPreferences.DefaultMarkerNameDistance, "Malformed visual preferences normalize to usable values");

var trailPickNear = new TrailPickTriangle(new(500, 7), new(0, 0), new(10, 0), new(0, 10), 2, 2, 2);
var trailPickFar = trailPickNear with { Segment = new(501, 8), DepthA = 10, DepthB = 10, DepthC = 10 };
Check(TrailPicking.Hit([trailPickNear, trailPickFar], new(2, 2)) == trailPickNear.Segment
    && TrailPicking.Hit([trailPickFar, trailPickNear], new(2, 2)) == trailPickNear.Segment, "Crossing ribbons select the nearest depth independently of draw order");
Check(TrailPicking.Hit([trailPickNear], new(9, 9)) == null && TrailPicking.Hit([trailPickNear], new(20, 20)) == null, "Ribbon picking rejects empty areas within bounding boxes and gaps outside the ribbon");
Check(TrailPicking.Hit([trailPickNear with { B = trailPickNear.C, C = trailPickNear.B }], new(2, 2)) == trailPickNear.Segment, "Ribbon picking accepts either triangle winding");
Check(TrailPicking.Hit([trailPickNear with { C = trailPickNear.B }, trailPickNear with { DepthA = -1 }], new(2, 2)) == null, "Degenerate and behind-camera ribbon triangles cannot be selected");
Check(TrailPicking.TryDepth(trailPickNear with { DepthA = 2, DepthB = 4, DepthC = 8 }, new(2.5f, 2.5f), out float trailPickDepth)
    && Math.Abs(trailPickDepth - 1 / (.5f / 2 + .25f / 4 + .25f / 8)) < .0001f, "Ribbon overlap uses perspective-correct interpolated depth");
Check(TrailPicking.NearestPoint([(100L, new(1, 0)), (7L, new(9, 0)), (42L, new(20, 0))], new(8.5f, 0)) == 7
    && TrailPicking.NearestPoint([], new(8.5f, 0)) == null, "Nearest-point selection uses screen distance and stable IDs rather than list indices");
var interactionCategories = new Dictionary<string, XElement>
{
    ["route.handoff"] = XElement.Parse("<MarkerCategory name='handoff' INFO='First line&#xA;Second line' inforange='100' triggerRange='6' autoTrigger=' TRUE '/>")
};
var interactionPoi = XElement.Parse("<POI type='route.handoff' MapID='24' xpos='10' ypos='2' zpos='30' copy='[&amp;BMwDAAA=]' copy-message='Next {0}'/>");
var interactionSpec = MarkerInteraction.Read("test|POI:1", interactionPoi, interactionCategories)!;
Check(interactionSpec.Info == "First line\nSecond line" && interactionSpec.InfoRadius == 100 && interactionSpec.CopyRadius == 6 && interactionSpec.AutoTrigger,
    "Interaction parsing inherits mixed-case attributes, exact newlines, independent meter ranges and whitespace-tolerant booleans");
Check(interactionSpec.Copy == "[&BMwDAAA=]" && MarkerInteraction.Confirmation(interactionSpec.Message, interactionSpec.Copy) == "Next [&BMwDAAA=]",
    "XML entities decode exactly once before clipboard formatting");
Check(MarkerInteraction.Confirmation("{{{0}}} {9}", "x{0}") == "{x{0}} {9}", "Confirmation expands escaped braces and inserts a payload once without recursively formatting it");
interactionPoi.SetAttributeValue("info", "");
interactionPoi.SetAttributeValue("copy", "");
Check(MarkerInteraction.Read("empty", interactionPoi, interactionCategories) == null, "Empty POI strings clear inherited interaction text");
interactionPoi.SetAttributeValue("copy", "payload");
interactionCategories["route.handoff"].SetAttributeValue("inforange", "NaN");
Check(MarkerInteraction.Read("fallback", interactionPoi, interactionCategories)!.InfoRadius == 6, "Invalid infoRange falls back to effective triggerRange");
interactionCategories["route.handoff"].SetAttributeValue("triggerRange", "Infinity");
Check(MarkerInteraction.Read("fallback2", interactionPoi, interactionCategories)!.InfoRadius == 2, "Missing/invalid radii fall back to two meters");
Check(MarkerInteraction.TryRadius("-4", out float zeroRange) && zeroRange == 0 && !MarkerInteraction.TryRadius("6m", out _) && !MarkerInteraction.TryRadius("NaN", out _), "Range parsing clamps negative radii and rejects partial and nonfinite numbers");
Check(MarkerInteraction.Read("trail", new XElement("Trail"), interactionCategories) == null, "Trails do not acquire marker Info/Copy behavior");
var interactionState = new MarkerInteractionSession();
var independent = interactionSpec with { Position = System.Numerics.Vector3.Zero };
var betweenRanges = interactionState.Update([independent], 24, new(0, 0, 50), 0, true);
Check(betweenRanges.Info == independent && betweenRanges.Copy == null && betweenRanges.Automatic == null, "Info remains visible between the independent 6m Copy and 100m Info radii");
interactionState.Dismiss(independent.Key);
Check(interactionState.Update([independent], 24, new(0, 0, 50), 100, false).Info == null, "Info X dismissal persists while in range, including map/focus suppression");
interactionState.Update([independent], 24, new(0, 0, 101), 200, false);
Check(interactionState.Update([independent], 24, new(0, 0, 50), 300, false).Info == independent, "Leaving and re-entering re-arms dismissed Info");
Check(interactionState.Update([independent], 24, new(0, 0, 5), 400, true).Automatic == independent, "Auto Copy triggers on an actual range entry");
interactionState.AutomaticAttempted(400);
Check(interactionState.Update([independent], 24, new(0, 0, 5), 3000, true).Automatic == null, "Remaining in range never rewrites the clipboard each frame");
interactionState.Update([independent], 24, new(0, 0, 8), 3100, false);
Check(interactionState.Update([independent], 24, new(0, 0, 5), 3200, false).Automatic == null
    && interactionState.Update([independent], 24, new(0, 0, 5), 3300, true).Automatic == null, "Enabling interaction tests while already in range does not create a fake entry");
interactionState.Reset();
Check(interactionState.Update([independent], 24, new(0, 0, 1), 4000, true).Automatic == null, "Import, model edits and reset baseline never execute Copy");
var closerInfo = independent with { Key = "near-info", Position = new(0, 0, 20), Copy = "", InfoRadius = 100 };
var closerCopy = independent with { Key = "near-copy", Position = new(0, 0, 1), Info = "", AutoTrigger = false };
interactionState.Reset();
var separateNearest = interactionState.Update([closerInfo, closerCopy], 24, System.Numerics.Vector3.Zero, 5000, false);
Check(separateNearest.Info == closerInfo && separateNearest.Copy == closerCopy, "Nearest Info and nearest Copy are resolved independently");
Check(interactionState.Update([], 24, System.Numerics.Vector3.Zero, 5100, false).Info == null, "Deleting a marker clears its HUD reference safely");
interactionState.Reset();
interactionState.Update([independent], 24, new(0, 0, 10), 6000, true);
Check(interactionState.Update([independent], 24, new(0, 0, 1), 6100, true).Automatic == independent, "A fresh range entry schedules one automatic attempt");
interactionState.AutomaticAttempted(6100);
interactionState.Update([independent], 24, new(0, 0, 10), 6200, true);
Check(interactionState.Update([independent], 24, new(0, 0, 1), 6300, true).Automatic == null, "Two-second automatic-copy cooldown suppresses boundary chatter");
interactionState.Update([independent], 24, new(0, 0, 10), 8500, true);
var nearerAutomatic = independent with { Key = "nearer-auto", Position = new(0, 0, 2) };
Check(interactionState.Update([independent, nearerAutomatic], 24, new(0, 0, 2), 8600, true).Automatic == nearerAutomatic, "Simultaneous entries choose the nearest automatic-copy marker");
Check(interactionState.Update([independent], 25, new(0, 0, 1), 9000, true).Copy == null, "Changing maps removes old proximity targets");
var interactionPrefs = new EditorPreferences();
Check(interactionPrefs.ShowMarkerInformation && interactionPrefs.MarkerClipboard == MarkerClipboardMode.Always, "Info is on and clipboard permission defaults to Always; automatic writes still require explicit runtime test mode");
interactionPrefs.ShowMarkerInformation = false;
interactionPrefs.MarkerClipboard = MarkerClipboardMode.Never;
var interactionPrefsCopy = interactionPrefs.Copy();
interactionPrefsCopy.ResetDefaults();
Check(!interactionPrefs.ShowMarkerInformation && interactionPrefs.MarkerClipboard == MarkerClipboardMode.Never
    && interactionPrefsCopy.ShowMarkerInformation && interactionPrefsCopy.MarkerClipboard == MarkerClipboardMode.Always, "Info/clipboard preferences copy and reset without mutating saved settings");
string interactionPrefsPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TacoStufferInteractions_" + Guid.NewGuid().ToString("N") + ".json");
try
{
    interactionPrefs.Save(interactionPrefsPath);
    var reloadedInteractions = EditorPreferences.Load(interactionPrefsPath);
    Check(!reloadedInteractions.ShowMarkerInformation && reloadedInteractions.MarkerClipboard == MarkerClipboardMode.Never, "Info visibility and clipboard policy persist independently of pack XML");
}
finally { System.IO.File.Delete(interactionPrefsPath); }
var infoGroup = embeddedCatalog.Groups.Single(g => g.Key == "info");
var copyGroup = embeddedCatalog.Groups.Single(g => g.Key == "copy");
Check(infoGroup.Members.Select(m => m.Name).SequenceEqual(new[] { "info", "infoRange" })
    && copyGroup.Members.Select(m => m.Name).SequenceEqual(new[] { "copy", "copy-message", "triggerRange", "autoTrigger" }), "Add Attributes exposes the exact Info and Copy bundles without adding XML settings");
var lowercaseRange = XElement.Parse("<POI inforange='4'/>");
AttributeRules.Apply([lowercaseRange], new Dictionary<string, string?> { ["infoRange"] = "8" }, (_, _) => true);
Check(lowercaseRange.Attributes().Count() == 1 && (string?)lowercaseRange.Attribute("inforange") == "8", "Editing lowercase inforange does not introduce a second case alias");
Check((string?)XElement.Parse(new XElement("POI", new XAttribute("info", "A\nB"), new XAttribute("copy", "[&BOAHAAA=] 漢字")).ToString()).Attribute("info") == "A\nB", "Intentional Info line breaks survive XML serialization");

// Source/document lifecycle and export inclusion regression coverage.
Check(ProjectDocuments.NewPath("main", ProjectDocumentKind.Lua) == "scripts/main.lua"
    && ProjectDocuments.NewPath("README.MD", ProjectDocumentKind.Markdown) == "DOCS/README.MD"
    && ProjectDocuments.NewPath("notes", ProjectDocumentKind.PlainText) == "DOCS/notes.txt", "New source/documents use lazy scripts and DOCS folders with one extension");
bool invalidDocumentName = false;
try { ProjectDocuments.NewPath("../escape", ProjectDocumentKind.Lua); }
catch (InvalidOperationException) { invalidDocumentName = true; }
Check(invalidDocumentName, "New document names cannot escape their designated folder");
string authorRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AuthorDocs_" + Guid.NewGuid().ToString("N"));
System.IO.Directory.CreateDirectory(authorRoot);
try
{
    string luaSource = "-- long comment\nlocal message = 'hello'\nreturn message\n";
    string markdownSource = "# Build notes\n\n- [x] Tested\n\n| Build | Status |\n| --- | --- |\n| 3b | Beta |\n\n```lua\nreturn 1\n```\n";
    var author = new ProjectSnapshot("Author Pack", "", [new("markers.xml", "<OverlayData><MarkerCategory name='author_pack' DisplayName='Author Pack'/><POIs/></OverlayData>")], new(),
        CodeDocuments: [new("scripts/main.lua", luaSource, true), new("DOCS/README.md", markdownSource, false), new("DOCS/notes.txt", "Plain text\n", true)], IncludeDocumentation: false);
    string authorFile = ProjectStore.Create(author, authorRoot);
    var loadedAuthor = ProjectStore.Load(authorFile);
    Check(!loadedAuthor.IncludeDocumentation && loadedAuthor.CodeDocuments!.Select(d => d.Open).SequenceEqual(new[] { true, false, true }), "Document inclusion and open/closed view state persist per project");
    Check(loadedAuthor.CodeDocuments![0].Content == luaSource && loadedAuthor.CodeDocuments[1].Content == markdownSource, "Save/reopen preserves source content and terminal newlines exactly");
    string authorFolder = System.IO.Path.GetDirectoryName(authorFile)!;
    string builtDocs = System.IO.Path.Combine(authorRoot, "docs-on.taco");
    TestPackBuilder.Build(builtDocs, author.Documents.Select(d => (d.Name,d.Content)).ToList(), authorFolder, codeDocuments: author.CodeDocuments, includeDocumentation: true);
    using (var zip = System.IO.Compression.ZipFile.OpenRead(builtDocs))
    {
        Check(zip.GetEntry("scripts/main.lua") != null && zip.GetEntry("DOCS/README.md") != null && zip.GetEntry("DOCS/notes.txt") != null, "Documentation inclusion packages tracked scripts, Markdown and text without XML injection");
    }
    string builtNoDocs = System.IO.Path.Combine(authorRoot, "docs-off.taco");
    TestPackBuilder.Build(builtNoDocs, author.Documents.Select(d => (d.Name,d.Content)).ToList(), authorFolder,
        importedAssets: ["DOCS/README.md", "DOCS/notes.txt", "scripts/main.lua"], assetPaths: new ProjectAssetPaths(".",".","."), codeDocuments: author.CodeDocuments, includeDocumentation: false);
    using (var zip = System.IO.Compression.ZipFile.OpenRead(builtNoDocs))
    {
        Check(zip.GetEntry("scripts/main.lua") != null && zip.GetEntry("DOCS/README.md") == null && zip.GetEntry("DOCS/notes.txt") == null, "Documentation exclusion applies to tracked text, imported assets and root asset scans while retaining Lua");
    }
    Check(System.IO.File.Exists(System.IO.Path.Combine(authorFolder,"DOCS/README.md")), "Excluding documentation never deletes the project copy");
    string rendered = MarkdownPreview.Html(markdownSource, "DOCS/README.md", authorFolder);
    Check(rendered.Contains("<h1>") && rendered.Contains("<table>") && rendered.Contains("type=\"checkbox\"") && rendered.Contains("language-lua"), "Markdown preview renders headings, GFM tables/checklists and fenced source blocks");
    string safePreview = MarkdownPreview.Html("<script>alert(1)</script>\n\n[bad](javascript:alert)\n\n![local](../TEXTURES/icon.png)", "DOCS/README.md", authorFolder);
    Check(!safePreview.Contains("<script>") && !safePreview.Contains("href=\"javascript:") && safePreview.Contains("TEXTURES/icon.png"), "Preview escapes raw HTML, blocks active URL schemes and resolves project-relative images");
    bool protectedUntracked = false;
    System.IO.File.WriteAllText(System.IO.Path.Combine(authorFolder,"DOCS/private.txt"),"Keep this");
    try { ProjectStore.Save(authorFile, author with { CodeDocuments = [new("DOCS/private.txt", "overwrite")] }); }
    catch (InvalidOperationException) { protectedUntracked = true; }
    Check(protectedUntracked && System.IO.File.ReadAllText(System.IO.Path.Combine(authorFolder,"DOCS/private.txt")) == "Keep this", "Creating tracked documents cannot overwrite an existing untracked file");
}
finally { System.IO.Directory.Delete(authorRoot, true); }


Check(MarkdownPreview.IsInternalNavigation(null) && MarkdownPreview.IsInternalNavigation(new Uri("about:blank"))
    && MarkdownPreview.IsInternalNavigation(new Uri("about:blank#heading"))
    && !MarkdownPreview.IsInternalNavigation(new Uri("https://example.com"))
    && !MarkdownPreview.IsInternalNavigation(new Uri("javascript:alert(1)")), "Markdown preview accepts WPF in-memory navigation while retaining external-link handling");
Check(ProjectDocuments.RenamePath("scripts/main.lua", "utilities") == "scripts/utilities.lua"
    && ProjectDocuments.RenamePath("DOCS/README.md", "Guide.md") == "DOCS/Guide.md"
    && ProjectDocuments.RenamePath("notes.txt", "changelog") == "changelog.txt", "Renames retain each document's folder and extension, including imported root files");
bool renameTraversalBlocked = false;
try { ProjectDocuments.RenamePath("DOCS/README.md", "../outside"); }
catch (InvalidOperationException) { renameTraversalBlocked = true; }
Check(renameTraversalBlocked, "Rename rejects path traversal");
string managedRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ManagedSources_" + Guid.NewGuid().ToString("N"));
System.IO.Directory.CreateDirectory(managedRoot);
try
{
    var sources = new ProjectSnapshot("Managed Sources", "", [new("markers.xml", "<OverlayData><MarkerCategory name='pack'/><POIs/></OverlayData>")], new(),
        ImportedAssets: ["scripts/main.lua"], CodeDocuments: [new("scripts/main.lua", "return 17\n", true), new("DOCS/README.md", "# Keep me\n"), new("DOCS/notes.txt", "Notes\n")]);
    string file = ProjectStore.Create(sources, managedRoot);
    string folder = System.IO.Path.GetDirectoryName(file)!;
    var renamed = sources with { ImportedAssets = ["scripts/utilities.lua"], CodeDocuments = [new("scripts/utilities.lua", "return 17\n", true), new("DOCS/Guide.md", "# Keep me\n"), new("DOCS/notes.txt", "Notes\n")] };
    ProjectStore.Save(file, renamed);
    var reloaded = ProjectStore.Load(file);
    Check(!System.IO.File.Exists(System.IO.Path.Combine(folder,"scripts/main.lua")) && !System.IO.File.Exists(System.IO.Path.Combine(folder,"DOCS/README.md"))
        && reloaded.CodeDocuments![0].Content == "return 17\n" && reloaded.CodeDocuments[1].Name == "DOCS/Guide.md", "Saving renamed sources removes old paths and preserves content/reopen state");
    var deletedSourcesSnapshot = reloaded with { ImportedAssets = [], CodeDocuments = [new("DOCS/Guide.md", "# Keep me\n")] };
    ProjectStore.Save(file, deletedSourcesSnapshot);
    Check(!System.IO.File.Exists(System.IO.Path.Combine(folder,"scripts/utilities.lua")) && !System.IO.File.Exists(System.IO.Path.Combine(folder,"DOCS/notes.txt"))
        && ProjectStore.Load(file).CodeDocuments!.Single().Name == "DOCS/Guide.md", "Saving source deletions removes disk files and manifest entries, retaining other documents");
    string packed = System.IO.Path.Combine(managedRoot,"after-delete.taco");
    TestPackBuilder.Build(packed, deletedSourcesSnapshot.Documents.Select(d => (d.Name,d.Content)).ToList(), folder,
        assetPaths: new ProjectAssetPaths(".",".","."), codeDocuments: deletedSourcesSnapshot.CodeDocuments);
    using var zip = System.IO.Compression.ZipFile.OpenRead(packed);
    Check(zip.GetEntry("scripts/main.lua") == null && zip.GetEntry("scripts/utilities.lua") == null && zip.GetEntry("DOCS/README.md") == null
        && zip.GetEntry("DOCS/notes.txt") == null && zip.GetEntry("DOCS/Guide.md") != null, "Root asset scans cannot resurrect renamed/deleted documents into compiled packs");
}
finally { System.IO.Directory.Delete(managedRoot,true); }

Console.WriteLine("Core editing, export, GPU geometry, trail picking, history, preferences, interactions and search checks passed.");
