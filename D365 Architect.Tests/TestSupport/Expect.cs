using Xunit;

namespace D365Architect.Tests.TestSupport;

/// <summary>
/// Assertions that always say what the requirement expected and what was
/// seen, so a red test reads as a sentence about the behaviour rather than a
/// bare value mismatch. xunit's own <c>Assert.Equal</c>/<c>Assert.Contains</c>
/// have no message parameter, and the developer picking up a failing test
/// has only that message to go on.
/// </summary>
public static class Expect
{
    public static void Equal<T>(T expected, T actual, string because) =>
        Assert.True(EqualityComparer<T>.Default.Equals(expected, actual), $"{because} Expected: <{expected}>. Actual: <{actual}>.");

    public static void Contains(string expectedSubstring, string? actual, string because) =>
        Assert.True(actual is not null && actual.Contains(expectedSubstring, StringComparison.Ordinal), $"{because} Expected to find: <{expectedSubstring}>. Actual: <{actual}>.");

    public static void True(bool condition, string because) => Assert.True(condition, because);

    public static void Empty<T>(IReadOnlyCollection<T> items, string because) =>
        Assert.True(items.Count == 0, $"{because} Expected nothing, but found: <{string.Join(", ", items)}>.");

    public static void Sequence<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, string because) =>
        Assert.True(expected.SequenceEqual(actual), $"{because} Expected: <{string.Join(" | ", expected)}>. Actual: <{string.Join(" | ", actual)}>.");
}
