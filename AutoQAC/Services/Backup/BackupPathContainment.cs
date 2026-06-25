using System;
using System.IO;

namespace AutoQAC.Services.Backup;

/// <summary>
/// Shared string-level path-containment helper used by backup, restore, and delete-session
/// safety boundaries. Encapsulates the canonical normalize-with-trailing-separator policy so
/// BackupService (restore target containment) and RestoreViewModel (Delete Session containment,
/// Plan 07-13) cannot diverge.
/// </summary>
/// <remarks>
/// Does not resolve NTFS reparse points or symlinks; callers still constrain metadata
/// before filesystem writes. This is intentional and documented in
/// <c>.planning/STATE.md</c> (Phase 7 decision: "Restore target containment is string-level
/// Path.GetFullPath validation and does not resolve NTFS reparse points or symlinks").
/// </remarks>
internal static class BackupPathContainment
{
    /// <summary>
    /// Performs string-level containment validation for backup/restore/delete safety boundaries.
    /// Returns true when both <paramref name="candidatePath"/> and <paramref name="rootPath"/> normalize
    /// successfully and the normalized candidate has the normalized root as a directory prefix using
    /// <see cref="StringComparison.OrdinalIgnoreCase"/>.
    /// </summary>
    /// <param name="candidatePath">Path that must remain inside <paramref name="rootPath"/>.</param>
    /// <param name="rootPath">Trust boundary root.</param>
    /// <returns>
    /// True when contained; false for null/empty/whitespace inputs, traversal-normalized paths
    /// that escape the root, sibling-prefix paths (e.g., "Backups 2" vs "Backups"), or malformed
    /// paths that throw <see cref="ArgumentException"/>, <see cref="IOException"/>,
    /// <see cref="NotSupportedException"/>, or <see cref="UnauthorizedAccessException"/> during
    /// normalization.
    /// </returns>
    public static bool IsContained(string? candidatePath, string? rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || string.IsNullOrWhiteSpace(candidatePath))
        {
            return false;
        }

        try
        {
            var normalizedRoot = EnsureTrailingDirectorySeparator(Path.GetFullPath(rootPath));
            var normalizedCandidate = Path.GetFullPath(candidatePath);
            return normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException
                                       or UnauthorizedAccessException)
        {
            // Malformed paths must fail closed without leaking exception detail to callers; the
            // calling site (BackupService / RestoreViewModel) is responsible for any logging it
            // considers safe to surface.
            return false;
        }
    }

    /// <summary>
    /// Ensures string-prefix containment compares against a directory boundary instead of a
    /// similarly named sibling (e.g., "Backups 2" must not be considered inside "Backups").
    /// </summary>
    private static string EnsureTrailingDirectorySeparator(string path) =>
        Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;
}
