using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Core.CivilizationVII;

/// <summary>Enables the game's own local tuner without installing anything in its game directory.</summary>
public static class Civ7Setup
{
    public const string ReceiptRelativePath = "HappyCheats/CivilizationVII/setup.json";
    private const int MaximumOptionsBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    // VII ships a commented default: ;EnableTuner -1 (1 enabled, 2 disabled).
    // A single active override takes precedence over commented examples/defaults.
    private static readonly Regex SettingKey = new(@"^[\t ]*(?<comment>;[\t ]*)?EnableTuner(?:[\t =]|$)", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex Setting = new(@"^(?<indent>[\t ]*)(?<comment>;[\t ]*)?(?<key>EnableTuner)(?<separator>[\t ]+(?:=[\t ]*)?)(?<value>-1|[012])(?<suffix>[\t ]*(?:;[^\r\n]*)?)$", RegexOptions.CultureInvariant);
    private sealed record Receipt(int Schema, string Module, string OptionsPath, string OriginalLine, string InstalledLine, bool Owned, string Backup, string BackupSha256);
    private sealed record Options(byte[] Bytes, string Text, Encoding Encoding, byte[] Preamble, int LineStart, int LineLength, string Line, bool Enabled)
    {
        public byte[] WithLine(string line)
        {
            var text = Text.Remove(LineStart, LineLength).Insert(LineStart, line);
            return Preamble.Concat(Encoding.GetBytes(text)).ToArray();
        }
    }

    /// <summary>Only existing profiles are candidates; no profile or game is created or started.</summary>
    public static string FindOptions(IEnumerable<string>? profileDirectories = null)
    {
        profileDirectories ??= new[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Firaxis Games", "Sid Meier's Civilization VII")
        };
        var found = profileDirectories.Select(p => SafePath(p, "AppOptions.txt")).Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (found.Length == 0) throw new IOException("Civilization VII AppOptions.txt was not found. Launch the game once yourself, then close it before installing the trainer.");
        if (found.Length > 1) throw new IOException("More than one Civilization VII profile contains AppOptions.txt. Resolve the old/active profile ambiguity before enabling Tuner: " + string.Join("; ", found));
        return found[0];
    }

    /// <summary>Validates the entire package, accepting only the one bundled runtime.</summary>
    public static string ReadRuntime(string package)
    {
        var manifest = ReadJson(SafePath(package, "package.json"));
        if (Text(manifest, "app_id") != "1295660" || Text(manifest, "version") != CivilizationVIIModule.TrainerVersion)
            throw new IOException("The Civilization VII trainer package has an unsupported application or runtime version.");
        var files = Object(manifest, "files");
        if (files.ValueKind != JsonValueKind.Array || files.GetArrayLength() != 1)
            throw new IOException("The Civilization VII package must contain exactly runtime.js.");
        var file = files[0];
        var hash = Text(file, "sha256");
        if (Text(file, "path") != "runtime.js" || !Regex.IsMatch(hash, "^[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant))
            throw new IOException("The Civilization VII package contains an invalid runtime entry.");
        var path = SafePath(package, "runtime.js");
        if (!File.Exists(path) || new FileInfo(path).Length is <= 0 or > MaximumOptionsBytes)
            throw new IOException("The Civilization VII runtime is missing, empty, or unexpectedly large.");
        var bytes = File.ReadAllBytes(path);
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), hash, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Civilization VII runtime checksum failed. Reinstall HappyCheats before enabling Tuner.");
        return new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
    }

    /// <summary>
    /// Reads the saved Tuner setting without consulting HappyCheats ownership metadata.
    /// This is setup guidance only: an already-running game's authenticated local socket
    /// is the authority for connection, even if its profile cannot be located or inspected.
    /// </summary>
    public static bool IsTunerEnabled(string optionsPath)
    {
        var (path, _) = Resolve(optionsPath);
        return File.Exists(path) && ReadOptions(path).Enabled;
    }

    /// <summary>
    /// Indicates an enabled setup with a valid HappyCheats ownership receipt and backup.
    /// Ownership is required for safe removal, never for connecting to an existing Tuner.
    /// </summary>
    public static bool Installed(string optionsPath)
    {
        var (path, profile) = Resolve(optionsPath);
        if (!File.Exists(path) || !File.Exists(SafePath(profile, ReceiptRelativePath))) return false;
        return ReadReceipt(profile, path) != null && ReadOptions(path).Enabled;
    }

    public static void Install(string package, string optionsPath, Action checkGameClosed, IProgress<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(checkGameClosed);
        checkGameClosed();
        _ = ReadRuntime(package);
        var (path, profile) = Resolve(optionsPath);
        var original = ReadOptions(path);
        var previous = ReadReceipt(profile, path);
        if (previous != null) {
            if (original.Line != previous.InstalledLine)
                throw new IOException("EnableTuner changed after HappyCheats setup. The setting and backup were preserved; remove or inspect the previous setup before reinstalling.");
            progress?.Report("Civilization VII trainer is already enabled.");
            return;
        }

        var enabledLine = EnabledLine(Setting.Match(original.Line));
        var replacement = original.WithLine(enabledLine);
        var backupRelative = "HappyCheats/CivilizationVII/AppOptions.original." + Guid.NewGuid().ToString("N") + ".txt";
        var backup = SafePath(profile, backupRelative);
        var receiptPath = SafePath(profile, ReceiptRelativePath);
        var receiptStage = SafePath(profile, ReceiptRelativePath + "." + Guid.NewGuid().ToString("N") + ".tmp");
        var receipt = new Receipt(1, "civilization-vii", path, original.Line, enabledLine, !original.Enabled, backupRelative, Convert.ToHexString(SHA256.HashData(original.Bytes)));
        checkGameClosed();
        AssertUnchanged(path, original.Bytes);
        if (File.Exists(receiptPath)) throw new IOException("Civilization VII setup was changed by another operation. No setting was overwritten.");
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        using (var stream = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None)) stream.Write(original.Bytes);
        var changed = false;
        try {
            using (var stream = new FileStream(receiptStage, FileMode.CreateNew, FileAccess.Write, FileShare.None)) JsonSerializer.Serialize(stream, receipt);
            if (!original.Enabled) {
                AtomicReplace(path, original.Bytes, replacement, checkGameClosed);
                changed = true;
            }
            // Never overwrite a receipt created concurrently, even if its contents look familiar.
            File.Move(receiptStage, receiptPath);
        }
        catch {
            if (changed) {
                try { AtomicReplace(path, replacement, original.Bytes, checkGameClosed); }
                catch (Exception recovery) {
                    throw new IOException("Civilization VII setup could not complete or safely roll back. The original configuration is preserved at " + backup + ". " + recovery.Message, recovery);
                }
            }
            throw;
        }
        finally { if (File.Exists(receiptStage)) File.Delete(receiptStage); }
        progress?.Report("Civilization VII trainer enabled. Launch a single-player game to connect.");
    }

    public static void Remove(string optionsPath, Action checkGameClosed, IProgress<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(checkGameClosed);
        checkGameClosed();
        var (path, profile) = Resolve(optionsPath);
        var receiptPath = SafePath(profile, ReceiptRelativePath);
        var receiptBytes = File.Exists(receiptPath) ? File.ReadAllBytes(receiptPath) : null;
        var receipt = ReadReceipt(profile, path) ?? throw new IOException("No HappyCheats Civilization VII setup receipt was found. Existing settings were preserved.");
        var original = ReadOptions(path);
        if (receipt.Owned) {
            if (original.Line != receipt.InstalledLine)
                throw new IOException("EnableTuner changed after HappyCheats setup and was preserved. The original backup remains at " + SafePath(profile, receipt.Backup));
            AtomicReplace(path, original.Bytes, original.WithLine(receipt.OriginalLine), checkGameClosed);
        }
        else checkGameClosed();
        AssertUnchanged(receiptPath, receiptBytes!);
        File.Delete(receiptPath);
        progress?.Report(receipt.Owned ? "Civilization VII trainer removed; the original EnableTuner setting was restored. The configuration backup was kept." : "Civilization VII trainer removed. Your pre-existing Tuner setting and configuration backup were preserved.");
    }

    private static (string Path, string Profile) Resolve(string optionsPath)
    {
        var path = Path.GetFullPath(optionsPath);
        if (!Path.GetFileName(path).Equals("AppOptions.txt", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Select the existing Civilization VII AppOptions.txt profile file.");
        var profile = Path.GetDirectoryName(path)!;
        return (SafePath(profile, Path.GetFileName(path)), profile);
    }

    private static Options ReadOptions(string path)
    {
        if (!File.Exists(path)) throw new IOException("Civilization VII AppOptions.txt is missing. Launch the game once yourself and close it before setup; no profile was created.");
        if (new FileInfo(path).Length > MaximumOptionsBytes) throw new IOException("Civilization VII AppOptions.txt is unexpectedly large.");
        var bytes = File.ReadAllBytes(path);
        Encoding encoding = new UTF8Encoding(false, true);
        var preamble = Array.Empty<byte>();
        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) preamble = new byte[] { 0xEF, 0xBB, 0xBF };
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE })) { encoding = new UnicodeEncoding(false, false, true); preamble = new byte[] { 0xFF, 0xFE }; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF })) { encoding = new UnicodeEncoding(true, false, true); preamble = new byte[] { 0xFE, 0xFF }; }
        var text = encoding.GetString(bytes, preamble.Length, bytes.Length - preamble.Length);
        var keys = SettingKey.Matches(text).Cast<Match>().ToArray();
        var active = keys.Where(key => !key.Groups["comment"].Success).ToArray();
        var candidates = active.Length > 0 ? active : keys;
        if (candidates.Length != 1) throw new IOException("AppOptions.txt must contain one unambiguous EnableTuner setting. Missing or duplicate settings were preserved.");
        var start = candidates[0].Index;
        var end = text.IndexOf('\n', start);
        if (end < 0) end = text.Length;
        if (end > start && text[end - 1] == '\r') end--;
        var line = text[start..end];
        var match = Setting.Match(line);
        if (!match.Success) throw new IOException("AppOptions.txt has an unsupported EnableTuner value. The setting was preserved.");
        return new Options(bytes, text, encoding, preamble, start, end - start, line, Enabled(match));
    }

    private static bool Enabled(Match setting) => !setting.Groups["comment"].Success && setting.Groups["value"].Value == "1";
    private static string EnabledLine(Match setting) => setting.Groups["indent"].Value + setting.Groups["key"].Value
        + setting.Groups["separator"].Value + "1" + setting.Groups["suffix"].Value;

    private static Receipt? ReadReceipt(string profile, string optionsPath)
    {
        var path = SafePath(profile, ReceiptRelativePath);
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > MaximumOptionsBytes) throw new IOException("Civilization VII setup receipt is unexpectedly large.");
        var receipt = JsonSerializer.Deserialize<Receipt>(File.ReadAllText(path), Json);
        if (receipt == null || receipt.Schema != 1 || receipt.Module != "civilization-vii" ||
            !string.Equals(receipt.OptionsPath, optionsPath, StringComparison.OrdinalIgnoreCase) ||
            receipt.OriginalLine == null || receipt.InstalledLine == null || receipt.Backup == null || receipt.BackupSha256 == null ||
            !Regex.IsMatch(receipt.Backup, "^HappyCheats/CivilizationVII/AppOptions\\.original\\.[a-f0-9]{32}\\.txt$", RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(receipt.BackupSha256, "^[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant))
            throw new IOException("Civilization VII setup receipt is invalid. Existing settings were preserved.");
        var original = Setting.Match(receipt.OriginalLine);
        var installed = Setting.Match(receipt.InstalledLine);
        if (!original.Success || original.Length != receipt.OriginalLine.Length || !installed.Success || installed.Length != receipt.InstalledLine.Length || !Enabled(installed) ||
            receipt.Owned == Enabled(original) || receipt.InstalledLine != EnabledLine(original))
            throw new IOException("Civilization VII setup ownership record is invalid. Existing settings were preserved.");
        var backup = SafePath(profile, receipt.Backup);
        if (!File.Exists(backup) || !Hash(backup).Equals(receipt.BackupSha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Civilization VII setup backup is missing or modified. Existing settings were preserved.");
        return receipt;
    }

    private static void AssertUnchanged(string path, byte[] expected)
    {
        if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(expected))
            throw new IOException("Civilization VII configuration changed during setup and was preserved: " + path);
    }

    private static void AtomicReplace(string path, byte[] expected, byte[] replacement, Action checkGameClosed)
    {
        var stage = SafePath(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".happycheats-" + Guid.NewGuid().ToString("N") + ".tmp");
        try {
            using (var stream = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None)) stream.Write(replacement);
            checkGameClosed();
            AssertUnchanged(path, expected);
            File.Replace(stage, path, null);
        }
        finally { if (File.Exists(stage)) File.Delete(stage); }
    }
}
