using System.IO;
using System.Windows;
using System.Windows.Controls;
using TacoStuffer.Models;
using TacoStuffer.Views;

namespace TacoStuffer;

public partial class MainWindow
{
    private void NewXmlDocument(object sender, RoutedEventArgs e)
    {
        if (projectFile == null) { Notice("Project required", "Create or open a project before adding a document."); return; }
        CreateXmlPage();
    }
    private void NewLuaDocument(object sender, RoutedEventArgs e) => CreateCodeDocument(ProjectDocumentKind.Lua);
    private void NewMarkdownDocument(object sender, RoutedEventArgs e) => CreateCodeDocument(ProjectDocumentKind.Markdown);
    private void NewTextDocument(object sender, RoutedEventArgs e) => CreateCodeDocument(ProjectDocumentKind.PlainText);

    private void RenameCodeDocument(CodePage page)
    {
        if (projectFolder == null || workspaceBusy) return;
        bool Exists(string path) => !path.Equals(page.Name, StringComparison.OrdinalIgnoreCase)
            && (codePages.Any(c => c.Name.Equals(path, StringComparison.OrdinalIgnoreCase))
                || importedAssets.Contains(path, StringComparer.OrdinalIgnoreCase) || File.Exists(Path.Combine(projectFolder, path)));
        var dialog = new DocumentNameWindow(ProjectDocuments.Kind(page.Name)!.Value, Path.GetFileName(page.Name), Exists, page.Name) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.DocumentPath == page.Name) return;
        string oldName = page.Name, newName = dialog.DocumentPath;
        int index = codePages.IndexOf(page);
        if (index < 0) return;
        codePages[index] = page with { Name = newName };
        for (int i = 0; i < importedAssets.Count; i++)
            if (importedAssets[i].Equals(oldName, StringComparison.OrdinalIgnoreCase)) importedAssets[i] = newName;
        page.View.RenameDocument(newName);
        SetTabHeader(page.Tab, newName);
        if (detachedTabs.TryGetValue(page.Tab, out var host)) host.RenameDocument(newName);
        if (activeHostedCodeTab == page.Tab) hostedCodeWindow?.Rename(newName);
        RefreshExplorer(refreshScene: false);
        var row = ElementsList.Items.Cast<ExplorerRow>().FirstOrDefault(r => r.Kind == "code" && r.DocumentName == newName);
        if (row != null) FocusRow(row, false);
        StatusText.Text = "Document renamed to " + newName + ". Save Project to update files. References inside scripts/text are not rewritten.";
    }

    private void DeleteCodeDocuments(IEnumerable<CodePage> documents)
    {
        var targets = documents.Distinct().Where(codePages.Contains).ToList();
        if (targets.Count == 0 || workspaceBusy) return;
        var confirm = new ConfirmActionWindow("Delete documents", "Delete " + string.Join(", ", targets.Select(p => p.Name))
            + "? These files will be removed from the project and disk on Save. References inside scripts/text are not rewritten.") { Owner = this };
        if (confirm.ShowDialog() != true) return;
        foreach (var page in targets)
        {
            page.View.ClosePreview();
            CloseTab(page.Tab);
            codePages.Remove(page);
            documentUse.Remove(page.Tab);
            importedAssets.RemoveAll(p => p.Equals(page.Name, StringComparison.OrdinalIgnoreCase));
        }
        selected = null;
        RefreshExplorer(refreshScene: false);
        AttributesHost.Children.Clear();
        SelectionTitle.Text = "Select an element";
        CategoryPath.Text = "";
        StatusText.Text = "Documents removed from the workspace. Save Project to remove their files.";
    }

    private void DeleteCodeSelection() => DeleteCodeDocuments(codePages.Where(p => ElementsList.SelectedItems.Cast<ExplorerRow>()
        .Any(r => r.Kind == "code" && r.DocumentName == p.Name)).ToList());

    private void CreateCodeDocument(ProjectDocumentKind kind)
    {
        if (projectFile == null || projectFolder == null)
        {
            Notice("Project required", "Create or open a project before adding a document.");
            return;
        }
        string stem = kind switch { ProjectDocumentKind.Lua => "script", ProjectDocumentKind.Markdown => "README", _ => "notes" };
        bool Exists(string path) => codePages.Any(c => c.Name.Equals(path, StringComparison.OrdinalIgnoreCase))
            || importedAssets.Contains(path, StringComparer.OrdinalIgnoreCase) || File.Exists(Path.Combine(projectFolder, path));
        string suggested = stem;
        for (int i = 2; Exists(ProjectDocuments.NewPath(suggested, kind)); i++) suggested = stem + i;
        var dialog = new DocumentNameWindow(kind, suggested, Exists) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try
        {
            // Folders are created lazily for the first document, never at project setup.
            Directory.CreateDirectory(Path.Combine(projectFolder, Path.GetDirectoryName(dialog.DocumentPath)!));
            RegisterCode(new ProjectCode(dialog.DocumentPath, ProjectDocuments.InitialContent(kind), true));
            if (kind == ProjectDocumentKind.Lua) importedAssets.Add(dialog.DocumentPath);
            var page = codePages[^1];
            OpenTab(page.Tab);
            RefreshExplorer(refreshScene: false);
            page.Editor.Focus();
            StatusText.Text = "Created " + dialog.DocumentPath + ". Save Project to write this document.";
        }
        catch (Exception ex) when (ProjectError(ex)) { Notice("Document could not be created", ex.Message); }
    }
}

internal sealed class DocumentNameWindow : PreviewDialog
{
    public string DocumentPath { get; private set; } = "";
    public DocumentNameWindow(ProjectDocumentKind kind, string suggested, Func<string, bool> exists, string? originalPath = null) : base((originalPath == null ? "New " : "Rename ") + kind + " document", 480)
    {
        Label("Filename (stored in " + (originalPath == null ? kind == ProjectDocumentKind.Lua ? "scripts/" : "DOCS/" : Path.GetDirectoryName(originalPath) ?? "project root") + ")");
        var input = Input(suggested);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
        Body.Children.Add(status);
        Action(originalPath == null ? "Create Document" : "Rename Document", (_, _) =>
        {
            try
            {
                string path = originalPath == null ? ProjectDocuments.NewPath(input.Text, kind) : ProjectDocuments.RenamePath(originalPath, input.Text);
                if (exists(path)) { status.Text = "A file with that name already exists. Choose another name."; return; }
                DocumentPath = path;
                DialogResult = true;
            }
            catch (InvalidOperationException ex) { status.Text = ex.Message; }
        });
        Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
    }
}
