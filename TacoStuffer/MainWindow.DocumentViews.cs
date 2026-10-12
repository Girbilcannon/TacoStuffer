using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;

namespace TacoStuffer;

public partial class MainWindow
{
    private readonly Dictionary<TabItem, long> documentUse = [];
    private long documentUseNumber;
    private bool viewRefreshQueued;
    private bool ViewOpen(TabItem tab) => TabOwner(tab) != null || detachedTabs.ContainsKey(tab);

    private IEnumerable<TabItem> OpenDocumentTabs() => pages.Select(p => p.Tab).Concat(codePages.Select(p => p.Tab)).Where(ViewOpen);

    private static string PageSource(XmlPage page) => page.Loaded && page.Dirty ? HistorySource(page.Editor) : page.Source;

    private void SetPageSource(XmlPage page, string source)
    {
        if (page.Source == source && !page.Dirty) return;
        page.Source = source;
        page.Dirty = false;
        if (!page.Loaded) return;
        bool previous = updating;
        updating = true;
        try
        {
            ColorXml(page.Editor, source);
        }
        finally
        {
            updating = previous;
        }
    }

    private void EnsureEditor(XmlPage page)
    {
        if (page.Loaded) return;
        bool previous = updating;
        updating = true;
        try
        {
            ColorXml(page.Editor, page.Source);
            page.Loaded = true;
        }
        finally
        {
            updating = previous;
        }
    }

    private void UnloadEditor(TabItem tab)
    {
        ReleaseHostedDocument(tab);
        var page = pages.FirstOrDefault(p => p.Tab == tab);
        if (page?.Loaded != true) return;
        page.Source = PageSource(page);
        bool previous = updating;
        updating = true;
        try
        {
            page.Loaded = false;
            page.ReleaseSourceIndex();
            page.Editor.Tag = null;
            page.Editor.Document = new FlowDocument();
        }
        finally
        {
            updating = previous;
        }
    }

    private void PrepareDocumentView(TabItem tab)
    {
        if (!pages.Any(p => p.Tab == tab) && !codePages.Any(p => p.Tab == tab)) return;
        if (!ViewOpen(tab))
            while (OpenDocumentTabs().Count() >= 5)
            {
                var oldest = OpenDocumentTabs().OrderBy(t => documentUse.GetValueOrDefault(t)).First();
                CloseTab(oldest);
            }
        documentUse[tab] = ++documentUseNumber;
        if (pages.FirstOrDefault(p => p.Tab == tab) is { } page) EnsureEditor(page);
    }

    private void QueueViewRefresh()
    {
        if (viewRefreshQueued || projectFile == null) return;
        viewRefreshQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            viewRefreshQueued = false;
            if (!updating) RefreshExplorer(refreshScene: false);
        }));
    }
}
