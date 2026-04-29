using System;
using System.IO;
using AutoQAC.Services.Backup;
using FluentAssertions;

namespace AutoQAC.Tests.Services.Backup;

/// <summary>
/// Unit tests for <see cref="BackupPathContainment.IsContained"/>: the shared string-level
/// containment helper consumed by BackupService (restore target containment) and
/// RestoreViewModel (Delete Session containment, Plan 07-13). Locks the canonical
/// containment policy in one place to prevent divergent behavior across safety boundaries.
/// </summary>
public sealed class BackupPathContainmentTests : IDisposable
{
    // Per-instance temp root keeps tests isolated across xUnit's parallel test execution.
    private readonly string _testRoot = Path.Combine(
        Path.GetTempPath(),
        $"BackupPathContainment_{Guid.NewGuid():N}");

    public BackupPathContainmentTests()
    {
        Directory.CreateDirectory(_testRoot);
    }

    [Fact]
    public void IsContained_NullCandidate_ReturnsFalse()
    {
        BackupPathContainment.IsContained(null, _testRoot)
            .Should().BeFalse("null candidate paths must fail closed");
    }

    [Fact]
    public void IsContained_EmptyCandidate_ReturnsFalse()
    {
        BackupPathContainment.IsContained("", _testRoot)
            .Should().BeFalse("empty candidate paths must fail closed");
    }

    [Fact]
    public void IsContained_WhitespaceCandidate_ReturnsFalse()
    {
        BackupPathContainment.IsContained("   ", _testRoot)
            .Should().BeFalse("whitespace-only candidate paths must fail closed");
    }

    [Fact]
    public void IsContained_NullRoot_ReturnsFalse()
    {
        BackupPathContainment.IsContained(Path.Combine(_testRoot, "Sub"), null)
            .Should().BeFalse("null root paths must fail closed");
    }

    [Fact]
    public void IsContained_EmptyRoot_ReturnsFalse()
    {
        BackupPathContainment.IsContained(Path.Combine(_testRoot, "Sub"), "")
            .Should().BeFalse("empty root paths must fail closed");
    }

    [Fact]
    public void IsContained_WhitespaceRoot_ReturnsFalse()
    {
        BackupPathContainment.IsContained(Path.Combine(_testRoot, "Sub"), "   ")
            .Should().BeFalse("whitespace-only root paths must fail closed");
    }

    [Fact]
    public void IsContained_ValidContainment_ReturnsTrue()
    {
        var candidate = Path.Combine(_testRoot, "child", "file.esp");

        BackupPathContainment.IsContained(candidate, _testRoot)
            .Should().BeTrue("nested paths under the root must be contained");
    }

    [Fact]
    public void IsContained_TraversalNormalizationEscapesRoot_ReturnsFalse()
    {
        // _testRoot/Inside/../../Outside resolves to a sibling of _testRoot via Path.GetFullPath
        // normalization, so containment must reject it.
        var candidate = Path.Combine(_testRoot, "Inside", "..", "..", "Outside", "file.esp");

        BackupPathContainment.IsContained(candidate, _testRoot)
            .Should().BeFalse("paths whose Path.GetFullPath result escapes the root must fail closed");
    }

    [Fact]
    public void IsContained_SiblingPrefixCollision_ReturnsFalse()
    {
        // Root: _testRoot/Backups
        // Candidate: _testRoot/Backups 2/2026-01-01_10-00-00 (shares the "Backups" prefix but is a sibling directory).
        // Trailing-separator normalization is what makes this rejection possible.
        var root = Path.Combine(_testRoot, "Backups");
        var candidate = Path.Combine(_testRoot, "Backups 2", "2026-01-01_10-00-00");

        BackupPathContainment.IsContained(candidate, root)
            .Should().BeFalse("sibling-prefix paths must be rejected via trailing-separator normalization");
    }

    [Fact]
    public void IsContained_CaseInsensitiveContainment_ReturnsTrue()
    {
        // Windows paths must compare case-insensitively. Mixed casing of the same directory must
        // still register as contained.
        var lowerRoot = _testRoot.ToLowerInvariant();
        var mixedCandidate = Path.Combine(_testRoot.ToUpperInvariant(), "Child", "file.esp");

        BackupPathContainment.IsContained(mixedCandidate, lowerRoot)
            .Should().BeTrue("Windows paths must compare case-insensitively (StringComparison.OrdinalIgnoreCase)");
    }

    [Fact]
    public void IsContained_MalformedPath_ReturnsFalse()
    {
        // Null character causes Path.GetFullPath to throw ArgumentException on Windows; the helper
        // must swallow that and fail closed rather than letting the exception bubble up to callers.
        var malformed = "\0invalid\0path";

        BackupPathContainment.IsContained(malformed, _testRoot)
            .Should().BeFalse("malformed paths that throw during normalization must be swallowed and fail closed");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup; xUnit may run before AV/indexer releases the temp dir handle.
        }
    }
}
