using SteamAchievements.Core.Diagnostics;

namespace SteamAchievements.Core.Tests.Diagnostics;

public class RollingFileWriterTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "satlog-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string Current => Path.Combine(_directory, "log.txt");

    private string Rotated(int index) => Path.Combine(_directory, $"log.{index}.txt");

    [Fact]
    public void CreatesTheDirectoryAndTheFileOnTheFirstWrite()
    {
        using var writer = new RollingFileWriter(new LogFileOptions(_directory));

        writer.Write("first\r\n");

        Assert.Equal("first\r\n", File.ReadAllText(Current));
    }

    [Fact]
    public void AppendsRatherThanTruncating()
    {
        using var writer = new RollingFileWriter(new LogFileOptions(_directory));

        writer.Write("one\r\n");
        writer.Write("two\r\n");

        Assert.Equal("one\r\ntwo\r\n", File.ReadAllText(Current));
    }

    [Fact]
    public void LeavesTheFileReadableWhileItIsStillOpen()
    {
        // "Open log" shows the file while the application is running, so the
        // writer must not hold an exclusive handle.
        using var writer = new RollingFileWriter(new LogFileOptions(_directory));

        writer.Write("live\r\n");

        using var reader = new FileStream(Current, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var text = new StreamReader(reader);
        Assert.Equal("live\r\n", text.ReadToEnd());
    }

    [Fact]
    public void RotatesWhenTheNextWriteWouldPassTheLimit()
    {
        using var writer = new RollingFileWriter(new LogFileOptions(_directory, MaxBytes: 10));

        writer.Write("123456\r\n");  // 8 bytes, fits
        writer.Write("789012\r\n");  // would reach 16, so rotate first

        Assert.Equal("123456\r\n", File.ReadAllText(Rotated(1)));
        Assert.Equal("789012\r\n", File.ReadAllText(Current));
    }

    [Fact]
    public void KeepsTheNewestLinesInTheCurrentFile()
    {
        using var writer = new RollingFileWriter(new LogFileOptions(_directory, MaxBytes: 10));

        writer.Write("oldest\r\n");
        writer.Write("newest\r\n");

        Assert.Contains("newest", File.ReadAllText(Current));
        Assert.DoesNotContain("newest", File.ReadAllText(Rotated(1)));
    }

    [Fact]
    public void KeepsAtMostMaxFilesAndDiscardsTheOldest()
    {
        using (var writer = new RollingFileWriter(new LogFileOptions(_directory, MaxBytes: 10, MaxFiles: 4)))
        {
            foreach (var n in Enumerable.Range(1, 8))
            {
                writer.Write($"line-{n}\r\n");
            }
        }

        Assert.Equal(4, Directory.GetFiles(_directory).Length);
        Assert.True(File.Exists(Current));
        Assert.True(File.Exists(Rotated(1)));
        Assert.True(File.Exists(Rotated(2)));
        Assert.True(File.Exists(Rotated(3)));
        Assert.False(File.Exists(Rotated(4)));
    }

    [Fact]
    public void WritesASingleLineLargerThanTheLimitRatherThanRotatingForever()
    {
        using var writer = new RollingFileWriter(new LogFileOptions(_directory, MaxBytes: 4));

        writer.Write("a line much longer than four bytes\r\n");

        Assert.Contains("much longer", File.ReadAllText(Current));
        Assert.False(File.Exists(Rotated(1)));
    }

    [Fact]
    public void DisablesItselfPermanentlyWhenTheFileCannotBeOpened()
    {
        // A file where the directory should be: opening anything inside it
        // fails, and no amount of retrying will change that.
        var blocker = Path.Combine(Path.GetTempPath(), "satlog-blocker-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(blocker, "not a directory");

        try
        {
            using var writer = new RollingFileWriter(new LogFileOptions(blocker));

            writer.Write("never lands\r\n");

            Assert.True(writer.Disabled);

            // The second write must not throw either.
            writer.Write("still nothing\r\n");
            Assert.True(writer.Disabled);
        }
        finally
        {
            File.Delete(blocker);
        }
    }

    [Fact]
    public void WritesFromManyThreadsWithoutLosingOrInterleavingALine()
    {
        const int threads = 8;
        const int perThread = 200;

        using (var writer = new RollingFileWriter(new LogFileOptions(_directory)))
        {
            Parallel.For(0, threads, thread =>
            {
                foreach (var n in Enumerable.Range(0, perThread))
                {
                    writer.Write($"thread-{thread}-line-{n}\r\n");
                }
            });
        }

        var lines = File.ReadAllLines(Current);

        Assert.Equal(threads * perThread, lines.Length);
        Assert.All(lines, line => Assert.Matches(@"^thread-\d-line-\d+$", line));
    }
}
