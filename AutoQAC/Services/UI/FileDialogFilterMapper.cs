using System.Collections.Generic;
using System.Linq;

namespace AutoQAC.Services.UI;

internal static class FileDialogFilterMapper
{
    public static IReadOnlyList<string> BuildExtensionList(string filter)
    {
        var extensions = FileDialogFilterParser.Parse(filter)
            .SelectMany(entry => entry.Patterns)
            .Select(NormalizePattern)
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Distinct()
            .ToList();

        return extensions.Count > 0 ? extensions : ["*"];
    }

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildFileTypeChoices(string filter)
    {
        var entries = FileDialogFilterParser.Parse(filter);
        if (entries.Count == 0)
        {
            return new Dictionary<string, IReadOnlyList<string>>
            {
                ["All Files (*.*)"] = ["*"]
            };
        }

        return entries.ToDictionary(
            entry => entry.Name,
            entry => (IReadOnlyList<string>)entry.Patterns
                .Select(NormalizePattern)
                .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
                .Distinct()
                .DefaultIfEmpty("*")
                .ToList());
    }

    private static string NormalizePattern(string pattern)
    {
        var normalized = pattern.Trim();
        return normalized switch
        {
            "*.*" => "*",
            "*" => "*",
            _ when normalized.StartsWith("*.") => normalized[2..],
            _ when normalized.StartsWith('.') => normalized[1..],
            _ => normalized
        };
    }
}
