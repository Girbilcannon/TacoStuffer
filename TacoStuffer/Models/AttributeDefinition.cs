using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace TacoStuffer.Models;

public sealed record AttributeMember(string Name, string DefaultValue, string Type, bool Optional);

public sealed record AttributeDefinition(string Key, string DisplayName, string AppliesTo, List<AttributeMember> Members, bool IsCustom)
{
    public bool Applies(XElement element) => AppliesTo.Split(',').Any(v => v.Trim().Equals(element.Name.LocalName, StringComparison.OrdinalIgnoreCase));

    public XElement ToXml() => new("AttributeGroup", new XAttribute("version", "1"), new XAttribute("key", Key), new XAttribute("displayName", DisplayName), new XAttribute("appliesTo", AppliesTo),
        Members.Select(m => new XElement("Attribute", new XAttribute("name", m.Name), new XAttribute("default", m.DefaultValue), new XAttribute("type", m.Type), new XAttribute("optional", m.Optional ? "true" : "false"))));
}

public sealed class AttributeCatalog
{
    public List<AttributeDefinition> Groups { get; } = [];
    public List<string> Errors { get; } = [];
    // Beta packaging: built-in XML definitions are compiled directly into this array.
    // The application does not read the source Attributes/*.xml files at runtime;
    // changing those files alone will not change the catalog used by this beta.
    // Official-release plan: expose editable built-in definition files again. When
    // implementing that, update the loader and publish settings together and verify
    // that defaults, validation and group ordering still match these definitions.
    // Project CUSTOM_ATTRIBUTES remain separate, editable files in the beta.
    private static readonly (string File, string Xml)[] BuiltInDefinitions =
    [
        ("alpha.xml", """
        <AttributeGroup version="1" key="alpha" displayName="Alpha" appliesTo="POI,Trail,MarkerCategory">
          <Attribute name="alpha" default="1.0" type="string" optional="false" />
        </AttributeGroup>
        """),
        ("animSpeed.xml", """
        <AttributeGroup version="1" key="animSpeed" displayName="Animspeed" appliesTo="Trail,MarkerCategory">
          <Attribute name="animSpeed" default="0" type="string" optional="false" />
        </AttributeGroup>
        """),
        ("color.xml", """
        <AttributeGroup version="1" key="color" displayName="Color" appliesTo="POI,Trail,MarkerCategory">
          <Attribute name="color" default="FFFFFFFF" type="string" optional="false" />
        </AttributeGroup>
        """),
        ("fade.xml", """
        <AttributeGroup version="1" key="fade" displayName="Fade" appliesTo="POI,Trail,MarkerCategory">
          <Attribute name="fadeNear" default="3000" type="string" optional="false" />
          <Attribute name="fadeFar" default="3500" type="string" optional="false" />
        </AttributeGroup>
        """),
        ("heightOffset.xml", """
        <AttributeGroup version="1" key="heightOffset" displayName="Heightoffset" appliesTo="POI,Trail,MarkerCategory">
          <Attribute name="heightOffset" default="0" type="string" optional="false" />
        </AttributeGroup>
        """),
        ("iconFile.xml", """
        <AttributeGroup version="1" key="iconFile" displayName="Icon (Markers)" appliesTo="POI,MarkerCategory">
          <Attribute name="iconFile" default="TEXTURES/marker.png" type="file" optional="false" />
        </AttributeGroup>
        """),
        ("iconSize.xml", """
        <AttributeGroup version="1" key="iconSize" displayName="Iconsize" appliesTo="POI,MarkerCategory">
          <Attribute name="iconSize" default="1.0" type="string" optional="true" />
        </AttributeGroup>
        """),
        ("isSeparator.xml", """
        <AttributeGroup version="1" key="isSeparator" displayName="Separator" appliesTo="POI,Trail,MarkerCategory">
          <Attribute name="isSeparator" default="1" type="bool" optional="false" />
        </AttributeGroup>
        """),
        ("mapDisplaySize.xml", """
        <AttributeGroup version="1" key="mapDisplaySize" displayName="Mapdisplaysize" appliesTo="POI,Trail,MarkerCategory">
          <Attribute name="mapDisplaySize" default="20" type="string" optional="false" />
          <Attribute name="scaleOnMapWithZoom" default="true" type="bool" optional="true" />
        </AttributeGroup>
        """),
        ("position.xml", """
        <AttributeGroup version="1" key="position" displayName="Position" appliesTo="POI,MarkerCategory">
          <Attribute name="xpos" default="0" type="string" optional="false" />
          <Attribute name="ypos" default="0" type="string" optional="false" />
          <Attribute name="zpos" default="0" type="string" optional="false" />
        </AttributeGroup>
        """),
        ("rotate.xml", """
        <AttributeGroup version="1" key="rotate" displayName="Rotate" appliesTo="POI,MarkerCategory">
          <Attribute name="rotate-x" default="0" type="string" optional="false" />
          <Attribute name="rotate-y" default="0" type="string" optional="false" />
          <Attribute name="rotate-z" default="0" type="string" optional="false" />
        </AttributeGroup>
        """),
        ("sticky.xml", """
        <AttributeGroup version="1" key="sticky" displayName="Sticky (For Taquito Only)" appliesTo="POI,MarkerCategory">
          <Attribute name="screenSticky" default="true" type="bool" optional="false" />
          <Attribute name="stickyDistance" default="300" type="string" optional="false" />
          <Attribute name="stickyHideOriginal" default="true" type="bool" optional="false" />
          <Attribute name="stickyX" default="0" type="string" optional="true" />
          <Attribute name="stickyY" default="0" type="string" optional="true" />
          <Attribute name="stickyScale" default="1.0" type="string" optional="true" />
        </AttributeGroup>
        """),
        ("texture.xml", """
        <AttributeGroup version="1" key="texture" displayName="Texture (Trails)" appliesTo="Trail,MarkerCategory">
          <Attribute name="texture" default="TEXTURES/trail.png" type="file" optional="false" />
        </AttributeGroup>
        """),
        ("trailScale.xml", """
        <AttributeGroup version="1" key="trailScale" displayName="Trailscale" appliesTo="Trail,MarkerCategory">
          <Attribute name="trailScale" default="1.0" type="string" optional="true" />
        </AttributeGroup>
        """),
        ("visibility.xml", """
        <AttributeGroup version="1" key="visibility" displayName="Visibility" appliesTo="POI,Trail,MarkerCategory">
          <Attribute name="inGameVisibility" default="1" type="bool" optional="false" />
          <Attribute name="miniMapVisibility" default="1" type="bool" optional="false" />
          <Attribute name="mapVisibility" default="1" type="bool" optional="false" />
        </AttributeGroup>
        """),
        ("info.xml", """
        <AttributeGroup version="1" key="info" displayName="Info" appliesTo="POI,MarkerCategory">
          <Attribute name="info" default="" type="multiline" optional="false" />
          <Attribute name="infoRange" default="2" type="number" optional="true" />
        </AttributeGroup>
        """),
        ("copy.xml", """
        <AttributeGroup version="1" key="copy" displayName="Copy" appliesTo="POI,MarkerCategory">
          <Attribute name="copy" default="" type="multiline" optional="false" />
          <Attribute name="copy-message" default="'{0}' copied to clipboard." type="multiline" optional="true" />
          <Attribute name="triggerRange" default="2" type="number" optional="true" />
          <Attribute name="autoTrigger" default="false" type="bool" optional="true" />
        </AttributeGroup>
        """),
    ];
    public void Reload(string? projectFolder)
    {
        Groups.Clear();
        Errors.Clear();
        LoadBuiltIns();
        Combine("iconFile", "iconSize");
        Combine("texture", "trailScale");
        if (projectFolder != null) LoadFolder(Path.Combine(projectFolder, "CUSTOM_ATTRIBUTES"), true);
    }

