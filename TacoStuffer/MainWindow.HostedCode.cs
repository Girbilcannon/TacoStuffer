using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using TacoStuffer.Services;
using TacoStuffer.Views;

namespace TacoStuffer;

public partial class MainWindow
{
    private bool inAppLayout, showHostedXml;
    private TabItem? activeHostedCodeTab;
    private HostedCodeWindow? hostedCodeWindow;
    private RichTextBox? hostedEditor;
    private Action? restoreHostedEditor;
    private void EnterInAppLayout()
    {
        if (inAppLayout) return;
        activeHostedCodeTab = pages.FirstOrDefault(p => p.Name == selected?.DocumentName)?.Tab
            ?? Documents.SelectedItem as TabItem;
        if (activeHostedCodeTab == GameTab || activeHostedCodeTab == AddTab) activeHostedCodeTab = pages.FirstOrDefault()?.Tab;
        OpenTab(GameTab);
        inAppLayout = true;
        Documents.Template = (ControlTemplate)FindResource("GamePaneTemplate");
        UpdateComparisonLayout();
        UpdateGameLayoutControls();
    }

    private void ExitInAppLayout()
    {
        ReleaseHostedEditor();
        hostedCodeWindow?.Hide();
        showHostedXml = false;
        HostedXmlButton.Content = "Show XML";
        if (!inAppLayout) return;
        inAppLayout = false;
        Documents.ClearValue(Control.TemplateProperty);
        // Remove the idle game view so its toolbar is replaced by code tabs.
        bool prior = updating;
        updating = true;
        try
        {
            TabOwner(GameTab)?.Items.Remove(GameTab);
            GameTab.Visibility = Visibility.Collapsed;
        }
        finally
        {
            updating = prior;
        }
        UpdateComparisonLayout();
        var tab = activeHostedCodeTab;
        activeHostedCodeTab = null;
        if (tab == null || !pages.Any(p => p.Tab == tab) && !codePages.Any(p => p.Tab == tab))
            tab = pages.FirstOrDefault(p => TabOwner(p.Tab) != null)?.Tab ?? pages.FirstOrDefault()?.Tab ?? codePages.FirstOrDefault()?.Tab;
        if (tab != null && (pages.Any(p => p.Tab == tab) || codePages.Any(p => p.Tab == tab))) OpenTab(tab, activateDetached: false);
        UpdateGameLayoutControls();
    }

    private bool CanUseStandardLayout => !GameViewport.IsAttached && SessionExited(gameSessionProcess)
        && !waitingToHostGame && !mountingGameViewport && !forceCloseInProgress && !applicationClosePending;
    private void UpdateGameLayoutControls()
    {
        bool idle = CanUseStandardLayout;
        ForceCloseGameButton.Visibility = idle ? Visibility.Collapsed : Visibility.Visible;
        StandardLayoutButton.Visibility = idle ? Visibility.Visible : Visibility.Collapsed;
        StandardLayoutButton.IsEnabled = idle;
        StandardLayoutMenuItem.IsEnabled = inAppLayout && idle;
    }

    private void ShowStandardLayout(object sender, RoutedEventArgs e)
    {
        if (!CanUseStandardLayout)
        {
            StatusText.Text = "Close the hosted game before returning to Standard Layout.";
            return;
        }
        waitingToHostGame = mountingGameViewport = false;
        gameAttachRevision++;
        if (gameSessionProcess != null && SessionExited(gameSessionProcess)) CompleteGameExit();
        else
        {
            HideGameViewport();
            ExitInAppLayout();
        }
        StatusText.Text = "Standard Layout · XML code tabs restored.";
    }

    // In-app editing changes the active document without replacing the game pane.
    // The same editor control is moved into the optional floating XML window so
    // selection, text edits and attribute synchronization share one document.
    private void OpenHostedDocument(TabItem tab)
    {
        bool wasOpen = ViewOpen(tab);
        if (activeHostedCodeTab != tab) ReleaseHostedEditor();
        if (detachedTabs.TryGetValue(tab, out var detached)) detached.Close();
        PrepareDocumentView(tab);
        bool prior = updating;
        updating = true;
        try
        {
            if (TabOwner(tab) == null) Documents.Items.Insert(Documents.Items.IndexOf(AddTab), tab);
            tab.Visibility = Visibility.Visible;
            Documents.SelectedItem = GameTab;
        }
        finally
        {
            updating = prior;
        }
        activeHostedCodeTab = tab;
        if (showHostedXml) MountHostedEditor();
        if (!wasOpen) QueueViewRefresh();
    }

