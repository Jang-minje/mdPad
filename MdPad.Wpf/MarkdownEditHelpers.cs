using System.Text.RegularExpressions;

namespace MdPad.Wpf;

public static class MarkdownEditHelpers
{
    public static string ToggleChecklistByLabel(string markdown, string label)
    {
        var lines = markdown.Split('\n').ToList();
        var normalizedLabel = NormalizeChecklistLabel(label);
        for (var index = 0; index < lines.Count; index += 1)
        {
            var match = Regex.Match(lines[index], @"^(\s*[-*+]\s+\[)( |x|X)(\]\s*)(.*)$");
            if (!match.Success)
            {
                continue;
            }

            if (!string.Equals(NormalizeChecklistLabel(match.Groups[4].Value), normalizedLabel, StringComparison.CurrentCulture))
            {
                continue;
            }

            var nextState = string.Equals(match.Groups[2].Value, " ", StringComparison.Ordinal) ? "x" : " ";
            lines[index] = $"{match.Groups[1].Value}{nextState}{match.Groups[3].Value}{match.Groups[4].Value}";
            return string.Join("\n", lines);
        }

        return markdown;
    }

    private static string NormalizeChecklistLabel(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        normalized = Regex.Replace(normalized, @"`([^`]+)`", "$1");
        normalized = Regex.Replace(normalized, @"\*\*([^*]+)\*\*", "$1");
        normalized = Regex.Replace(normalized, @"__([^_]+)__", "$1");
        normalized = Regex.Replace(normalized, @"\*([^*]+)\*", "$1");
        normalized = Regex.Replace(normalized, @"_([^_]+)_", "$1");
        normalized = Regex.Replace(normalized, @"~~([^~]+)~~", "$1");
        normalized = Regex.Replace(normalized, @"\s+", " ");
        return normalized.Trim();
    }
}
