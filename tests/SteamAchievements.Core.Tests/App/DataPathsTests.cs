using SteamAchievements.Core.App;

namespace SteamAchievements.Core.Tests.App;

public class DataPathsTests
{
    [Fact]
    public void PutsBothFilesInsideOneApplicationFolder()
    {
        var paths = DataPaths.Resolve(Path.Combine("C:", "Users", "someone", "AppData", "Local"));

        Assert.Equal(paths.Folder, Path.GetDirectoryName(paths.DatabaseFile));
        Assert.Equal(paths.Folder, Path.GetDirectoryName(paths.SecretFile));
    }

    [Fact]
    public void NamesTheFolderAfterTheApplication()
    {
        var paths = DataPaths.Resolve("/tmp/base");

        Assert.Equal("SteamAchievementsTracker", Path.GetFileName(paths.Folder));
    }

    [Fact]
    public void NamesTheTwoFiles()
    {
        var paths = DataPaths.Resolve("/tmp/base");

        Assert.Equal("library.db", Path.GetFileName(paths.DatabaseFile));
        Assert.Equal("apikey.bin", Path.GetFileName(paths.SecretFile));
    }

    [Fact]
    public void PutsTheLogBesideTheDatabase()
    {
        var paths = DataPaths.Resolve(Path.Combine("base", "dir"));

        Assert.Equal(Path.Combine(paths.Folder, "log.txt"), paths.LogFile);
    }

    [Fact]
    public void RejectsAnEmptyBaseDirectory()
    {
        Assert.Throws<ArgumentException>(() => DataPaths.Resolve("   "));
    }

    [Fact]
    public void CreatesTheFolderOnDemandAndDoesNotMindItAlreadyExisting()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var paths = DataPaths.Resolve(root);

            paths.EnsureFolderExists();
            paths.EnsureFolderExists();

            Assert.True(Directory.Exists(paths.Folder));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
