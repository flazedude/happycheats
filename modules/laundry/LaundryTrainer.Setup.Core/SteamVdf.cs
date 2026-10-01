using System.Text;

namespace LaundryTrainer.Setup.Core;

public sealed record SteamManifest(string? AppId, string InstallDirectory, string? StateFlags,
    long? SizeOnDisk = null, long? BytesToDownload = null, long? BytesDownloaded = null,
    long? BytesToStage = null, long? BytesStaged = null)
{
    public bool IsComplete => GameInspector.IsFinishedState(StateFlags) && SizeOnDisk > 0 &&
        (BytesToDownload ?? 0) <= (BytesDownloaded ?? 0) && (BytesToStage ?? 0) <= (BytesStaged ?? 0);
}

/// <summary>Small Valve KeyValues reader; understands nested objects, comments, and escaped strings.</summary>
public static class SteamVdf
{
    public static IReadOnlyList<string> ReadLibraryPaths(string text)
    {
        var document = new Parser(text).Parse();
        var root = Object(document, "libraryfolders");
        var paths = new List<string>();
        foreach (var item in root)
        {
            if (!uint.TryParse(item.Key, out _)) continue;
            var value = item.Value ?? (item.Children is null ? null : Scalar(item.Children, "path"));
            if (!string.IsNullOrWhiteSpace(value)) paths.Add(value);
        }
        return paths;
    }

    public static SteamManifest ReadAppManifest(string text)
    {
        var document = new Parser(text).Parse();
        var root = Object(document, "AppState");
        var install = Scalar(root, "installdir");
        if (string.IsNullOrWhiteSpace(install)) throw new IOException("Steam 安装清单缺少 installdir 值。");
        return new SteamManifest(Scalar(root, "appid"), install, Scalar(root, "StateFlags"),
            Number(root, "SizeOnDisk"), Number(root, "BytesToDownload"), Number(root, "BytesDownloaded"),
            Number(root, "BytesToStage"), Number(root, "BytesStaged"));
    }

    private static long? Number(List<Node> nodes, string key)
    {
        var text = Scalar(nodes, key);
        if (text is null) return null;
        if (!long.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value))
            throw new IOException($"Steam 安装清单中的 {key} 值无效。");
        return value;
    }

    private static List<Node> Object(List<Node> nodes, string key)
    {
        var matches = nodes.Where(n => n.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1 || matches[0].Children is null)
            throw new IOException($"Steam VDF 必须包含且仅包含一个 {key} 对象。");
        return matches[0].Children!;
    }

    private static string? Scalar(List<Node> nodes, string key)
    {
        var matches = nodes.Where(n => n.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length > 1 || matches.Length == 1 && matches[0].Children is not null)
            throw new IOException($"Steam VDF 中的 {key} 值重复或类型不正确。");
        return matches.FirstOrDefault()?.Value;
    }

    private sealed record Node(string Key, string? Value, List<Node>? Children);

    private sealed class Parser
    {
        private readonly string text;
        private int position;

        internal Parser(string text)
        {
            if (text.Length > 4 * 1024 * 1024) throw new IOException("Steam VDF 文件超出支持的大小。");
            this.text = text;
        }

        internal List<Node> Parse() => ParseObject(false, 0);

        private List<Node> ParseObject(bool nested, int depth)
        {
            if (depth > 32) throw new IOException("Steam VDF 的嵌套层级过多。");
            var nodes = new List<Node>();
            while (true)
            {
                SkipWhitespace();
                if (position == text.Length)
                {
                    if (nested) throw new IOException("Steam VDF 中的对象未完整结束。");
                    return nodes;
                }
                if (text[position] == '}')
                {
                    if (!nested) throw new IOException("Steam VDF 包含多余的右花括号。");
                    position++;
                    return nodes;
                }
                var key = ReadToken();
                SkipWhitespace();
                if (position < text.Length && text[position] == '{')
                {
                    position++;
                    nodes.Add(new Node(key, null, ParseObject(true, depth + 1)));
                }
                else
                    nodes.Add(new Node(key, ReadToken(), null));
            }
        }

        private string ReadToken()
        {
            SkipWhitespace();
            if (position >= text.Length || text[position] is '{' or '}')
                throw new IOException("Steam VDF 缺少键或值。");
            var result = new StringBuilder();
            if (text[position] != '"')
            {
                while (position < text.Length && !char.IsWhiteSpace(text[position]) && text[position] is not '{' and not '}')
                    result.Append(text[position++]);
                return result.ToString();
            }
            position++;
            while (position < text.Length)
            {
                var character = text[position++];
                if (character == '"') return result.ToString();
                if (character == '\\' && position < text.Length)
                {
                    var escaped = text[position++];
                    switch (escaped)
                    {
                        case '\\': result.Append('\\'); break;
                        case '"': result.Append('"'); break;
                        case 'n': result.Append('\n'); break;
                        case 'r': result.Append('\r'); break;
                        case 't': result.Append('\t'); break;
                        default: result.Append('\\').Append(escaped); break;
                    }
                }
                else result.Append(character);
            }
            throw new IOException("Steam VDF 中的字符串缺少结束引号。");
        }

        private void SkipWhitespace()
        {
            while (position < text.Length)
            {
                if (char.IsWhiteSpace(text[position]) || text[position] == '\uFEFF') { position++; continue; }
                if (text[position] == '/' && position + 1 < text.Length && text[position + 1] == '/')
                {
                    while (position < text.Length && text[position] != '\n') position++;
                    continue;
                }
                break;
            }
        }
    }
}
