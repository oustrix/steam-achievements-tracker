using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace SteamAchievements.Core.Presentation;

/// <summary>
/// The try/catch/log wrapper every screen repeats around its reaction to a
/// cross-cutting signal — <c>LibraryChangeSignal</c>, <c>IOnboarding.Changed</c>,
/// <c>IAccountAdmin.Changed</c>, <c>ISyncPresenter.Changed</c>. Each handler is
/// fire-and-forget: it runs inside an <c>InvokeAsync(() => ...)</c> whose
/// returned <c>Task</c> nothing awaits, so an exception that escapes it is not
/// merely uncaught — it is a screen that quietly stops updating, with no
/// message and no crash to say why. Both entry points below exist to make that
/// impossible while still surfacing the failure.
///
/// Neither method rethrows. Both report whether the body completed so the
/// caller can decide whether <c>StateHasChanged</c> should run — calling it
/// after a caught exception would render a screen that only partially updated.
/// </summary>
public static class ChangeHandlerGuard
{
    /// <summary>
    /// Guards a re-read of the SQLite-backed store that follows
    /// <c>LibraryChangeSignal.Changed</c>. That signal fires right after a
    /// sync has finished writing, so the re-read can race the writer's own
    /// connection closing out its transaction. SQLite reports that race as
    /// <c>SqliteErrorCode</c> 5 (SQLITE_BUSY) or 6 (SQLITE_LOCKED) — the
    /// previous data stays on screen and the next signal tries again, so this
    /// is logged as a warning, not swallowed as an error. Every other code is
    /// not this race — a broken query, a corrupt file, a full disk — and must
    /// not be hidden behind stale data forever, so it is logged as an error.
    /// </summary>
    /// <param name="log">The screen's own logger.</param>
    /// <param name="noun">
    /// What was being re-read, for the log line — "queue", "summary",
    /// "history", "settings".
    /// </param>
    /// <param name="reread">The re-read itself.</param>
    /// <returns><c>true</c> if <paramref name="reread"/> completed without throwing.</returns>
    public static bool Reread(ILogger log, string noun, Action reread)
    {
        try
        {
            reread();
        }
        catch (SqliteException e) when (e.SqliteErrorCode is 5 or 6)
        {
            log.LogWarning(e, "{Noun} re-read lost a race with the writer (code {Code})", noun, e.SqliteErrorCode);

            return false;
        }
        catch (Exception e)
        {
            log.LogError(e, "{Noun} re-read failed", noun);

            return false;
        }

        return true;
    }

    /// <summary>
    /// Guards a reaction that is not a SQLite re-read — recomputing a gate,
    /// redirecting a route, re-rendering. There is no writer race to
    /// distinguish here, only the same fire-and-forget hazard <see cref="Reread"/>
    /// guards against: nothing awaits the enclosing <c>Task</c>, so an
    /// exception left uncaught here is simply lost.
    /// </summary>
    /// <param name="log">The screen's own logger.</param>
    /// <param name="failureMessage">
    /// The log line if <paramref name="action"/> throws, e.g. "reacting to a
    /// preference change failed".
    /// </param>
    /// <param name="action">The reaction itself.</param>
    /// <returns><c>true</c> if <paramref name="action"/> completed without throwing.</returns>
    public static bool Guard(ILogger log, string failureMessage, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            log.LogError(e, failureMessage);

            return false;
        }

        return true;
    }
}
