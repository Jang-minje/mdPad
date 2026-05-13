using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace MdPad.Wpf;

public partial class MainWindow : Window
{
    private readonly MarkdownRenderer _renderer = new();
    private readonly DispatcherTimer _previewDebounceTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly DispatcherTimer _sessionSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly SessionStateStore _sessionStateStore = new();
    private readonly LanShareService _lanShareService = new();
    private readonly Dictionary<Guid, PreviewCacheEntry> _previewCache = [];
    private readonly double[] _fontSizes = Enumerable.Range(8, 29).Select(size => (double)size).ToArray();
    private bool _isUpdatingEditor;
    private bool _isUpdatingStyleControls;
    private bool _isUpdatingStartupMenu;
    private bool _isExitRequested;
    private bool _launchOnLogin = true;
    private bool _isPreviewReady;
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private DocumentMode _mode = DocumentMode.Edit;
    private EditorStyleSettings _defaultStyle = new();
    private int _lastSearchIndex = -1;
    private Guid? _renderedPreviewTabId;

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
        InitializeTray();
        await PreviewWebView.EnsureCoreWebView2Async();
        PreviewWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
        PreviewWebView.CoreWebView2.WebMessageReceived += PreviewWebView_OnWebMessageReceived;
        _isPreviewReady = true;

        _lanShareService.PeersChanged += () => Dispatcher.Invoke(RefreshPeers);
        _lanShareService.DocumentReceived += (title, markdown) => Dispatcher.Invoke(() =>
        {
            AddNewTab(string.IsNullOrWhiteSpace(title) ? "받은 문서" : title, markdown ?? string.Empty);
            StatusTextBlock.Text = "네트워크에서 문서를 받았습니다.";
            _notifyIcon?.ShowBalloonTip(2500, "MD Pad", "네트워크에서 문서를 받았습니다.", System.Windows.Forms.ToolTipIcon.Info);
        });

        try
        {
            await _lanShareService.StartAsync();
            LanStatusTextBlock.Text = $"LAN discovery : {Environment.MachineName}";
        }
        catch (Exception exception)
        {
            LanStatusTextBlock.Text = $"LAN 비활성: {exception.Message}";
        }

        InitializeStyleControls();
        RestoreSession();
        ApplyStartupRegistration(_launchOnLogin);
        UpdateStartupMenu();
        if (Tabs.Count == 0)
        {
            AddNewTab();
        }

        ApplyMode(_mode);
        if (Environment.GetCommandLineArgs().Any(arg => arg.Equals("--tray", StringComparison.OrdinalIgnoreCase)))
        {
            HideToTray(showTip: false);
        }
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
        QueueSessionSave();
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
            Math.Abs(cache.FontSize - CurrentTab.FontSize) > 0.001)
        {
            cache = new PreviewCacheEntry(CurrentTab.Title, CurrentTab.Markdown, CurrentTab.FontFamily, CurrentTab.FontSize, _renderer.RenderDocument(CurrentTab.Title, CurrentTab.Markdown, CurrentTab.FontFamily, CurrentTab.FontSize));
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
        if (e.Key == Key.Tab)
        {
            var selectionStart = EditorTextBox.SelectionStart;
            EditorTextBox.SelectedText = "    ";
            EditorTextBox.SelectionStart = selectionStart + 4;
            e.Handled = true;
        }
    }

    private void InsertTableMenuItem_OnClick(object sender, RoutedEventArgs e) => InsertSnippet("\n| 항목 | 설명 |\n| --- | --- |\n| 값 | 내용 |\n");

    private void InsertChecklistMenuItem_OnClick(object sender, RoutedEventArgs e) => InsertSnippet("\n- [ ] 할 일\n- [ ] 확인할 일\n");

    private void InsertCodeBlockMenuItem_OnClick(object sender, RoutedEventArgs e) => InsertSnippet("\n```txt\n코드\n```\n");

    private void InsertImageMenuItem_OnClick(object sender, RoutedEventArgs e) => InsertSnippet("\n![설명](https://example.com/image.png)\n");

    private void InsertLinkMenuItem_OnClick(object sender, RoutedEventArgs e) => InsertSnippet("[링크](https://example.com)");

    private void InsertDividerMenuItem_OnClick(object sender, RoutedEventArgs e) => InsertSnippet("\n---\n");

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

    private void RefreshPeersButton_OnClick(object sender, RoutedEventArgs e) => RefreshPeers();

    private void RefreshPeers()
    {
        PeerComboBox.ItemsSource = null;
        PeerComboBox.ItemsSource = _lanShareService.Peers;
        if (PeerComboBox.Items.Count > 0 && PeerComboBox.SelectedIndex < 0)
        {
            PeerComboBox.SelectedIndex = 0;
        }
    }

    private async void SendButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (CurrentTab is null)
        {
            return;
        }

        if (PeerComboBox.SelectedItem is not NearbyPeer peer)
        {
            StatusTextBlock.Text = "전송할 컴퓨터를 선택하세요.";
            return;
        }

        try
        {
            await _lanShareService.SendAsync(peer, CurrentTab.Title, CurrentTab.Markdown);
            StatusTextBlock.Text = $"전송됨: {peer.DisplayName}";
        }
        catch (Exception exception)
        {
            StatusTextBlock.Text = $"전송 실패: {exception.Message}";
        }
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
        _notifyIcon?.Dispose();
        _lanShareService.Dispose();
    }

    private void UpdateTitle()
    {
        Title = CurrentTab is null ? "MD Pad" : $"{CurrentTab.DisplayTitle} - MD Pad";
    }

    private void RestoreSession()
    {
        var session = _sessionStateStore.Load();
        _mode = session.Mode;
        _launchOnLogin = session.LaunchOnLogin;
        _defaultStyle = session.DefaultStyle ?? new EditorStyleSettings();
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
    }

    private void QueueSessionSave()
    {
        _sessionSaveTimer.Stop();
        _sessionSaveTimer.Start();
    }

    private void SaveSession()
    {
        _sessionStateStore.Save(new SessionState
        {
            SelectedTabId = CurrentTab?.Id.ToString("N"),
            Mode = _mode,
            LaunchOnLogin = _launchOnLogin,
            DefaultStyle = _defaultStyle,
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
            _notifyIcon?.ShowBalloonTip(1800, "MD Pad", "트레이에서 계속 실행 중입니다. 네트워크 문서 수신이 유지됩니다.", System.Windows.Forms.ToolTipIcon.Info);
        }
    }

    private void ExitApplication()
    {
        _isExitRequested = true;
        SaveSession();
        _notifyIcon?.Dispose();
        _lanShareService.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    private void StartupMenuItem_OnChecked(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingStartupMenu)
        {
            return;
        }

        _launchOnLogin = true;
        ApplyStartupRegistration(true);
        QueueSessionSave();
    }

    private void StartupMenuItem_OnUnchecked(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingStartupMenu)
        {
            return;
        }

        _launchOnLogin = false;
        ApplyStartupRegistration(false);
        QueueSessionSave();
    }

    private void UpdateStartupMenu()
    {
        _isUpdatingStartupMenu = true;
        try
        {
            StartupMenuItem.IsChecked = _launchOnLogin;
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

    private sealed record PreviewCacheEntry(string Title, string Markdown, string FontFamily, double FontSize, string Html);
}
