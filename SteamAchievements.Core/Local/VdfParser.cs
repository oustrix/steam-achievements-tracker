namespace SteamAchievements.Core.Local;

/// <summary>
/// Parser for Valve's KeyValues text format used by loginusers.vdf,
/// libraryfolders.vdf and appmanifest_*.acf.
/// </summary>
public static class VdfParser
{
    public static VdfNode Parse(string text)
    {
        var position = 0;
        var root = new VdfNode();

        while (true)
        {
            var key = ReadToken(text, ref position);
            if (key is null)
            {
                return root;
            }

            root.Add(key, ReadValue(text, ref position, key));
        }
    }

    private static VdfNode ReadValue(string text, ref int position, string key)
    {
        SkipTrivia(text, ref position);

        if (position < text.Length && text[position] == '{')
        {
            position++;
            var section = new VdfNode();

            while (true)
            {
                SkipTrivia(text, ref position);

                if (position >= text.Length)
                {
                    throw new FormatException($"Unbalanced braces in section '{key}'.");
                }

                if (text[position] == '}')
                {
                    position++;
                    return section;
                }

                var childKey = ReadToken(text, ref position)
                    ?? throw new FormatException($"Unbalanced braces in section '{key}'.");

                section.Add(childKey, ReadValue(text, ref position, childKey));
            }
        }

        var scalar = ReadToken(text, ref position)
            ?? throw new FormatException($"Key '{key}' has no value.");

        return new VdfNode { Value = scalar };
    }

    private static string? ReadToken(string text, ref int position)
    {
        SkipTrivia(text, ref position);

        if (position >= text.Length || text[position] != '"')
        {
            return null;
        }

        position++;
        var builder = new System.Text.StringBuilder();

        while (position < text.Length && text[position] != '"')
        {
            if (text[position] == '\\' && position + 1 < text.Length)
            {
                position++;
                builder.Append(text[position] switch
                {
                    'n' => '\n',
                    't' => '\t',
                    var other => other,
                });
            }
            else
            {
                builder.Append(text[position]);
            }

            position++;
        }

        position++;
        return builder.ToString();
    }

    private static void SkipTrivia(string text, ref int position)
    {
        while (position < text.Length)
        {
            if (char.IsWhiteSpace(text[position]))
            {
                position++;
            }
            else if (text[position] == '/' && position + 1 < text.Length && text[position + 1] == '/')
            {
                while (position < text.Length && text[position] != '\n')
                {
                    position++;
                }
            }
            else
            {
                return;
            }
        }
    }
}
