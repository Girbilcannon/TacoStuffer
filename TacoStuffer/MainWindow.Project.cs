using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;
using TacoStuffer.Models;
using TacoStuffer.Views;

namespace TacoStuffer;

public partial class MainWindow
{
    private string? projectFile;
    private ProjectAssetPaths assetPaths = new();
    private string tacoOutputFolder = "";
    private string savedFingerprint = "";
    private long nextMarkerNumber = 1;
    private string WorkspaceFingerprint() => ProjectTitle.Text + "\0" + tacoOutputFolder + "\0" + string.Join("\0", pages.Select(p => p.Name + "\0" + PageSource(p))) + "\0" + string.Join("\0", textureLabels.OrderBy(p => p.Key).Select(p => p.Key + "=" + p.Value)) + "\0" + string.Join("\0", categoryOrder) + "\0" + nextMarkerNumber + "\0" + nextTrailNumber + "\0" + string.Join("\0", editableTrails.Select(EditableTrails.Serialize)) + "\0" + string.Join("\0", codePages.Select(p => p.Name + "\0" + HistorySource(p.Editor))) + "\0" + assetPaths;

    private void InitializeProjectPersistence()
    {
        categoryOrder = CategoryOrder.Collect(pages.Select(p => p.Xml));
        savedFingerprint = WorkspaceFingerprint();
        Closing += EditorClosing;
    }

    private bool AllowProjectChange()
    {
        if (workspaceBusy) return false;
        if (WorkspaceFingerprint() == savedFingerprint) return true;
        var prompt = new UnsavedProjectWindow { Owner = this };
        if (prompt.ShowDialog() != true) return false;
        return prompt.Choice == "discard" || (prompt.Choice == "save" && SaveCurrentProject());
    }

    private ProjectSnapshot CurrentSnapshot() => new(ProjectTitle.Text, tacoOutputFolder, pages.Select(p => new ProjectXml(p.Name, p.FormattedXml, TabOwner(p.Tab) != null || detachedTabs.ContainsKey(p.Tab))).ToList(), new Dictionary<string, string>(textureLabels, StringComparer.OrdinalIgnoreCase), new List<string>(categoryOrder), collapsedElements.ToList(), nextMarkerNumber, editableTrails.ToList(), nextTrailNumber, importedAssets.ToList(), codePages.Select(p => new ProjectCode(p.Name, HistorySource(p.Editor))).ToList(), assetPaths);

