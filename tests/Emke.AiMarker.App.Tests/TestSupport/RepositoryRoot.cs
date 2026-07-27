namespace Emke.AiMarker.App.Tests.TestSupport;

internal static class RepositoryRoot
{
    public static string Find()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Emke.AiMarker.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not locate Emke.AiMarker.sln from the test output directory.");
    }
}
