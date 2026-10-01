using System.Globalization;

namespace HappyCheats.CrimsonDesert;

/// <summary>An immutable byte pattern. Tokens are hexadecimal bytes or whole-byte wildcards.</summary>
public sealed class BinaryPattern
{
    private readonly byte[] bytes;
    private readonly bool[] wildcard;
    private readonly int anchor;

    private BinaryPattern(byte[] bytes, bool[] wildcard)
    {
        this.bytes = bytes;
        this.wildcard = wildcard;
        anchor = Array.FindIndex(wildcard, value => !value);
    }

    public int Length => bytes.Length;

    /// <summary>Parse whitespace-separated bytes, with ? or ?? matching one arbitrary byte.</summary>
    public static BinaryPattern Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) throw new FormatException("A byte pattern cannot be empty.");
        var bytes = new byte[tokens.Length];
        var wildcard = new bool[tokens.Length];
        for (var i = 0; i < tokens.Length; i++)
        {
            if (tokens[i] is "?" or "??") wildcard[i] = true;
            else if (tokens[i].Length != 2 || !byte.TryParse(tokens[i], NumberStyles.AllowHexSpecifier,
                         CultureInfo.InvariantCulture, out bytes[i]))
                throw new FormatException($"Invalid byte pattern token at position {i + 1}: '{tokens[i]}'.");
        }
        return new BinaryPattern(bytes, wildcard);
    }

    /// <summary>Find every complete match, including overlapping matches, relative to this span.</summary>
    public List<int> FindAll(ReadOnlySpan<byte> data)
    {
        var matches = new List<int>();
        var last = data.Length - bytes.Length;
        for (var start = 0; start <= last; start++)
        {
            if (anchor >= 0 && data[start + anchor] != bytes[anchor]) continue;
            var matched = true;
            for (var i = 0; i < bytes.Length; i++)
            {
                if (!wildcard[i] && data[start + i] != bytes[i])
                {
                    matched = false;
                    break;
                }
            }
            if (matched) matches.Add(start);
        }
        return matches;
    }
}
