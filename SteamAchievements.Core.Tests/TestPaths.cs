namespace SteamAchievements.Core.Tests;

public static class TestPaths
{
    public static string Data(string fileName)
    {
        var directory = AppContext.BaseDirectory;

        while (directory is not null && !Directory.Exists(Path.Combine(directory, "testdata")))
        {
            directory = Directory.GetParent(directory)?.FullName;
        }

        if (directory is null)
        {
            throw new DirectoryNotFoundException("Could not locate the testdata directory.");
        }

        return Path.Combine(directory, "testdata", fileName);
    }
}
