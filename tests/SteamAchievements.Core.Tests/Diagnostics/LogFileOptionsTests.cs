using SteamAchievements.Core.Diagnostics;

namespace SteamAchievements.Core.Tests.Diagnostics;

public class LogFileOptionsTests
{
    [Fact]
    public void ConstructsSuccessfullyWithOnlyADirectory()
    {
        var options = new LogFileOptions("/tmp/some-log-directory");

        Assert.Equal("log.txt", options.FileName);
        Assert.Equal(2 * 1024 * 1024, options.MaxBytes);
        Assert.Equal(4, options.MaxFiles);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ThrowsWhenTheDirectoryIsBlank(string? directory)
    {
        Assert.Throws<ArgumentException>(() => new LogFileOptions(directory!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ThrowsWhenMaxBytesIsNotPositive(int maxBytes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LogFileOptions("/tmp/some-log-directory", MaxBytes: maxBytes));
    }

    [Fact]
    public void ThrowsWhenMaxFilesIsOne()
    {
        // The rotation scheme in RollingFileWriter cannot express a single
        // rotated file: MaxFiles = 1 disables the writer permanently on the
        // second rotation instead of failing here. Kept as its own Fact,
        // separate from the Theory below, because that boundary is a distinct
        // claim from "not positive" — it is the smallest value that passes
        // this constructor's check yet still cannot work.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LogFileOptions("/tmp/some-log-directory", MaxFiles: 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ThrowsWhenMaxFilesIsNotPositive(int maxFiles)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LogFileOptions("/tmp/some-log-directory", MaxFiles: maxFiles));
    }
}
