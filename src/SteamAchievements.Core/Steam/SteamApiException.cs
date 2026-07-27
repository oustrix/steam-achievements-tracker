namespace SteamAchievements.Core.Steam;

public sealed class SteamApiException : Exception
{
    public SteamApiException(SteamApiErrorKind kind, int statusCode, string message)
        : base(message)
    {
        Kind = kind;
        StatusCode = statusCode;
    }

    public SteamApiErrorKind Kind { get; }

    public int StatusCode { get; }

    /// <summary>Retrying only makes sense for transient conditions.</summary>
    public bool IsTransient => Kind is SteamApiErrorKind.RateLimited or SteamApiErrorKind.ServerError;
}
