namespace SteamAchievements.Core.Local;

public sealed class VdfNode
{
    private static readonly VdfNode Empty = new();

    private readonly Dictionary<string, VdfNode> _children = new(StringComparer.OrdinalIgnoreCase);

    public string? Value { get; init; }

    public IReadOnlyDictionary<string, VdfNode> Children => _children;

    public VdfNode this[string key] =>
        _children.TryGetValue(key, out var child) ? child : Empty;

    internal void Add(string key, VdfNode child) => _children[key] = child;
}
