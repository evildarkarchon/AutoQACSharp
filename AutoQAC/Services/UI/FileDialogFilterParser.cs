using System.Collections.Generic;
using System.Linq;

namespace AutoQAC.Services.UI;

public sealed record FileDialogFilterEntry(string Name, IReadOnlyList<string> Patterns);

public static class FileDialogFilterParser
{
    public static IReadOnlyList<FileDialogFilterEntry> Parse(string filter)
    {
        var result = new List<FileDialogFilterEntry>();
        var parts = filter.Split('|');

        for (var i = 0; i < parts.Length; i += 2)
        {
            if (i + 1 >= parts.Length)
            {
                break;
            }

            var name = parts[i];
            var patterns = parts[i + 1].Split(';').ToList();

            result.Add(new FileDialogFilterEntry(name, patterns));
        }

        return result;
    }
}
