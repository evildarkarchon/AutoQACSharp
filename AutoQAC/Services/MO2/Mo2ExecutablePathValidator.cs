using System;
using System.IO;

namespace AutoQAC.Services.MO2;

internal static class Mo2ExecutablePathValidator
{
    private const string ExpectedFileName = "ModOrganizer.exe";

    internal static bool IsValidExecutablePath(string? path) =>
        File.Exists(path) && IsModOrganizerExecutableName(path);

    private static bool IsModOrganizerExecutableName(string? path) =>
        string.Equals(Path.GetFileName(path), ExpectedFileName, StringComparison.OrdinalIgnoreCase);
}
