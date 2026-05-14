using System.IO;
using System.Text.Json;

namespace MdPad.Wpf;

public sealed class SessionStateStore
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MdPadWv2",
        "session.json");

    public SessionState Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new SessionState();
            }

            return JsonSerializer.Deserialize<SessionState>(File.ReadAllText(_path), _jsonOptions) ?? new SessionState();
        }
        catch
        {
            return new SessionState();
        }
    }

    public void Save(SessionState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(state, _jsonOptions));
    }
}

public sealed class SessionState
{
    public string? SelectedTabId { get; set; }
    public DocumentMode Mode { get; set; } = DocumentMode.Edit;
    public ThemeMode Theme { get; set; } = ThemeMode.Default;
    public bool LaunchOnLogin { get; set; } = true;
    public EditorStyleSettings DefaultStyle { get; set; } = new();
    public StyleShortcutSettings StyleShortcuts { get; set; } = new();
    public List<SessionDocument> Documents { get; set; } = [];
}

public sealed class SessionDocument
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = "Untitled";
    public string? FilePath { get; set; }
    public string Markdown { get; set; } = string.Empty;
    public bool IsDirty { get; set; }
    public string FontFamily { get; set; } = "Malgun Gothic";
    public double FontSize { get; set; } = 16;
}

public sealed class EditorStyleSettings
{
    public string FontFamily { get; set; } = "Malgun Gothic";
    public double FontSize { get; set; } = 16;
}

public sealed class StyleShortcutSettings
{
    public string Table { get; set; } = "Ctrl+Alt+T";
    public string Checklist { get; set; } = "Ctrl+Alt+K";
    public string CodeBlock { get; set; } = "Ctrl+Alt+C";
    public string Image { get; set; } = "Ctrl+Alt+I";
    public string Link { get; set; } = "Ctrl+Alt+L";
    public string Divider { get; set; } = "Ctrl+Alt+D";
}
