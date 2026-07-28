using System.Text;

namespace SteamAchievements.Core.Diagnostics;

/// <summary>
/// The one stateful part of the log: an append-only file that rotates by size.
///
/// Synchronous, under a single lock, flushed on every write. A queue with a
/// background writer would be faster and would also lose exactly what this
/// exists to keep — the lines still in memory when the process dies. At
/// roughly six thousand lines per full sync there is no throughput problem to
/// solve.
/// </summary>
internal sealed class RollingFileWriter : IDisposable
{
    private readonly LogFileOptions _options;
    private readonly string _currentPath;
    private readonly string _stem;
    private readonly string _extension;
    private readonly Lock _gate = new();

    private FileStream? _stream;
    private long _length;

    public RollingFileWriter(LogFileOptions options)
    {
        _options = options;
        _currentPath = Path.Combine(options.Directory, options.FileName);
        _stem = Path.GetFileNameWithoutExtension(options.FileName);
        _extension = Path.GetExtension(options.FileName);
    }

    /// <summary>
    /// Set once the file has proved unwritable, and never cleared. Logging must
    /// never be the reason the application fails to start, and a writer that
    /// retried on every line would turn a permissions problem into a
    /// nine-minute stall during a sync.
    /// </summary>
    public bool Disabled { get; private set; }

    public void Write(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);

        lock (_gate)
        {
            if (Disabled)
            {
                return;
            }

            try
            {
                Open();

                // The _length > 0 test is what stops a line larger than the
                // whole budget from rotating on every write and filling the
                // folder with empty files.
                if (_length > 0 && _length + bytes.Length > _options.MaxBytes)
                {
                    Rotate();
                    Open();
                }

                _stream!.Write(bytes);
                _stream.Flush();
                _length += bytes.Length;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Disabled = true;
                Close();
            }
        }
    }

    private void Open()
    {
        if (_stream is not null)
        {
            return;
        }

        Directory.CreateDirectory(_options.Directory);

        // FileShare.ReadWrite so "Open log" can show the file while the
        // application is still writing to it.
        _stream = new FileStream(_currentPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _length = _stream.Length;
    }

    /// <summary>
    /// Shifts every rotated file up by one index and moves the current file
    /// into the vacated <c>.1</c> slot. Requires <c>MaxFiles &gt;= 2</c>, which
    /// <see cref="LogFileOptions"/> now enforces at construction — with fewer
    /// than two files there is no <c>.1</c> slot to move the current file
    /// into without colliding with a file this method never deletes.
    ///
    /// Order matters in both halves:
    /// <list type="number">
    /// <item>The true oldest file (index <c>MaxFiles - 1</c>) is deleted
    /// first, so it does not linger once nothing points at it.</item>
    /// <item>The shift then runs from the highest index down to 1, so each
    /// destination is vacated (moved out of) before anything moves into it —
    /// running it the other way would overwrite a file before it had been
    /// moved, silently dropping it instead of aging it out.</item>
    /// </list>
    /// </summary>
    private void Rotate()
    {
        Close();

        var oldest = Path.Combine(_options.Directory, $"{_stem}.{_options.MaxFiles - 1}{_extension}");

        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (var index = _options.MaxFiles - 2; index >= 1; index--)
        {
            var from = Path.Combine(_options.Directory, $"{_stem}.{index}{_extension}");

            if (File.Exists(from))
            {
                File.Move(from, Path.Combine(_options.Directory, $"{_stem}.{index + 1}{_extension}"));
            }
        }

        if (File.Exists(_currentPath))
        {
            File.Move(_currentPath, Path.Combine(_options.Directory, $"{_stem}.1{_extension}"));
        }

        _length = 0;
    }

    private void Close()
    {
        _stream?.Dispose();
        _stream = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            Close();
        }
    }
}
