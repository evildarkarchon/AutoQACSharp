using System.Collections;
using Mutagen.Bethesda.Plugins;
using QueryPlugins.Models;

namespace QueryPlugins.Tests.Models;

public sealed class PluginAnalysisResultTests
{
    [Fact]
    public void Constructor_EnumeratesIssuesOnceWhileComputingCounts()
    {
        var issues = new TrackingIssueList(
        [
            new PluginIssue(FormKey.Null, null, IssueType.ItmRecord),
            new PluginIssue(FormKey.Null, null, IssueType.ItmRecord),
            new PluginIssue(FormKey.Null, null, IssueType.DeletedReference),
            new PluginIssue(FormKey.Null, null, IssueType.DeletedNavmesh),
        ]);

        var result = new PluginAnalysisResult(issues);

        result.ItmCount.Should().Be(2);
        result.DeletedReferenceCount.Should().Be(1);
        result.DeletedNavmeshCount.Should().Be(1);
        issues.EnumerationCount.Should().Be(1);
    }

    private sealed class TrackingIssueList(IReadOnlyList<PluginIssue> items) : IReadOnlyList<PluginIssue>
    {
        public int EnumerationCount { get; private set; }

        public PluginIssue this[int index] => items[index];

        public int Count => items.Count;

        public IEnumerator<PluginIssue> GetEnumerator()
        {
            EnumerationCount++;
            return items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
