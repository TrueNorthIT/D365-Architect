namespace D365Architect.Tests.TestSupport;

/// <summary>Finds the repository's root folder (the one holding <c>D365 Architect.slnx</c>) from the test assembly's location, for the tests that read the project file or the committed schema files.</summary>
internal static class RepositoryRoot
{
    public static string Path { get; } = Find();

    private static string Find()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, "D365 Architect.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"Couldn't find the repository root (a folder holding 'D365 Architect.slnx') above '{AppContext.BaseDirectory}'.");
    }
}
