using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MdPad.Wpf;

public enum DocumentMode
{
    Edit,
    Preview,
    Split,
}

public enum ThemeMode
{
    Default,
    Dark,
}

public sealed class DocumentTab : INotifyPropertyChanged
{
    private string _title = "Untitled";
    private string _markdown = string.Empty;
    private string? _filePath;
    private bool _isDirty;
    private string _fontFamily = "Malgun Gothic";
    private double _fontSize = 16;
    private Dictionary<string, CodeBlockViewState> _codeBlockStates = [];

    public Guid Id { get; init; } = Guid.NewGuid();

    public string Title
    {
        get => _title;
        set => SetField(ref _title, string.IsNullOrWhiteSpace(value) ? "Untitled" : value.Trim());
    }

    public string Markdown
    {
        get => _markdown;
        set
        {
            if (SetField(ref _markdown, value ?? string.Empty))
            {
                IsDirty = true;
            }
        }
    }

    public string? FilePath
    {
        get => _filePath;
        set
        {
            if (SetField(ref _filePath, value))
            {
                OnPropertyChanged(nameof(DisplayTitle));
            }
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        set
        {
            if (SetField(ref _isDirty, value))
            {
                OnPropertyChanged(nameof(DisplayTitle));
            }
        }
    }

    public string FontFamily
    {
        get => _fontFamily;
        set => SetField(ref _fontFamily, string.IsNullOrWhiteSpace(value) ? "Malgun Gothic" : value.Trim());
    }

    public double FontSize
    {
        get => _fontSize;
        set => SetField(ref _fontSize, Math.Clamp(value, 8, 36));
    }

    public Dictionary<string, CodeBlockViewState> CodeBlockStates
    {
        get => _codeBlockStates;
        set => SetField(ref _codeBlockStates, value ?? []);
    }

    public string DisplayTitle => $"{(IsDirty ? "*" : string.Empty)}{Title}";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void MarkSaved()
    {
        IsDirty = false;
    }

    public void ReplaceSavedContent(string markdown)
    {
        markdown ??= string.Empty;
        if (!string.Equals(_markdown, markdown, StringComparison.Ordinal))
        {
            _markdown = markdown;
            OnPropertyChanged(nameof(Markdown));
        }

        IsDirty = false;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class CodeBlockViewState
{
    public bool Collapsed { get; set; }
    public bool Wrapped { get; set; }
}