    private static bool ProjectError(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or XmlException or ArgumentException;

    private bool SaveCurrentProject()
    {
        if (projectFile == null && pages.Count == 0)
        {
            Notice("No project", "Create or load a project first.");
            return false;
        }
        if (!TrySyncXml()) return false;
        try
        {
            if (projectFile == null)
            {
                var setup = new NewProjectWindow("Save workspace as a project") { Owner = this };
                if (setup.ShowDialog() != true) return false;
                var snapshot = CurrentSnapshot() with { Name = setup.ProjectName, OutputFolder = setup.OutputLocation };
                string? sourceFolder = projectFolder;
                string newFolder = Path.GetFullPath(Path.Combine(setup.ProjectLocation, setup.ProjectName));
                if (sourceFolder != null)
                    foreach (string child in new[] { "DATA", "TEXTURES", "CUSTOM_ATTRIBUTES", EditableTrails.Folder })
                    {
                        string source = Path.GetFullPath(Path.Combine(sourceFolder, child));
                        if (newFolder.Equals(source, StringComparison.OrdinalIgnoreCase) || newFolder.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("Save the new project outside the current asset folders.");
                    }
                string file = ProjectStore.Create(snapshot, setup.ProjectLocation);
                try
                {
                    if (sourceFolder != null) CopyWorkspaceAssets(sourceFolder, Path.GetDirectoryName(file)!);
                }
                catch
                {
                    Directory.Delete(Path.GetDirectoryName(file)!, true);
                    throw;
                }
                projectFile = file;
                projectFolder = Path.GetDirectoryName(file);
                tacoOutputFolder = setup.OutputLocation;
                ProjectTitle.Text = setup.ProjectName;
                catalog.Reload(projectFolder);
            }
            else ProjectStore.Save(projectFile, CurrentSnapshot());
            savedFingerprint = WorkspaceFingerprint();
            EditorNotice.Text = "Project saved. XML files retain their project-relative paths.";
            StatusText.Text = "Saved " + projectFile;
            return true;
        }
        catch (Exception ex) when (ProjectError(ex))
        {
            Notice("Project save failed", ex.Message);
            return false;
        }
    }

    private static void CopyWorkspaceAssets(string sourceFolder, string destinationFolder)
    {
        void CopyFolder(string source, string destination)
        {
            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Linked asset folders cannot be copied into a project.");
            Directory.CreateDirectory(destination);
            foreach (string entry in Directory.EnumerateFileSystemEntries(source))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Linked assets cannot be copied into a project.");
                string target = Path.Combine(destination, Path.GetFileName(entry));
                if ((attributes & FileAttributes.Directory) != 0) CopyFolder(entry, target);
                else File.Copy(entry, target, false);
            }
        }
        foreach (string child in new[] { "DATA", "TEXTURES", "CUSTOM_ATTRIBUTES", EditableTrails.Folder })
        {
            if (child == EditableTrails.Folder) continue; // Active sources were serialized by ProjectStore.Create.
            string source = Path.Combine(sourceFolder, child);
            if (Directory.Exists(source)) CopyFolder(source, Path.Combine(destinationFolder, child));
        }
    }

    private void SaveProject(object sender, RoutedEventArgs e) => SaveCurrentProject();

    private void OpenProject(object sender, RoutedEventArgs e)
    {
        var open = new OpenFileDialog { Title = "Open Cilantro project", Filter = "Cilantro project (*.cilantro)|*.cilantro" };
        if (open.ShowDialog(this) != true) return;
        try
        {
            var snapshot = ProjectStore.Load(open.FileName);
            var xml = snapshot.Documents.Select(d => ParseXml(d.Content)).ToList();
            string? error = AttributeTypeError(xml);
            if (error != null) throw new InvalidOperationException(error);
            if (!AllowProjectChange()) return;
            ApplyProject(open.FileName, snapshot);
        }
        catch (Exception ex) when (ProjectError(ex))
        {
            Notice("Project load failed", ex.Message);
        }
    }

    private void ApplyProject(string file, ProjectSnapshot snapshot)
    {
        restoringHistory = true;
        try
        {
            ApplyProjectCore(file, snapshot);
        }
        finally
        {
            restoringHistory = false;
        }
        ResetHistory();
    }

    private void ApplyProjectCore(string file, ProjectSnapshot snapshot)
    {
        if (!ReturnGameToDesktop()) throw new InvalidOperationException("Return GW2 to the desktop before changing projects.");
        long markerSeed = MarkerRecording.NextNumber(snapshot.Documents.Select(d => ParseXml(d.Content)), snapshot.NextMarkerNumber);
        StopTrailRecording();
        xmlTimer.Stop();
        sourceSelectionTimer.Stop();
        overlay?.Close();
        searchTimer?.Stop();
        SearchQuery = "";
        SearchBox.Text = "";
        searchTimer?.Stop();
        updating = true;
        foreach (var host in detachedTabs.Values.ToList()) host.Close();
        Documents.Items.Clear();
        ComparisonDocuments.Items.Clear();
        pages.Clear();
        documentUse.Clear();
        codePages.Clear();
        rawTrailPreview.Clear();
        selected = null;
        GameTab.Visibility = Visibility.Collapsed;
        AddTab.Visibility = Visibility.Visible;
        Documents.Items.Add(GameTab);
        Documents.Items.Add(AddTab);
        UpdateBuildStatus();
        projectFile = file;
        projectFolder = Path.GetDirectoryName(file);
        tacoOutputFolder = snapshot.OutputFolder;
        ProjectTitle.Text = snapshot.Name;
        assetPaths = (snapshot.AssetPaths ?? ((snapshot.ImportedAssets?.Count ?? 0) > 0 ? ProjectAssetPaths.Infer(snapshot.Documents.Select(d => ParseXml(d.Content))) : new())).Validated();
        importedAssets.Clear();
        importedAssets.AddRange(snapshot.ImportedAssets ?? []);
        nextMarkerNumber = markerSeed;
        quickMenuNodes.Clear();
        quickMenuRows.Clear();
        quickMenuExpanded.Clear();
        hiddenPreviewCategories.Clear();
        previewMarkers.Clear();
        previewTrails.Clear();
        searchPointIds.Clear();
        assetPickerCache.Clear();
        editableTrails.Clear();
        editableTrails.AddRange(snapshot.Trails ?? []);
        nextTrailNumber = snapshot.NextTrailNumber;
        recordingTrail = null;
        recordingSegment = null;
        selectedTrailPoints.Clear();
        pointLookups.Clear();
        initializedTrailSegments.Clear();
        trailSampler.Reset();
        textureLabels.Clear();
        foreach (var label in snapshot.TextureLabels) textureLabels[label.Key] = label.Value;
        collapsedElements.Clear();
        foreach (string key in snapshot.CollapsedElements ?? []) collapsedElements.Add(key);
        categoryOrder = CategoryOrder.Collect(snapshot.Documents.Select(d => ParseXml(d.Content)), snapshot.CategoryOrder);
        var normalized = CategoryOrder.Normalize(snapshot.Documents.Select(d => ParseXml(d.Content)).ToList(), categoryOrder);
        int repairedTrailCategories = (snapshot.ImportedAssets?.Count ?? 0) == 0 ? EditableTrails.RemoveLegacyCategories(normalized, editableTrails) : 0;
        for (int i = 0; i < snapshot.Documents.Count; i++)
        {
            var document = snapshot.Documents[i];
            var editor = new RichTextBox { FontFamily = new FontFamily("Consolas"), FontSize = 13, AcceptsTab = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var tab = new TabItem { Content = editor };
            Register(document.Name, editor, tab, FormatXml(normalized[i]), loadEditor: false);
            collapsedElements.Add(document.Name + "|" + document.Name);
            updating = true;
            if (document.Open && OpenDocumentTabs().Count() < 5) OpenTab(tab);
        }
        // Retain all XMLs; make one visible if the saved project had every tab closed.
        if (pages.Count > 0 && !pages.Any(p => Documents.Items.Contains(p.Tab))) OpenTab(pages[0].Tab);
        Documents.SelectedItem = pages.FirstOrDefault(p => Documents.Items.Contains(p.Tab))?.Tab;
        UpdateComparisonLayout();
        updating = false;
        xmlNumber = 2;
        foreach (var code in snapshot.CodeDocuments ?? []) RegisterCode(code);
        catalog.Reload(projectFolder);
        RefreshExplorer();
        AttributesHost.Children.Clear();
        SelectionTitle.Text = "Select an element";
        CategoryPath.Text = "";
        savedFingerprint = WorkspaceFingerprint();
        EditorNotice.Text = repairedTrailCategories > 0 ? $"Removed {repairedTrailCategories} untouched generated trail categories. Save to retain the cleanup." : "Project loaded. Save writes every XML, including closed tabs.";
        StatusText.Text = "Project: " + file;
    }

    private void CreateProject()
    {
        if (!AllowProjectChange()) return;
        var setup = new NewProjectWindow { Owner = this };
        if (setup.ShowDialog() != true) return;
        try
        {
            string categoryName = Regex.Replace(setup.ProjectName.ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
            if (categoryName.Length == 0) categoryName = "pack";
            var xml = new XDocument(new XElement("OverlayData", new XElement("MarkerCategory", new XAttribute("name", categoryName), new XAttribute("DisplayName", setup.ProjectName)), new XElement("POIs")));
            var snapshot = new ProjectSnapshot(setup.ProjectName, setup.OutputLocation, [new ProjectXml("markers.xml", xml.ToString())], new Dictionary<string, string>());
            string file = ProjectStore.Create(snapshot, setup.ProjectLocation);
            ApplyProject(file, snapshot);
            StatusText.Text = "Created " + file;
        }
        catch (Exception ex) when (ProjectError(ex))
        {
            Notice("Project creation failed", ex.Message);
        }
    }
}
