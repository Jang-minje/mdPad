using System.IO;
using System.Text.Json;

namespace MdPad.Wpf;

public sealed class TrashDocumentStore
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MdPadWv2",
        "trash");

    public List<TrashDocument> Load(TimeSpan retention)
    {
        Directory.CreateDirectory(_directory);
        var cutoffUtc = DateTime.UtcNow.Subtract(retention);
        var documents = new List<TrashDocument>();

        foreach (var path in Directory.EnumerateFiles(_directory, "*.json"))
        {
            try
            {
                var document = JsonSerializer.Deserialize<TrashDocument>(File.ReadAllText(path), _jsonOptions);
                if (document is null || document.DeletedAtUtc < cutoffUtc)
                {
                    File.Delete(path);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(document.Id))
                {
                    document.Id = Path.GetFileNameWithoutExtension(path);
                }

                documents.Add(document);
            }
            catch
            {
                // Ignore corrupt trash entries. The user can delete the file manually if needed.
            }
        }

        return documents
            .OrderByDescending(document => document.DeletedAtUtc)
            .ToList();
    }

    public void Save(TrashDocument document)
    {
        Directory.CreateDirectory(_directory);
        if (string.IsNullOrWhiteSpace(document.Id))
        {
            document.Id = Guid.NewGuid().ToString("N");
        }

        File.WriteAllText(GetPath(document.Id), JsonSerializer.Serialize(document, _jsonOptions));
    }

    public void Delete(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        var path = GetPath(id);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public void Clear(IEnumerable<TrashDocument> documents)
    {
        foreach (var document in documents)
        {
            Delete(document.Id);
        }
    }

    private string GetPath(string id) => Path.Combine(_directory, $"{id}.json");
}

public sealed class TrashDocument
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = "Untitled";
    public string? FilePath { get; set; }
    public string Markdown { get; set; } = string.Empty;
    public DateTime DeletedAtUtc { get; set; } = DateTime.UtcNow;
    public string FontFamily { get; set; } = "Malgun Gothic";
    public double FontSize { get; set; } = 16;
    public Dictionary<string, CodeBlockViewState> CodeBlockStates { get; set; } = [];
}
