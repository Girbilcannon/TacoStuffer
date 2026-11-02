using System.IO.Compression;
using System.Numerics;
using System.Text;
using System.Xml.Linq;
using TacoStuffer.Models;

internal static class Namespace103Checks
{
    private static void Check(bool value,string reason) { if (!value) throw new Exception(reason); }
    public static void Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"TacoStuffer103-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            const string guid="AAAAAAAAAAAAAAAAAAAAAA==";
            byte[] lua=Encoding.UTF8.GetBytes("-- Preserve CRLF and script paths\r\nfunction Teh_Test(marker, focused)\r\nend\r\n");
            var trail=new EditableTrail { Number=1,Guid=guid,MapId=15 };
            var segment=trail.AddSegment();segment.Add(new Vector3(1,2,3));segment.Add(new Vector3(4,2,3));
            foreach(string kind in new[]{"default","prefixed","plain"})
            foreach(bool convert in new[]{false,true})
            {
                XNamespace ns=kind=="plain" ? "" : "urn:markerpack";
                XNamespace extension="urn:metadata",xsi="http://www.w3.org/2001/XMLSchema-instance";
                var xml=new XDocument(new XElement(ns+"OverlayData",
                    new XAttribute(XNamespace.Xmlns+"meta",extension),new XAttribute(XNamespace.Xmlns+"xsi",xsi),
                    new XAttribute(xsi+"schemaLocation","urn:markerpack schema.xsd"),
                    new XComment("Keep author notes"),
                    new XElement(ns+"MarkerCategory",new XAttribute("name","pack"),new XAttribute("DisplayName","Pack")),
                    new XElement(ns+"POIs",new XElement(ns+"POI",new XAttribute("guid","AQAAAAAAAAAAAAAAAAAAAA=="),new XAttribute("mapid",15),
                        new XAttribute("xpos",1),new XAttribute("ypos",2),new XAttribute("zpos",3),new XAttribute("type","pack"),
                        new XAttribute("script-focus","Teh_Test()"),new XAttribute(extension+"guid","extension-guid")),
                        new XElement(ns+"Trail",new XAttribute("GUID",guid),new XAttribute("type","pack"),new XAttribute("trailData","Data/route.trl"))),
                    new XElement(extension+"Metadata",new XElement(extension+"POI",new XAttribute("mapid","foreign-metadata"),new XCData("Keep <custom> data")))));
                if(kind=="default")
                {
                    xml.Root!.Add(new XAttribute("xmlns",ns.NamespaceName));
                    xml.Root.Element(ns+"MarkerCategory")!.Add(new XAttribute("xmlns",ns.NamespaceName));
                }
                if(kind=="prefixed")xml.Root!.Add(new XAttribute(XNamespace.Xmlns+"p",ns.NamespaceName));
                string pack=Path.Combine(root,kind+convert+".taco");
                using(var zip=ZipFile.Open(pack,ZipArchiveMode.Create))
                {
                    void Write(string path,byte[] data){using var output=zip.CreateEntry(path).Open();output.Write(data);}
                    Write("markers.xml",Encoding.UTF8.GetBytes(xml.ToString()));Write("Pack.lua",lua);Write("Data/route.trl",EditableTrails.Compile(trail));
                }
                var imported=TacoImporter.Import(pack,kind+convert,root,"",convert);
                var loaded=ProjectStore.Load(imported.ProjectFile);
                var parsed=XDocument.Parse(loaded.Documents.Single().Content);
                Check(parsed.Root!.Name=="OverlayData" && parsed.Descendants("POI").Count()==1 && parsed.Descendants("Trail").Count()==1,"Default/prefixed/plain XML is canonicalized without treating extension elements as pathing data");
                Check((string?)parsed.Descendants("POI").Single().Attribute("MapID")=="15" && (string?)parsed.Descendants("POI").Single().Attribute(extension+"guid")=="extension-guid","Known attributes normalize while namespaced extension attributes are retained");
                Check(parsed.Descendants(extension+"POI").Single().Attribute("mapid")?.Value=="foreign-metadata" && parsed.DescendantNodes().OfType<XComment>().Any(c=>c.Value=="Keep author notes") && parsed.DescendantNodes().OfType<XCData>().Any(),"Foreign elements, their attributes, comments and CDATA survive import/save/reload");
                Check(parsed.Root.Attribute(xsi+"schemaLocation")?.Value=="urn:markerpack schema.xsd" && parsed.Descendants("POI").Single().Attribute("script-focus")?.Value=="Teh_Test()","Schema metadata and script hooks survive normalization");
                Check((loaded.Trails?.Count ?? 0)==(convert?1:0),"Namespace handling works with optional editable trail conversion");
                string rebuilt=Path.Combine(root,kind+convert+"-out.taco");
                TestPackBuilder.Build(rebuilt,loaded.Documents.Select(d=>(d.Name,d.Content)).ToList(),Path.GetDirectoryName(imported.ProjectFile),loaded.Trails,loaded.ImportedAssets,loaded.AssetPaths,loaded.CodeDocuments);
                using var outputZip=ZipFile.OpenRead(rebuilt);
                using var luaStream=outputZip.GetEntry("Pack.lua")!.Open();using var buffer=new MemoryStream();luaStream.CopyTo(buffer);
                Check(buffer.ToArray().SequenceEqual(lua),"Lua bytes remain identical after import, save and export");
            }
            Console.WriteLine("1.0.3 default/prefixed/plain XML namespaces, extension preservation, editable trails and Lua round-trip checks passed.");
        }
        finally { Directory.Delete(root,true); }
    }
}