    private void Combine(string primaryKey, string secondaryKey)
    {
        var primary = Groups.FirstOrDefault(g => g.Key == primaryKey);
        var secondary = Groups.FirstOrDefault(g => g.Key == secondaryKey);
        if (primary == null || secondary == null) return;
        Groups[Groups.IndexOf(primary)] = primary with { Members = primary.Members.Concat(secondary.Members).ToList() };
        Groups.Remove(secondary);
    }

    private void LoadFolder(string folder, bool custom)
    {
        if (!Directory.Exists(folder)) return;
        foreach (string file in Directory.EnumerateFiles(folder, "*.xml").OrderBy(p => p))
        {
            try
            {
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
                using var reader = XmlReader.Create(file, settings);
                LoadDefinition(reader, file, custom);
            }
            catch (Exception ex) when (ex is XmlException or FormatException or IOException or UnauthorizedAccessException)
            {
                Errors.Add(Path.GetFileName(file) + ": " + ex.Message);
            }
        }
    }

    // Use the same XML parser/validation as custom groups so compiled definitions
    // keep the catalog rules. Errors are collected for the editor to report.
    private void LoadBuiltIns()
    {
        foreach (var definition in BuiltInDefinitions)
        {
            try
            {
                using var source = new StringReader(definition.Xml);
                using var reader = XmlReader.Create(source, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                LoadDefinition(reader, definition.File, false);
            }
            catch (Exception ex) when (ex is XmlException or FormatException or IOException or UnauthorizedAccessException)
            {
                Errors.Add(definition.File + ": " + ex.Message);
            }
        }
    }

    private void LoadDefinition(XmlReader reader, string file, bool custom)
    {
        var root = XElement.Load(reader);
        if (root.Name != "AttributeGroup" || (string?)root.Attribute("version") != "1") throw new FormatException("Expected AttributeGroup version 1.");
        var members = root.Elements("Attribute").Select(a => new AttributeMember((string?)a.Attribute("name") ?? "", (string?)a.Attribute("default") ?? "", (string?)a.Attribute("type") ?? "string", (string?)a.Attribute("optional") == "true")).ToList();
        if (members.Count == 0 || members.Any(m => string.IsNullOrWhiteSpace(m.Name))) throw new FormatException("Missing attribute names.");
        foreach (var member in members) XmlConvert.VerifyNCName(member.Name);
        if (members.Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != members.Count) throw new FormatException("Duplicate member names.");
        string key = (string?)root.Attribute("key") ?? Path.GetFileNameWithoutExtension(file);
        if (Groups.Any(g => g.Key.Equals(key, StringComparison.OrdinalIgnoreCase) || g.Members.Any(m => members.Any(n => n.Name.Equals(m.Name, StringComparison.OrdinalIgnoreCase))))) throw new FormatException("A group or member is already defined. Rename or merge the definition.");
        Groups.Add(new(key, (string?)root.Attribute("displayName") ?? key, (string?)root.Attribute("appliesTo") ?? "POI,Trail,MarkerCategory", members, custom));
    }
}
