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

    [Fact]
    public void ThrowsWhenMaxBytesIsZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LogFileOptions("/tmp/some-log-directory", MaxBytes: 0));
    }

    [Fact]
    public void ThrowsWhenMaxBytesIsNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LogFileOptions("/tmp/some-log-directory", MaxBytes: -1));
    }

    [Fact]
    public void ThrowsWhenMaxFilesIsOne()
    {
        // The rotation scheme in RollingFileWriter cannot express a single
        // rotated file: MaxFiles = 1 disables the writer permanently on the
        // second rotation instead of failing here.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LogFileOptions("/tmp/some-log-directory", MaxFiles: 1));
    }

    [Fact]
    public void ThrowsWhenMaxFilesIsZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LogFileOptions("/tmp/some-log-directory", MaxFiles: 0));
    }

    [Fact]
    public void ThrowsWhenMaxFilesIsNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LogFileOptions("/tmp/some-log-directory", MaxFiles: -1));
    }
}
