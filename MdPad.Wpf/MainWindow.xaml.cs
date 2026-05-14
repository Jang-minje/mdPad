using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using WpfColor = System.Windows.Media.Color;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace MdPad.Wpf;

public partial class MainWindow : Window
{
    private readonly MarkdownRenderer _renderer = new();
    private readonly DispatcherTimer _previewDebounceTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly DispatcherTimer _sessionSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly SessionStateStore _sessionStateStore = new();
    private readonly Dictionary<Guid, PreviewCacheEntry> _previewCache = [];
    private readonly double[] _fontSizes = Enumerable.Range(8, 29).Select(size => (double)size).ToArray();
    private bool _isUpdatingEditor;
    private bool _isUpdatingStyleControls;
    private bool _isUpdatingStartupMenu;
    private bool _isExitRequested;
    private bool _launchOnLogin = true;
    private bool _isPreviewReady;
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private System.Windows.Forms.ToolStripMenuItem? _trayStartupMenuItem;
    private DocumentMode _mode = DocumentMode.Edit;
    private ThemeMode _theme = ThemeMode.Default;
    private EditorStyleSettings _defaultStyle = new();
    private StyleShortcutSettings _styleShortcuts = new();
    private int _lastSearchIndex = -1;
    private Guid? _renderedPreviewTabId;
    private ScrollViewer? _tabsScrollViewer;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        Tabs = [];
        _previewDebounceTimer.Tick += (_, _) =>
        {
            _previewDebounceTimer.Stop();
            RefreshPreview();
        };
        _sessionSaveTimer.Tick += (_, _) =>
        {
            _sessionSaveTimer.Stop();
            SaveSession();
        };
        Loaded += MainWindow_OnLoaded;
        PreviewKeyDown += MainWindow_OnPreviewKeyDown;
    }

    public ObservableCollection<DocumentTab> Tabs { get; }

    private DocumentTab? CurrentTab => TabsListBox.SelectedItem as DocumentTab;

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        string[][] pendingExternalArguments = [];
        if (System.Windows.Application.Current is App app)
        {
            app.ExternalArgumentsReceived += App_OnExternalArgumentsReceived;
            pendingExternalArguments = app.DrainPendingExternalArguments();
        }

        InitializeTray();
        await PreviewWebView.EnsureCoreWebView2Async();
        PreviewWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
        PreviewWebView.CoreWebView2.WebMessageReceived += PreviewWebView_OnWebMessageReceived;
        _isPreviewReady = true;

        InitializeStyleControls();
        RestoreSession();
        UpdateStyleShortcutMenuText();
        ApplyTheme();
        ApplyStartupRegistration(_launchOnLogin);
        ApplyProtocolRegistration();
        UpdateStartupMenu();
        var commandLineArguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
        var protocolArguments = GetProtocolArguments(commandLineArguments).ToList();
        if (Tabs.Count == 0 && protocolArguments.Count == 0)
        {
            AddNewTab();
        }

        ApplyMode(_mode);
        await ProcessProtocolArgumentsAsync(commandLineArguments);
        foreach (var pendingArguments in pendingExternalArguments)
        {
            await ProcessProtocolArgumentsAsync(pendingArguments);
        }

        if (Environment.GetCommandLineArgs().Any(arg => arg.Equals("--tray", StringComparison.OrdinalIgnoreCase)))
        {
            HideToTray(showTip: false);
        }
    }

    private async void App_OnExternalArgumentsReceived(string[] args)
    {
        ShowFromTray();
        await ProcessProtocolArgumentsAsync(args);
    }

    private void AddNewTab(string title = "Untitled", string markdown = "")
    {
        ApplyMode(DocumentMode.Edit);
        var tab = new DocumentTab
        {
            Title = title,
            Markdown = markdown,
            FontFamily = _defaultStyle.FontFamily,
            FontSize = _defaultStyle.FontSize,
            IsDirty = false,
        };
        tab.PropertyChanged += Tab_OnPropertyChanged;
        Tabs.Add(tab);
        TabsListBox.SelectedItem = tab;
        QueueSessionSave();
    }

    private void Tab_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentTab.DisplayTitle))
        {
            TabsListBox.Items.Refresh();
        }

        if (e.PropertyName is nameof(DocumentTab.DisplayTitle) or nameof(DocumentTab.Markdown) or nameof(DocumentTab.FilePath) or nameof(DocumentTab.IsDirty))
        {
            QueueSessionSave();
        }
    }

    private void TabsListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        LoadCurrentTabIntoEditor();
        UpdateStyleControlsFromCurrentTab();
        QueuePreviewRefresh();
        UpdateTitle();
        TabsListBox.ScrollIntoView(TabsListBox.SelectedItem);
        UpdateTabScrollButtons();
        QueueSessionSave();
    }

    private void TabsListBox_OnLoaded(object sender, RoutedEventArgs e)
    {
        _tabsScrollViewer = FindVisualChildren<ScrollViewer>(TabsListBox).FirstOrDefault();
        if (_tabsScrollViewer is not null)
        {
            _tabsScrollViewer.ScrollChanged += (_, _) => UpdateTabScrollButtons();
            _tabsScrollViewer.SizeChanged += (_, _) => UpdateTabScrollButtons();
        }

        UpdateTabScrollButtons();
    }

    private void TabsListBox_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        ScrollTabsBy(e.Delta > 0 ? -96 : 96);
        e.Handled = true;
    }

    private void TabScrollLeftButton_OnClick(object sender, RoutedEventArgs e) => ScrollTabsBy(-180);

    private void TabScrollRightButton_OnClick(object sender, RoutedEventArgs e) => ScrollTabsBy(180);

    private void ScrollTabsBy(double delta)
    {
        if (_tabsScrollViewer is null)
        {
            _tabsScrollViewer = FindVisualChildren<ScrollViewer>(TabsListBox).FirstOrDefault();
        }

        if (_tabsScrollViewer is null)
        {
            return;
        }

        var nextOffset = Math.Clamp(_tabsScrollViewer.HorizontalOffset + delta, 0, _tabsScrollViewer.ScrollableWidth);
        _tabsScrollViewer.ScrollToHorizontalOffset(nextOffset);
        UpdateTabScrollButtons();
    }

    private void UpdateTabScrollButtons()
    {
        if (!IsLoaded)
        {
            return;
        }

        if (_tabsScrollViewer is null)
        {
            _tabsScrollViewer = FindVisualChildren<ScrollViewer>(TabsListBox).FirstOrDefault();
        }

        var canScroll = _tabsScrollViewer is not null && _tabsScrollViewer.ScrollableWidth > 0.5;
        TabScrollLeftButton.Visibility = canScroll ? Visibility.Visible : Visibility.Collapsed;
        TabScrollRightButton.Visibility = canScroll ? Visibility.Visible : Visibility.Collapsed;
        TabScrollLeftButton.IsEnabled = canScroll && _tabsScrollViewer!.HorizontalOffset > 0.5;
        TabScrollRightButton.IsEnabled = canScroll && _tabsScrollViewer!.HorizontalOffset < _tabsScrollViewer.ScrollableWidth - 0.5;
    }

    private void LoadCurrentTabIntoEditor()
    {
        _isUpdatingEditor = true;
        try
        {
            EditorTextBox.Text = CurrentTab?.Markdown ?? string.Empty;
        }
        finally
        {
            _isUpdatingEditor = false;
        }
    }

    private void EditorTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingEditor || CurrentTab is null)
        {
            return;
        }

        CurrentTab.Markdown = EditorTextBox.Text;
        _previewCache.Remove(CurrentTab.Id);
        _renderedPreviewTabId = null;
        QueuePreviewRefresh();
        UpdateTitle();
        UpdateSearchStatus();
    }

    private void NewButton_OnClick(object sender, RoutedEventArgs e) => AddNewTab();

    private void OpenButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "All files (*.*)|*.*",
            Multiselect = true,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        foreach (var path in dialog.FileNames)
        {
            OpenFileInTab(path);
        }
    }

    private void OpenFileInTab(string path)
    {
        var markdown = File.ReadAllText(path);
        var tab = new DocumentTab
        {
            Title = Path.GetFileName(path),
            FilePath = path,
            Markdown = markdown,
            FontFamily = _defaultStyle.FontFamily,
            FontSize = _defaultStyle.FontSize,
            IsDirty = false,
        };
        tab.PropertyChanged += Tab_OnPropertyChanged;
        Tabs.Add(tab);
        TabsListBox.SelectedItem = tab;
        QueueSessionSave();
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e) => SaveCurrentTab(false);

    private void SaveAsButton_OnClick(object sender, RoutedEventArgs e) => SaveCurrentTab(true);

    private bool SaveCurrentTab(bool forceSaveAs)
    {
        if (CurrentTab is not { } tab)
        {
            return true;
        }

        var path = tab.FilePath;
        if (forceSaveAs || string.IsNullOrWhiteSpace(path))
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Markdown files (*.md)|*.md|Text files (*.txt)|*.txt|All files (*.*)|*.*",
                FileName = tab.Title.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? tab.Title : $"{tab.Title}.md",
            };
            if (dialog.ShowDialog(this) != true)
            {
                return false;
            }

            path = dialog.FileName;
        }

        File.WriteAllText(path, tab.Markdown);
        tab.FilePath = path;
        tab.Title = Path.GetFileName(path);
        tab.MarkSaved();
        StatusTextBlock.Text = $"저장됨: {path}";
        UpdateTitle();
        QueueSessionSave();
        return true;
    }

    private void CloseTabButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (CurrentTab is not null)
        {
            CloseTab(CurrentTab);
        }
    }

    private void TabCloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is DocumentTab tab)
        {
            CloseTab(tab);
            e.Handled = true;
        }
    }

    private void TabContextCloseMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (GetTabFromContextMenu(sender) is { } tab)
        {
            CloseTab(tab);
        }
    }

    private void TabContextCloseRightMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (GetTabFromContextMenu(sender) is not { } tab)
        {
            return;
        }

        var startIndex = Tabs.IndexOf(tab);
        if (startIndex < 0 || startIndex >= Tabs.Count - 1)
        {
            return;
        }

        var tabsToClose = Tabs.Skip(startIndex + 1).ToList();
        CloseTabs(tabsToClose);
    }

    private void TabContextCloseAllMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        CloseTabs(Tabs.ToList());
    }

    private static DocumentTab? GetTabFromContextMenu(object sender)
    {
        return sender is FrameworkElement { Parent: ContextMenu { PlacementTarget: FrameworkElement target } }
            ? target.DataContext as DocumentTab
            : null;
    }

    private bool CloseTab(DocumentTab tab)
    {
        if (!ConfirmSaveIfNeeded(tab))
        {
            return false;
        }

        _previewCache.Remove(tab.Id);
        tab.PropertyChanged -= Tab_OnPropertyChanged;
        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        if (Tabs.Count == 0)
        {
            AddNewTab();
        }
        else
        {
            TabsListBox.SelectedIndex = Math.Clamp(index, 0, Tabs.Count - 1);
        }

        QueueSessionSave();
        return true;
    }

    private void CloseTabs(IReadOnlyList<DocumentTab> tabsToClose)
    {
        foreach (var tab in tabsToClose)
        {
            if (!Tabs.Contains(tab))
            {
                continue;
            }

            if (!CloseTab(tab))
            {
                break;
            }
        }
    }

    private bool ConfirmSaveIfNeeded(DocumentTab tab)
    {
        if (!tab.IsDirty)
        {
            return true;
        }

        var result = System.Windows.MessageBox.Show(this, $"{tab.Title} 변경 내용을 저장할까요?", "MD Pad", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (result == MessageBoxResult.Cancel)
        {
            return false;
        }

        if (result == MessageBoxResult.No)
        {
            return true;
        }

        TabsListBox.SelectedItem = tab;
        return SaveCurrentTab(false);
    }

    private void EditModeButton_OnChecked(object sender, RoutedEventArgs e) => ApplyMode(DocumentMode.Edit);

    private void PreviewModeButton_OnChecked(object sender, RoutedEventArgs e) => ApplyMode(DocumentMode.Preview);

    private void EditModeMenuItem_OnClick(object sender, RoutedEventArgs e) => ApplyMode(DocumentMode.Edit);

    private void PreviewModeMenuItem_OnClick(object sender, RoutedEventArgs e) => ApplyMode(DocumentMode.Preview);

    private void DefaultThemeMenuItem_OnClick(object sender, RoutedEventArgs e) => SetTheme(ThemeMode.Default);

    private void DarkThemeMenuItem_OnClick(object sender, RoutedEventArgs e) => SetTheme(ThemeMode.Dark);

    private void ApplyMode(DocumentMode mode)
    {
        _mode = mode;
        EditorHost.Visibility = mode == DocumentMode.Edit ? Visibility.Visible : Visibility.Collapsed;
        PreviewHost.Visibility = mode == DocumentMode.Preview ? Visibility.Visible : Visibility.Collapsed;
        EditModeButton.IsChecked = mode == DocumentMode.Edit;
        PreviewModeButton.IsChecked = mode == DocumentMode.Preview;
        if (mode == DocumentMode.Preview)
        {
            RefreshPreview();
        }

        QueueSessionSave();
    }

    private void QueuePreviewRefresh()
    {
        if (_mode != DocumentMode.Preview)
        {
            return;
        }

        _previewDebounceTimer.Stop();
        _previewDebounceTimer.Start();
    }

    private void RefreshPreview()
    {
        if (!_isPreviewReady || CurrentTab is null)
        {
            return;
        }

        var cacheChanged = false;
        if (!_previewCache.TryGetValue(CurrentTab.Id, out var cache) ||
            cache.Markdown != CurrentTab.Markdown ||
            cache.Title != CurrentTab.Title ||
            cache.FontFamily != CurrentTab.FontFamily ||
            Math.Abs(cache.FontSize - CurrentTab.FontSize) > 0.001 ||
            cache.Theme != _theme)
        {
            cache = new PreviewCacheEntry(CurrentTab.Title, CurrentTab.Markdown, CurrentTab.FontFamily, CurrentTab.FontSize, _theme, _renderer.RenderDocument(CurrentTab.Title, CurrentTab.Markdown, CurrentTab.FontFamily, CurrentTab.FontSize, _theme));
            _previewCache[CurrentTab.Id] = cache;
            cacheChanged = true;
        }

        if (_renderedPreviewTabId == CurrentTab.Id && !cacheChanged)
        {
            return;
        }

        PreviewWebView.NavigateToString(cache.Html);
        _renderedPreviewTabId = CurrentTab.Id;
    }

    private void PreviewWebView_OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            StatusTextBlock.Text = "미리보기 렌더링 실패";
        }
    }

    private void PreviewWebView_OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeElement))
            {
                return;
            }

            switch (typeElement.GetString())
            {
                case "toggle-task":
                    if (CurrentTab is not null && root.TryGetProperty("label", out var label))
                    {
                        CurrentTab.Markdown = MarkdownEditHelpers.ToggleChecklistByLabel(CurrentTab.Markdown, label.GetString() ?? string.Empty);
                        _previewCache.Remove(CurrentTab.Id);
                        _renderedPreviewTabId = null;
                        LoadCurrentTabIntoEditor();
                        RefreshPreview();
                    }
                    break;
                case "copy-code":
                    if (root.TryGetProperty("codeText", out var code))
                    {
                        System.Windows.Clipboard.SetText(code.GetString() ?? string.Empty);
                        StatusTextBlock.Text = "코드 복사됨";
                    }
                    break;
                case "change-code-language":
                    if (CurrentTab is not null &&
                        root.TryGetProperty("blockIndex", out var blockIndexElement) &&
                        root.TryGetProperty("language", out var languageElement))
                    {
                        CurrentTab.Markdown = ChangeCodeBlockLanguage(CurrentTab.Markdown, blockIndexElement.GetInt32(), languageElement.GetString() ?? "txt");
                        _previewCache.Remove(CurrentTab.Id);
                        _renderedPreviewTabId = null;
                        LoadCurrentTabIntoEditor();
                        RefreshPreview();
                    }
                    break;
                case "open-link":
                    if (root.TryGetProperty("href", out var href))
                    {
                        Process.Start(new ProcessStartInfo(href.GetString() ?? string.Empty) { UseShellExecute = true });
                    }
                    break;
                case "adjust-font-size":
                    if (root.TryGetProperty("delta", out var delta))
                    {
                        AdjustCurrentTabFontSize(delta.GetDouble());
                    }
                    break;
            }
        }
        catch
        {
        }
    }

    private void MainWindow_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (TryHandleStyleShortcut(e))
        {
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
        {
            ShowSearch();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
        {
            SaveCurrentTab(false);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.O)
        {
            OpenButton_OnClick(sender, e);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N)
        {
            AddNewTab();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && SearchBar.Visibility == Visibility.Visible)
        {
            HideSearch();
            e.Handled = true;
        }
    }

    private void EditorTextBox_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.X:
                    CutEditorSelection();
                    e.Handled = true;
                    return;
                case Key.C:
                    CopyEditorSelection();
                    e.Handled = true;
                    return;
                case Key.V:
                    PasteIntoEditor();
                    e.Handled = true;
                    return;
                case Key.A:
                    EditorTextBox.SelectAll();
                    StatusTextBlock.Text = "전체 선택";
                    e.Handled = true;
                    return;
            }
        }

        if (e.Key == Key.Tab)
        {
            var selectionStart = EditorTextBox.SelectionStart;
            EditorTextBox.SelectedText = "    ";
            EditorTextBox.SelectionStart = selectionStart + 4;
            e.Handled = true;
        }
    }

    private void CutEditorSelection()
    {
        if (EditorTextBox.SelectionLength <= 0)
        {
            StatusTextBlock.Text = "잘라내기: 선택된 텍스트가 없습니다.";
            return;
        }

        var selectedText = EditorTextBox.SelectedText;
        if (!TrySetClipboardText(selectedText, out var error))
        {
            StatusTextBlock.Text = $"잘라내기 실패: {error}";
            return;
        }

        EditorTextBox.SelectedText = string.Empty;
        StatusTextBlock.Text = $"잘라내기 완료: {selectedText.Length:N0}자";
    }

    private void CopyEditorSelection()
    {
        if (EditorTextBox.SelectionLength <= 0)
        {
            StatusTextBlock.Text = "복사: 선택된 텍스트가 없습니다.";
            return;
        }

        StatusTextBlock.Text = TrySetClipboardText(EditorTextBox.SelectedText, out var error)
            ? $"복사 완료: {EditorTextBox.SelectionLength:N0}자"
            : $"복사 실패: {error}";
    }

    private void PasteIntoEditor()
    {
        try
        {
            if (!System.Windows.Clipboard.ContainsText())
            {
                StatusTextBlock.Text = "붙여넣기: 클립보드에 텍스트가 없습니다.";
                return;
            }

            var text = System.Windows.Clipboard.GetText();
            var selectionStart = EditorTextBox.SelectionStart;
            EditorTextBox.SelectedText = text;
            EditorTextBox.Select(selectionStart + text.Length, 0);
            StatusTextBlock.Text = $"붙여넣기 완료: {text.Length:N0}자";
        }
        catch (Exception exception)
        {
            StatusTextBlock.Text = $"붙여넣기 실패: {exception.Message}";
        }
    }

    private static bool TrySetClipboardText(string text, out string error)
    {
        Exception? lastException = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetDataObject(text, copy: true);
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                lastException = exception;
                Thread.Sleep(45);
            }
        }

        error = BuildClipboardError(lastException);
        return false;
    }

    private static string BuildClipboardError(Exception? exception)
    {
        var owner = GetOpenClipboardProcessName();
        var ownerText = string.IsNullOrWhiteSpace(owner) ? "점유 프로세스 확인 안 됨" : $"점유 프로세스: {owner}";
        if (exception is null)
        {
            return $"클립보드 접근 실패, {ownerText}";
        }

        return $"{exception.GetType().Name} 0x{exception.HResult:X8}: {exception.Message}, {ownerText}";
    }

    private static string? GetOpenClipboardProcessName()
    {
        try
        {
            var windowHandle = GetOpenClipboardWindow();
            if (windowHandle == IntPtr.Zero)
            {
                return null;
            }

            _ = GetWindowThreadProcessId(windowHandle, out var processId);
            if (processId == 0)
            {
                return $"HWND 0x{windowHandle.ToInt64():X}";
            }

            using var process = Process.GetProcessById((int)processId);
            return $"{process.ProcessName}({processId})";
        }
        catch
        {
            return null;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetOpenClipboardWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private void InsertTableMenuItem_OnClick(object sender, RoutedEventArgs e) => InsertTableSnippet();

    private void InsertChecklistMenuItem_OnClick(object sender, RoutedEventArgs e) => InsertSnippet("\n- [ ] 할 일\n- [ ] 확인할 일\n");

    private void InsertCodeBlockMenuItem_OnClick(object sender, RoutedEventArgs e) => InsertSnippet("\n```txt\n코드\n```\n");

    private void InsertImageMenuItem_OnClick(object sender, RoutedEventArgs e) => InsertSnippet("\n![설명](https://example.com/image.png)\n");

    private void InsertLinkMenuItem_OnClick(object sender, RoutedEventArgs e) => InsertSnippet("[링크](https://example.com)");

    private void InsertDividerMenuItem_OnClick(object sender, RoutedEventArgs e) => InsertSnippet("\n---\n");

    private void InsertTableSnippet() => InsertSnippet("\n| 왼쪽 정렬 | 가운데 정렬 | 오른쪽 정렬 |\n| :--- | :---: | ---: |\n| 값 | 내용 | 100 |\n");

    private bool TryHandleStyleShortcut(System.Windows.Input.KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.None)
        {
            return false;
        }

        if (IsShortcutMatch(_styleShortcuts.Table, e))
        {
            InsertTableSnippet();
            e.Handled = true;
            return true;
        }

        if (IsShortcutMatch(_styleShortcuts.Checklist, e))
        {
            InsertChecklistMenuItem_OnClick(this, e);
            e.Handled = true;
            return true;
        }

        if (IsShortcutMatch(_styleShortcuts.CodeBlock, e))
        {
            InsertCodeBlockMenuItem_OnClick(this, e);
            e.Handled = true;
            return true;
        }

        if (IsShortcutMatch(_styleShortcuts.Image, e))
        {
            InsertImageMenuItem_OnClick(this, e);
            e.Handled = true;
            return true;
        }

        if (IsShortcutMatch(_styleShortcuts.Link, e))
        {
            InsertLinkMenuItem_OnClick(this, e);
            e.Handled = true;
            return true;
        }

        if (IsShortcutMatch(_styleShortcuts.Divider, e))
        {
            InsertDividerMenuItem_OnClick(this, e);
            e.Handled = true;
            return true;
        }

        return false;
    }

    private static bool IsShortcutMatch(string gestureText, System.Windows.Input.KeyEventArgs e)
    {
        if (!TryParseShortcut(gestureText, out var key, out var modifiers))
        {
            return false;
        }

        var actualKey = e.Key == Key.System ? e.SystemKey : e.Key;
        return actualKey == key && Keyboard.Modifiers == modifiers;
    }

    private static bool TryParseShortcut(string gestureText, out Key key, out ModifierKeys modifiers)
    {
        key = Key.None;
        modifiers = ModifierKeys.None;

        try
        {
            if (new KeyGestureConverter().ConvertFromString(gestureText) is not KeyGesture gesture)
            {
                return false;
            }

            key = gesture.Key;
            modifiers = gesture.Modifiers;
            return key != Key.None && modifiers != ModifierKeys.None;
        }
        catch
        {
            return false;
        }
    }

    private void InsertSnippet(string snippet)
    {
        ApplyMode(DocumentMode.Edit);
        EditorTextBox.Focus();
        var start = EditorTextBox.SelectionStart;
        EditorTextBox.SelectedText = snippet;
        EditorTextBox.SelectionStart = start + snippet.Length;
    }

    private void ContentHost_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            return;
        }

        AdjustCurrentTabFontSize(e.Delta > 0 ? 1 : -1);
        e.Handled = true;
    }

    private void ShowSearch()
    {
        SearchBar.Visibility = Visibility.Visible;
        SearchTextBox.Focus();
        SearchTextBox.SelectAll();
        UpdateSearchStatus();
    }

    private void ShowSearchMenuItem_OnClick(object sender, RoutedEventArgs e) => ShowSearch();

    private void IncreaseFontMenuItem_OnClick(object sender, RoutedEventArgs e) => AdjustCurrentTabFontSize(1);

    private void DecreaseFontMenuItem_OnClick(object sender, RoutedEventArgs e) => AdjustCurrentTabFontSize(-1);

    private void TableShortcutMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        ConfigureStyleShortcut("표 삽입", _styleShortcuts.Table, value => _styleShortcuts.Table = value);

    private void ChecklistShortcutMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        ConfigureStyleShortcut("체크리스트 삽입", _styleShortcuts.Checklist, value => _styleShortcuts.Checklist = value);

    private void CodeBlockShortcutMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        ConfigureStyleShortcut("코드블럭 삽입", _styleShortcuts.CodeBlock, value => _styleShortcuts.CodeBlock = value);

    private void ImageShortcutMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        ConfigureStyleShortcut("이미지 삽입", _styleShortcuts.Image, value => _styleShortcuts.Image = value);

    private void LinkShortcutMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        ConfigureStyleShortcut("링크 삽입", _styleShortcuts.Link, value => _styleShortcuts.Link = value);

    private void DividerShortcutMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        ConfigureStyleShortcut("구분선 삽입", _styleShortcuts.Divider, value => _styleShortcuts.Divider = value);

    private void VersionInfoMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var version = GetAppVersion();
        System.Windows.MessageBox.Show(
            this,
            $"MD Pad WV2\n버전: {version}",
            "버전 정보",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ConfigureStyleShortcut(string label, string currentValue, Action<string> apply)
    {
        var input = new System.Windows.Controls.TextBox
        {
            Text = currentValue,
            Margin = new Thickness(0, 8, 0, 12),
            MinWidth = 260,
            Height = 28,
        };

        var description = new TextBlock
        {
            Text = "예: Ctrl+Alt+T, Ctrl+Shift+T",
            Foreground = WpfSolidColorBrushCache("#606060"),
            Margin = new Thickness(0, 0, 0, 8),
        };

        var okButton = new System.Windows.Controls.Button { Content = "저장", Width = 72, Height = 28, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancelButton = new System.Windows.Controls.Button { Content = "취소", Width = 72, Height = 28, IsCancel = true };
        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        buttons.Children.Add(okButton);
        buttons.Children.Add(cancelButton);

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = $"{label} 단축키", FontWeight = FontWeights.SemiBold });
        panel.Children.Add(input);
        panel.Children.Add(description);
        panel.Children.Add(buttons);

        var dialog = new Window
        {
            Title = "스타일 단축키",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            Content = panel,
        };

        okButton.Click += (_, _) =>
        {
            var value = input.Text.Trim();
            if (!TryParseShortcut(value, out _, out _))
            {
                System.Windows.MessageBox.Show(dialog, "단축키 형식이 올바르지 않습니다.", "MD Pad", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            apply(value);
            UpdateStyleShortcutMenuText();
            QueueSessionSave();
            dialog.DialogResult = true;
        };

        _ = dialog.ShowDialog();
    }

    private static WpfSolidColorBrush WpfSolidColorBrushCache(string hex) => Brush(hex);

    private void HideSearch()
    {
        SearchBar.Visibility = Visibility.Collapsed;
        if (_mode == DocumentMode.Edit)
        {
            EditorTextBox.Focus();
        }
    }

    private void CloseSearchButton_OnClick(object sender, RoutedEventArgs e) => HideSearch();

    private void SearchTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        _lastSearchIndex = -1;
        UpdateSearchStatus();
    }

    private void SearchTextBox_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Find(forward: !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), keepSearchFocus: true);
            e.Handled = true;
        }
    }

    private void FindNextButton_OnClick(object sender, RoutedEventArgs e) => Find(forward: true, keepSearchFocus: true);

    private void FindPrevButton_OnClick(object sender, RoutedEventArgs e) => Find(forward: false, keepSearchFocus: true);

    private void FindNext()
    {
        Find(forward: true, keepSearchFocus: false);
    }

    private void FindPrevious()
    {
        Find(forward: false, keepSearchFocus: false);
    }

    private async void Find(bool forward, bool keepSearchFocus)
    {
        var query = SearchTextBox.Text;
        var text = EditorTextBox.Text;
        if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(text))
        {
            return;
        }

        if (_mode == DocumentMode.Preview && _isPreviewReady && PreviewWebView.CoreWebView2 is not null)
        {
            var queryJson = JsonSerializer.Serialize(query);
            var backwards = forward ? "false" : "true";
            await PreviewWebView.CoreWebView2.ExecuteScriptAsync($"window.find({queryJson}, false, {backwards}, true, false, false, false);");
            if (keepSearchFocus)
            {
                SearchTextBox.Focus();
            }

            UpdateSearchStatus();
            return;
        }

        var comparison = StringComparison.CurrentCultureIgnoreCase;
        var start = _lastSearchIndex >= 0 ? _lastSearchIndex + (forward ? 1 : -1) : EditorTextBox.SelectionStart;
        int index;
        if (forward)
        {
            index = text.IndexOf(query, Math.Clamp(start, 0, text.Length), comparison);
            if (index < 0)
            {
                index = text.IndexOf(query, 0, comparison);
            }
        }
        else
        {
            index = text.LastIndexOf(query, Math.Clamp(start, 0, Math.Max(0, text.Length - 1)), comparison);
            if (index < 0)
            {
                index = text.LastIndexOf(query, comparison);
            }
        }

        if (index < 0)
        {
            SearchStatusTextBlock.Text = "0개";
            return;
        }

        _lastSearchIndex = index;
        EditorTextBox.Focus();
        EditorTextBox.Select(index, query.Length);
        EditorTextBox.ScrollToLine(EditorTextBox.GetLineIndexFromCharacterIndex(index));
        if (keepSearchFocus)
        {
            _ = Dispatcher.BeginInvoke(() =>
            {
                SearchTextBox.Focus();
                SearchTextBox.CaretIndex = SearchTextBox.Text.Length;
            }, DispatcherPriority.Input);
        }

        UpdateSearchStatus();
    }

    private void UpdateSearchStatus()
    {
        var query = SearchTextBox.Text;
        if (string.IsNullOrEmpty(query))
        {
            SearchStatusTextBlock.Text = string.Empty;
            return;
        }

        var count = 0;
        var index = 0;
        while ((index = EditorTextBox.Text.IndexOf(query, index, StringComparison.CurrentCultureIgnoreCase)) >= 0)
        {
            count += 1;
            index += Math.Max(1, query.Length);
        }

        SearchStatusTextBlock.Text = $"{count}개";
    }

    private void Window_OnDragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = GetDroppedMarkdownFiles(e).Count > 0 ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        foreach (var path in GetDroppedMarkdownFiles(e))
        {
            OpenFileInTab(path);
        }
        e.Handled = true;
    }

    private static List<string> GetDroppedMarkdownFiles(System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop) ||
            e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] paths)
        {
            return [];
        }

        return paths
            .Where(path => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static string ChangeCodeBlockLanguage(string markdown, int targetBlockIndex, string language)
    {
        var safeLanguage = string.IsNullOrWhiteSpace(language) ? "txt" : language.Trim();
        using var reader = new StringReader(markdown ?? string.Empty);
        var result = new List<string>();
        var codeBlockIndex = -1;
        var inFence = false;
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                if (!inFence)
                {
                    codeBlockIndex++;
                    inFence = true;
                    if (codeBlockIndex == targetBlockIndex)
                    {
                        var indentLength = line.Length - trimmed.Length;
                        result.Add($"{line[..indentLength]}```{safeLanguage}");
                        continue;
                    }
                }
                else
                {
                    inFence = false;
                }
            }

            result.Add(line);
        }

        return string.Join(Environment.NewLine, result);
    }

    private async Task OpenProtocolArgumentAsync(string rawArgument)
    {
        try
        {
            var command = ParseProtocolArgument(rawArgument);
            if (command is null)
            {
                StatusTextBlock.Text = "지원하지 않는 mdpad 프로토콜입니다.";
                return;
            }

            switch (command.Value.Kind)
            {
                case "content":
                    AddNewTab("프로토콜 문서", command.Value.Value);
                    StatusTextBlock.Text = "프로토콜 내용으로 새 탭을 열었습니다.";
                    break;
                case "url":
                    var (title, markdown) = await LoadMarkdownFromUrlAsync(command.Value.Value);
                    AddNewTab(title, markdown);
                    StatusTextBlock.Text = $"URL 문서를 열었습니다: {command.Value.Value}";
                    break;
            }
        }
        catch (Exception exception)
        {
            StatusTextBlock.Text = $"프로토콜 처리 실패: {exception.Message}";
        }
    }

    private async Task ProcessProtocolArgumentsAsync(IEnumerable<string> args)
    {
        foreach (var protocolArgument in GetProtocolArguments(args))
        {
            await OpenProtocolArgumentAsync(protocolArgument);
        }
    }

    private static IEnumerable<string> GetProtocolArguments(IEnumerable<string> args) =>
        args.Where(arg => arg.StartsWith("mdpad:", StringComparison.OrdinalIgnoreCase));

    private static ProtocolCommand? ParseProtocolArgument(string rawArgument)
    {
        var decoded = WebUtility.HtmlDecode(rawArgument.Trim().Trim('"'));
        if (!decoded.StartsWith("mdpad:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var payload = decoded["mdpad:".Length..].TrimStart('/', '\\');
        if (payload.StartsWith("?"))
        {
            payload = payload[1..];
        }

        if (payload.Contains('?'))
        {
            payload = payload[(payload.IndexOf('?') + 1)..];
        }

        var separatorIndex = payload.IndexOf('=');
        if (separatorIndex <= 0)
        {
            return null;
        }

        var key = payload[..separatorIndex].Trim().ToLowerInvariant();
        var value = WebUtility.UrlDecode(WebUtility.HtmlDecode(payload[(separatorIndex + 1)..].Trim().Trim('"')));
        if (key is not ("content" or "url") || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return new ProtocolCommand(key, value);
    }

    private static async Task<(string Title, string Markdown)> LoadMarkdownFromUrlAsync(string url)
    {
        url = NormalizeProtocolUrlValue(url);
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            using var client = new HttpClient();
            var markdown = await client.GetStringAsync(url);
            return (GetTitleFromPathOrUrl(url), markdown);
        }

        string path;
        if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(url, UriKind.Absolute, out var fileUri))
        {
            path = fileUri.LocalPath;
        }
        else
        {
            path = url;
        }

        return (Path.GetFileName(path), await File.ReadAllTextAsync(path));
    }

    private static string NormalizeProtocolUrlValue(string value)
    {
        var normalized = value.Trim();
        normalized = normalized.Replace('\\', '/');
        if (normalized.StartsWith("https/", StringComparison.OrdinalIgnoreCase) &&
            !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return "https://" + normalized["https/".Length..].TrimStart('/');
        }

        if (normalized.StartsWith("http/", StringComparison.OrdinalIgnoreCase) &&
            !normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return "http://" + normalized["http/".Length..].TrimStart('/');
        }

        if (normalized.StartsWith("https//", StringComparison.OrdinalIgnoreCase))
        {
            return "https://" + normalized["https//".Length..];
        }

        if (normalized.StartsWith("https:/", StringComparison.OrdinalIgnoreCase) &&
            !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return "https://" + normalized["https:/".Length..].TrimStart('/');
        }

        if (normalized.StartsWith("http//", StringComparison.OrdinalIgnoreCase))
        {
            return "http://" + normalized["http//".Length..];
        }

        if (normalized.StartsWith("http:/", StringComparison.OrdinalIgnoreCase) &&
            !normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return "http://" + normalized["http:/".Length..].TrimStart('/');
        }

        return normalized;
    }

    private static string GetTitleFromPathOrUrl(string value)
    {
        try
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                var fileName = Path.GetFileName(uri.LocalPath);
                return string.IsNullOrWhiteSpace(fileName) ? "URL 문서" : fileName;
            }

            return Path.GetFileName(value);
        }
        catch
        {
            return "URL 문서";
        }
    }

    private void Window_OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_isExitRequested)
        {
            e.Cancel = true;
            SaveSession();
            HideToTray(showTip: true);
            return;
        }

        SaveSession();
        if (System.Windows.Application.Current is App app)
        {
            app.ExternalArgumentsReceived -= App_OnExternalArgumentsReceived;
        }
        _notifyIcon?.Dispose();
    }

    private void UpdateTitle()
    {
        Title = CurrentTab is null ? "MD Pad" : $"{CurrentTab.DisplayTitle} - MD Pad";
    }

    private void RestoreSession()
    {
        var session = _sessionStateStore.Load();
        _mode = session.Mode;
        _theme = session.Theme;
        _launchOnLogin = session.LaunchOnLogin;
        _defaultStyle = session.DefaultStyle ?? new EditorStyleSettings();
        _styleShortcuts = NormalizeShortcuts(session.StyleShortcuts);
        _defaultStyle.FontSize = Math.Clamp(_defaultStyle.FontSize, 8, 36);
        if (string.IsNullOrWhiteSpace(_defaultStyle.FontFamily))
        {
            _defaultStyle.FontFamily = "Malgun Gothic";
        }

        foreach (var document in session.Documents)
        {
            var tab = new DocumentTab
            {
                Id = Guid.TryParse(document.Id, out var id) ? id : Guid.NewGuid(),
                Title = string.IsNullOrWhiteSpace(document.Title) ? "Untitled" : document.Title,
                FilePath = document.FilePath,
                Markdown = document.Markdown ?? string.Empty,
                FontFamily = string.IsNullOrWhiteSpace(document.FontFamily) ? _defaultStyle.FontFamily : document.FontFamily,
                FontSize = document.FontSize <= 0 ? _defaultStyle.FontSize : document.FontSize,
                IsDirty = document.IsDirty,
            };
            tab.PropertyChanged += Tab_OnPropertyChanged;
            Tabs.Add(tab);
        }

        if (Tabs.Count == 0)
        {
            return;
        }

        TabsListBox.SelectedItem = Tabs.FirstOrDefault(tab => tab.Id.ToString("N") == session.SelectedTabId) ?? Tabs[0];
        UpdateStyleControlsFromCurrentTab();
        UpdateStyleShortcutMenuText();
    }

    private void QueueSessionSave()
    {
        _sessionSaveTimer.Stop();
        _sessionSaveTimer.Start();
    }

    private static StyleShortcutSettings NormalizeShortcuts(StyleShortcutSettings? shortcuts)
    {
        var defaults = new StyleShortcutSettings();
        if (shortcuts is null)
        {
            return defaults;
        }

        return new StyleShortcutSettings
        {
            Table = IsValidShortcut(shortcuts.Table) ? shortcuts.Table : defaults.Table,
            Checklist = IsValidShortcut(shortcuts.Checklist) ? shortcuts.Checklist : defaults.Checklist,
            CodeBlock = IsValidShortcut(shortcuts.CodeBlock) ? shortcuts.CodeBlock : defaults.CodeBlock,
            Image = IsValidShortcut(shortcuts.Image) ? shortcuts.Image : defaults.Image,
            Link = IsValidShortcut(shortcuts.Link) ? shortcuts.Link : defaults.Link,
            Divider = IsValidShortcut(shortcuts.Divider) ? shortcuts.Divider : defaults.Divider,
        };
    }

    private static bool IsValidShortcut(string value) => TryParseShortcut(value, out _, out _);

    private void UpdateStyleShortcutMenuText()
    {
        TableInsertMenuItem.InputGestureText = _styleShortcuts.Table;
        ChecklistInsertMenuItem.InputGestureText = _styleShortcuts.Checklist;
        CodeBlockInsertMenuItem.InputGestureText = _styleShortcuts.CodeBlock;
        ImageInsertMenuItem.InputGestureText = _styleShortcuts.Image;
        LinkInsertMenuItem.InputGestureText = _styleShortcuts.Link;
        DividerInsertMenuItem.InputGestureText = _styleShortcuts.Divider;

        TableShortcutMenuItem.Header = $"표 삽입... ({_styleShortcuts.Table})";
        ChecklistShortcutMenuItem.Header = $"체크리스트 삽입... ({_styleShortcuts.Checklist})";
        CodeBlockShortcutMenuItem.Header = $"코드블럭 삽입... ({_styleShortcuts.CodeBlock})";
        ImageShortcutMenuItem.Header = $"이미지 삽입... ({_styleShortcuts.Image})";
        LinkShortcutMenuItem.Header = $"링크 삽입... ({_styleShortcuts.Link})";
        DividerShortcutMenuItem.Header = $"구분선 삽입... ({_styleShortcuts.Divider})";
    }

    private void SaveSession()
    {
        _sessionStateStore.Save(new SessionState
        {
            SelectedTabId = CurrentTab?.Id.ToString("N"),
            Mode = _mode,
            Theme = _theme,
            LaunchOnLogin = _launchOnLogin,
            DefaultStyle = _defaultStyle,
            StyleShortcuts = _styleShortcuts,
            Documents = Tabs.Select(tab => new SessionDocument
            {
                Id = tab.Id.ToString("N"),
                Title = tab.Title,
                FilePath = tab.FilePath,
                Markdown = tab.Markdown,
                IsDirty = tab.IsDirty,
                FontFamily = tab.FontFamily,
                FontSize = tab.FontSize,
            }).ToList(),
        });
    }

    private void InitializeStyleControls()
    {
        _isUpdatingStyleControls = true;
        try
        {
            FontFamilyComboBox.ItemsSource = System.Windows.Media.Fonts.SystemFontFamilies
                .Select(font => font.Source)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            FontSizeComboBox.ItemsSource = _fontSizes;
        }
        finally
        {
            _isUpdatingStyleControls = false;
        }
    }

    private void UpdateStyleControlsFromCurrentTab()
    {
        if (CurrentTab is null)
        {
            return;
        }

        _isUpdatingStyleControls = true;
        try
        {
            FontFamilyComboBox.SelectedItem = CurrentTab.FontFamily;
            if (FontFamilyComboBox.SelectedItem is null)
            {
                FontFamilyComboBox.Text = CurrentTab.FontFamily;
            }

            FontSizeComboBox.SelectedItem = _fontSizes.FirstOrDefault(size => Math.Abs(size - CurrentTab.FontSize) < 0.001);
            FontSizeComboBox.Text = CurrentTab.FontSize.ToString("0");
            ApplyCurrentTabStyle(updatePreview: false);
        }
        finally
        {
            _isUpdatingStyleControls = false;
        }
    }

    private void FontFamilyComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingStyleControls || CurrentTab is null || FontFamilyComboBox.SelectedItem is not string fontFamily)
        {
            return;
        }

        CurrentTab.FontFamily = fontFamily;
        ApplyCurrentTabStyle(updatePreview: true);
    }

    private void FontSizeComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingStyleControls || CurrentTab is null)
        {
            return;
        }

        if (FontSizeComboBox.SelectedItem is double size)
        {
            CurrentTab.FontSize = size;
            ApplyCurrentTabStyle(updatePreview: true);
        }
    }

    private void SaveDefaultStyleButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (CurrentTab is null)
        {
            return;
        }

        _defaultStyle = new EditorStyleSettings
        {
            FontFamily = CurrentTab.FontFamily,
            FontSize = CurrentTab.FontSize,
        };
        StatusTextBlock.Text = $"새 탭 기본 스타일 저장: {CurrentTab.FontFamily}, {CurrentTab.FontSize:0}px";
        QueueSessionSave();
    }

    private void AdjustCurrentTabFontSize(double delta)
    {
        if (CurrentTab is null)
        {
            return;
        }

        CurrentTab.FontSize = Math.Clamp(CurrentTab.FontSize + delta, 8, 36);
        UpdateStyleControlsFromCurrentTab();
        ApplyCurrentTabStyle(updatePreview: true);
        StatusTextBlock.Text = $"글자 크기: {CurrentTab.FontSize:0}px";
    }

    private void ApplyCurrentTabStyle(bool updatePreview)
    {
        if (CurrentTab is null)
        {
            return;
        }

        EditorTextBox.FontFamily = new System.Windows.Media.FontFamily(CurrentTab.FontFamily);
        EditorTextBox.FontSize = CurrentTab.FontSize;

        if (updatePreview)
        {
            if (_mode == DocumentMode.Preview)
            {
                ApplyPreviewStyleOnly(CurrentTab);
            }

            QueueSessionSave();
        }
    }

    private async void ApplyPreviewStyleOnly(DocumentTab tab)
    {
        if (!_isPreviewReady || PreviewWebView.CoreWebView2 is null)
        {
            return;
        }

        if (_renderedPreviewTabId != tab.Id)
        {
            RefreshPreview();
            return;
        }

        var fontJson = JsonSerializer.Serialize(tab.FontFamily);
        var size = tab.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await PreviewWebView.CoreWebView2.ExecuteScriptAsync($"""
            document.documentElement.style.setProperty('--pad-font-family', {fontJson} + ', "Malgun Gothic", "Segoe UI", sans-serif');
            document.documentElement.style.setProperty('--pad-font-size', '{size}px');
        """);
    }

    private void SetTheme(ThemeMode theme)
    {
        _theme = theme;
        ApplyTheme();
        _previewCache.Clear();
        _renderedPreviewTabId = null;
        if (_mode == DocumentMode.Preview)
        {
            RefreshPreview();
        }

        QueueSessionSave();
    }

    private void ApplyTheme()
    {
        var dark = _theme == ThemeMode.Dark;
        DefaultThemeMenuItem.IsChecked = !dark;
        DarkThemeMenuItem.IsChecked = dark;

        SetBrush("ChromeBrush", dark ? "#1E1E1E" : "#F3F3F3");
        SetBrush("MenuForegroundBrush", dark ? "#E6E6E6" : "#111827");
        SetBrush("ToolbarButtonBackgroundBrush", dark ? "#2B2B2B" : "#F8F8F8");
        SetBrush("ToolbarButtonHoverBrush", dark ? "#3A3A3A" : "#E7E7E7");
        SetBrush("ToolbarButtonPressedBrush", dark ? "#202020" : "#D8D8D8");
        SetBrush("ToolbarButtonBorderBrush", dark ? "#4A4A4A" : "#B8B8B8");
        SetBrush("ToolbarButtonForegroundBrush", dark ? "#E6E6E6" : "#111827");
        SetBrush("ToolbarLabelBrush", dark ? "#CFCFCF" : "#404040");
        SetBrush("TabItemBackgroundBrush", dark ? "#2D2D2D" : "#ECECEC");
        SetBrush("TabItemForegroundBrush", dark ? "#DCDCDC" : "#111827");
        SetBrush("TabItemSelectedBackgroundBrush", dark ? "#4D5D5D" : "#FFFFFF");
        SetBrush("TabItemSelectedForegroundBrush", dark ? "#FFFFFF" : "#111827");
        SetBrush("TabItemBorderBrush", dark ? "#555555" : "#B8B8B8");

        Background = Brush(dark ? "#1E1E1E" : "#F5F5F5");
        MainMenu.Background = Brush(dark ? "#1E1E1E" : "#F3F3F3");
        MainMenu.Foreground = Brush(dark ? "#E6E6E6" : "#111827");
        ToolbarHost.Background = Brush(dark ? "#1E1E1E" : "#F3F3F3");
        TabBarHost.Background = Brush(dark ? "#252526" : "#E5E5E5");
        TabBarHost.BorderBrush = Brush(dark ? "#3E3E42" : "#C8C8C8");
        StatusHost.Background = Brush(dark ? "#1E1E1E" : "#F3F3F3");
        var editorBackground = Brush(dark ? "#3B3B3B" : "#FFFFFF");
        var editorForeground = Brush(dark ? "#F0F0F0" : "#111827");
        EditorHost.Background = editorBackground;
        EditorTextBox.Background = editorBackground;
        EditorTextBox.Foreground = editorForeground;
        EditorTextBox.CaretBrush = editorForeground;
        PreviewHost.Background = Brush(dark ? "#1E1E1E" : "#FAFAFA");
        PreviewWebView.DefaultBackgroundColor = dark
            ? System.Drawing.Color.FromArgb(255, 13, 17, 23)
            : System.Drawing.Color.FromArgb(255, 250, 250, 250);
        StatusTextBlock.Foreground = Brush(dark ? "#DCDCDC" : "#111827");

        foreach (var comboBox in FindVisualChildren<System.Windows.Controls.ComboBox>(ToolbarHost))
        {
            comboBox.Background = Brush(dark ? "#2B2B2B" : "#FFFFFF");
            comboBox.Foreground = Brush(dark ? "#E6E6E6" : "#111827");
            comboBox.BorderBrush = Brush(dark ? "#4A4A4A" : "#A8A8A8");
        }
    }

    private void SetBrush(string key, string hex)
    {
        Resources[key] = Brush(hex);
    }

    private static string GetAppVersion()
    {
        var informationalVersion = Assembly
            .GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return "2026.05.14.001";
        }

        var metadataIndex = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        return metadataIndex >= 0 ? informationalVersion[..metadataIndex] : informationalVersion;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T typed)
            {
                yield return typed;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static WpfSolidColorBrush Brush(string hex) => new((WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(hex));

    private void InitializeTray()
    {
        if (_notifyIcon is not null)
        {
            return;
        }

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("열기", null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        menu.Items.Add("새 탭", null, (_, _) => Dispatcher.Invoke(() =>
        {
            ShowFromTray();
            AddNewTab();
        }));
        _trayStartupMenuItem = new System.Windows.Forms.ToolStripMenuItem("윈도우 시작 시 자동 실행")
        {
            CheckOnClick = true,
        };
        _trayStartupMenuItem.CheckedChanged += (_, _) => Dispatcher.Invoke(() =>
        {
            if (_isUpdatingStartupMenu || _trayStartupMenuItem is null)
            {
                return;
            }

            SetLaunchOnLogin(_trayStartupMenuItem.Checked);
        });
        menu.Items.Add(_trayStartupMenuItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => Dispatcher.Invoke(ExitApplication));

        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "MD Pad WV2",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _notifyIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        var assetIcon = Path.Combine(AppContext.BaseDirectory, "Assets", "App.ico");
        if (File.Exists(assetIcon))
        {
            return new System.Drawing.Icon(assetIcon);
        }

        var processPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
        {
            var associatedIcon = System.Drawing.Icon.ExtractAssociatedIcon(processPath);
            if (associatedIcon is not null)
            {
                return associatedIcon;
            }
        }

        return System.Drawing.SystemIcons.Application;
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void HideToTray(bool showTip)
    {
        Hide();
        if (showTip)
        {
            _notifyIcon?.ShowBalloonTip(1800, "MD Pad", "트레이에서 계속 실행 중입니다.", System.Windows.Forms.ToolTipIcon.Info);
        }
    }

    private void ExitApplication()
    {
        _isExitRequested = true;
        SaveSession();
        _notifyIcon?.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    private void StartupMenuItem_OnChecked(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingStartupMenu)
        {
            return;
        }

        _launchOnLogin = true;
        SetLaunchOnLogin(true);
    }

    private void StartupMenuItem_OnUnchecked(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingStartupMenu)
        {
            return;
        }

        _launchOnLogin = false;
        SetLaunchOnLogin(false);
    }

    private void SetLaunchOnLogin(bool enabled)
    {
        _launchOnLogin = enabled;
        ApplyStartupRegistration(enabled);
        UpdateStartupMenu();
        QueueSessionSave();
    }

    private void UpdateStartupMenu()
    {
        _isUpdatingStartupMenu = true;
        try
        {
            StartupMenuItem.IsChecked = _launchOnLogin;
            if (_trayStartupMenuItem is not null)
            {
                _trayStartupMenuItem.Checked = _launchOnLogin;
            }
        }
        finally
        {
            _isUpdatingStartupMenu = false;
        }
    }

    private static void ApplyStartupRegistration(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true)
            ?? Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        if (key is null)
        {
            return;
        }

        const string name = "MdPadWv2";
        if (!enabled)
        {
            key.DeleteValue(name, throwOnMissingValue: false);
            return;
        }

        var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return;
        }

        key.SetValue(name, $"\"{exePath}\" --tray");
    }

    private static void ApplyProtocolRegistration()
    {
        var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return;
        }

        using var root = Registry.CurrentUser.CreateSubKey(@"Software\Classes\mdpad");
        root?.SetValue(null, "URL:MD Pad Protocol");
        root?.SetValue("URL Protocol", string.Empty);

        using var command = Registry.CurrentUser.CreateSubKey(@"Software\Classes\mdpad\shell\open\command");
        command?.SetValue(null, $"\"{exePath}\" \"%1\"");
    }

    private sealed record PreviewCacheEntry(string Title, string Markdown, string FontFamily, double FontSize, ThemeMode Theme, string Html);

    private readonly record struct ProtocolCommand(string Kind, string Value);
}