    private void ToggleHostedXml(object sender, RoutedEventArgs e)
    {
        showHostedXml = !showHostedXml;
        HostedXmlButton.Content = showHostedXml ? "Hide XML" : "Show XML";
        RequestPreviewPresentation();
        if (!showHostedXml)
        {
            hostedCodeWindow?.Hide();
            ReleaseHostedEditor();
            return;
        }
        activeHostedCodeTab ??= pages.FirstOrDefault(p => p.Name == selected?.DocumentName)?.Tab ?? pages.FirstOrDefault()?.Tab;
        if (activeHostedCodeTab == null)
        {
            showHostedXml = false;
            HostedXmlButton.Content = "Show XML";
            StatusText.Text = "Select an XML document in Elements first.";
            return;
        }
        OpenHostedDocument(activeHostedCodeTab);
        UpdateHostedCodeVisibility();
    }

    private void MountHostedEditor()
    {
        if (activeHostedCodeTab == null) return;
        var page = pages.FirstOrDefault(p => p.Tab == activeHostedCodeTab);
        var code = codePages.FirstOrDefault(p => p.Tab == activeHostedCodeTab);
        var editor = page?.Editor ?? code?.Editor;
        string name = page?.Name ?? code?.Name ?? "XML";
        if (editor == null) return;
        if (hostedEditor == editor)
        {
            hostedCodeWindow?.Rename(name);
            return;
        }
        ReleaseHostedEditor();
        if (page != null) EnsureEditor(page);
        Action restoreParent;
        if (editor.Parent is Panel panel)
        {
            int index = panel.Children.IndexOf(editor);
            panel.Children.Remove(editor);
            restoreParent = () => panel.Children.Insert(Math.Min(index, panel.Children.Count), editor);
        }
        else if ((editor.Parent as ContentControl ?? (activeHostedCodeTab.Content == editor ? activeHostedCodeTab : null)) is ContentControl control)
        {
            control.Content = null;
            restoreParent = () => control.Content = editor;
        }
        else return;
        var background = editor.ReadLocalValue(Control.BackgroundProperty);
        var border = editor.ReadLocalValue(Control.BorderThicknessProperty);
        var horizontalScroll = editor.HorizontalScrollBarVisibility;
        editor.Background = Brushes.Transparent;
        editor.BorderThickness = new Thickness(0);
        editor.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        hostedEditor = editor;
        restoreHostedEditor = () =>
        {
            if (background == DependencyProperty.UnsetValue) editor.ClearValue(Control.BackgroundProperty);
            else editor.SetValue(Control.BackgroundProperty, background);
            if (border == DependencyProperty.UnsetValue) editor.ClearValue(Control.BorderThicknessProperty);
            else editor.SetValue(Control.BorderThicknessProperty, border);
            editor.HorizontalScrollBarVisibility = horizontalScroll;
            restoreParent();
        };
        if (hostedCodeWindow == null)
        {
            hostedCodeWindow = new HostedCodeWindow(() => ToggleHostedXml(this, new RoutedEventArgs())) { Owner = this };
            hostedCodeWindow.PreviewKeyDown += ProjectShortcut;
            hostedCodeWindow.PreviewMouseDown += (_, _) =>
            {
                FlushHistoryCheckpoint();
                RequestPreviewPresentation();
            };
        }
        hostedCodeWindow.SetEditor(editor, name);
    }

    private void ReleaseHostedEditor()
    {
        hostedCodeWindow?.ReleaseEditor();
        restoreHostedEditor?.Invoke();
        restoreHostedEditor = null;
        hostedEditor = null;
    }

    private void ReleaseHostedDocument(TabItem tab)
    {
        if (tab != activeHostedCodeTab) return;
        ReleaseHostedEditor();
        activeHostedCodeTab = null;
        hostedCodeWindow?.Hide();
    }

    private void UpdateHostedCodeVisibility()
    {
        if (hostedCodeWindow == null) return;
        bool visible = inAppLayout && showHostedXml && hostedEditor != null && IsVisible && WindowState != WindowState.Minimized
            && GameViewport.IsAttached && GameViewportSlot.IsVisible
            && !OwnedWindows.Cast<Window>().Any(w => w != hostedCodeWindow && w.IsActive)
            && NativeGameWindow.IsForeground(GameViewport.Client, new WindowInteropHelper(hostedCodeWindow).Handle, Environment.ProcessId);
        if (!visible)
        {
            hostedCodeWindow.Topmost = false;
            if (hostedCodeWindow.IsVisible) hostedCodeWindow.Hide();
            return;
        }
        var a = GameViewportSlot.PointToScreen(new Point(0, 0));
        var b = GameViewportSlot.PointToScreen(new Point(GameViewportSlot.ActualWidth, GameViewportSlot.ActualHeight));
        var bounds = new NativeGameWindow.Rect { Left = (int)Math.Round(a.X), Top = (int)Math.Round(a.Y), Right = (int)Math.Round(b.X), Bottom = (int)Math.Round(b.Y) };
        if (bounds.Right <= bounds.Left || bounds.Bottom <= bounds.Top) return;
        hostedCodeWindow.Topmost = true;
        if (!hostedCodeWindow.IsVisible) hostedCodeWindow.Show();
        hostedCodeWindow.Fit(bounds);
    }
}
