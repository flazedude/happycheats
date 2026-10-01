using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace HappyCheats.Core.EldenRing;

/// <summary>The one reviewed, experimental executable layout. No game binary is distributed.</summary>
public sealed class EldenRingProfile
{
    public const string GameVersion = "1.17.1";
    public const string FileVersion = "2.7.1.0";
    internal const long WorldRva = 0x3D69FF8, GameRva = 0x3D6D988, SessionRva = 0x3D7E540,
        GameDataRva = 0x3D61F98, PlayerVtableRva = 0x2A7FBB0;
    internal const string GamePattern = "48 8B 05 ?? ?? ?? ?? 80 B8 ?? ?? ?? ?? 0D 0F 94 C0 C3";
    internal const string DataPattern = "48 8B 05 ?? ?? ?? ?? 48 85 C0 74 05 48 8B 40 58 C3 C3";

    private EldenRingProfile() { }
    public static EldenRingProfile Load(string packagePath)
    {
        try { return LoadCore(packagePath); }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException)
        { throw new IOException("Invalid Elden Ring package metadata.", e); }
    }
    private static EldenRingProfile LoadCore(string packagePath)
    {
        var path = Path.GetFullPath(packagePath);
        string FilePath(string relative)
        {
            var result = ModuleIO.SafePath(path, relative);
            if (new FileInfo(result).Length > 1024 * 1024) throw new IOException("Elden Ring package metadata is too large.");
            return result;
        }
        using var package = JsonDocument.Parse(File.ReadAllText(FilePath("package.json")));
        var root = package.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("game").GetString() != "elden-ring"
            || root.GetProperty("version").GetString() != "0.1.0")
            throw new IOException("Invalid Elden Ring package.");
        var files = root.GetProperty("files").EnumerateArray().ToArray();
        var expected = new HashSet<string>(["profile.json", "ThirdPartyNotices.txt"], StringComparer.Ordinal);
        foreach (var file in files)
        {
            var relative = file.GetProperty("path").GetString() ?? "";
            if (!expected.Remove(relative)) throw new IOException("Invalid Elden Ring package file list.");
            using var stream = File.OpenRead(FilePath(relative));
            if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(file.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Elden Ring package verification failed.");
        }
        if (expected.Count != 0) throw new IOException("Incomplete Elden Ring package.");
        using var profile = JsonDocument.Parse(File.ReadAllText(FilePath("profile.json")));
        var p = profile.RootElement;
        // The data file identifies the reviewed implementation; arbitrary offset overrides are deliberately unsupported.
        if (p.GetProperty("schemaVersion").GetInt32() != 1 || p.GetProperty("id").GetString() != "elden-ring-ww-2.7.1.0-resources-v1"
            || p.GetProperty("fileVersion").GetString() != FileVersion || p.GetProperty("gameVersion").GetString() != GameVersion
            || p.GetProperty("gameplayVerified").GetBoolean()) throw new IOException("Unsupported Elden Ring profile.");
        return new EldenRingProfile();
    }

    public void VerifyExecutable(string path)
    {
        if (!Path.GetFileName(path).Equals("eldenring.exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            throw new IOException("Choose the Elden Ring Game folder containing eldenring.exe.");
        var version = FileVersionInfo.GetVersionInfo(path);
        if (version.FileMajorPart != 2 || version.FileMinorPart != 7 || version.FileBuildPart != 1 || version.FilePrivatePart != 0)
            throw new IOException("Unsupported Elden Ring executable. This experimental profile requires worldwide 1.17.1 (file 2.7.1.0).");
    }
}
