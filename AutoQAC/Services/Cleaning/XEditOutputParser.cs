using System.Collections.Generic;
using System.Text.RegularExpressions;
using AutoQAC.Models;

namespace AutoQAC.Services.Cleaning;

public interface IXEditOutputParser
{
    CleaningStatistics ParseOutput(List<string> outputLines);
    bool IsCompletionLine(string line);
}

public sealed partial class XEditOutputParser : IXEditOutputParser
{
    [GeneratedRegex(@"Undeleting:\s*(.*)")]
    private static partial Regex UndeletedPattern();

    [GeneratedRegex(@"Removing:\s*(.*)")]
    private static partial Regex RemovedPattern();

    [GeneratedRegex(@"Skipping:\s*(.*)")]
    private static partial Regex SkippedPattern();

    [GeneratedRegex(@"Making Partial Form:\s*(.*)")]
    private static partial Regex PartialFormsPattern();

    public CleaningStatistics ParseOutput(List<string> outputLines)
    {
        int undeleted = 0;
        int removed = 0;
        int skipped = 0;
        int partialForms = 0;

        foreach (var line in outputLines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (UndeletedPattern().IsMatch(line)) undeleted++;
            else if (RemovedPattern().IsMatch(line)) removed++;
            else if (SkippedPattern().IsMatch(line)) skipped++;
            else if (PartialFormsPattern().IsMatch(line)) partialForms++;
        }

        return new CleaningStatistics
        {
            ItemsRemoved = removed,
            ItemsUndeleted = undeleted,
            ItemsSkipped = skipped,
            PartialFormsCreated = partialForms
        };
    }

    public bool IsCompletionLine(string line)
    {
        return line.Contains("Done.") ||
               line.Contains("Cleaning completed");
    }
}
