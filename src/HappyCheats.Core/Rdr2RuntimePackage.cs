using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Core;

/// <summary>Downloads the author's unchanged Story Mode prerequisites on demand; no runtime is redistributed.</summary>
public static class Rdr2RuntimePackage
{
    public const string DownloadUrl = "https://dev-c.com/files/ScriptHookRDR2_1.0.1491.17.zip";
    public const string AuthorPage = "https://www.dev-c.com/rdr2/scripthookrdr2/";
    public const string ArchiveSha256 = "A3BE69DCD33E6CFFE316D7C17EE5C4F7FCEDF0C7B7FA8CF68177398E6594C39F";
    public const string HookPath = "ScriptHookRDR2.dll";
    public const string LoaderPath = "dinput8.dll";
    public const string ReadmePath = "HappyCheatsRdr2/ScriptHookRDR2-readme.txt";
    public const string HookSha256 = "3AC29FBE8C92B664E358F7D4F0AF2EC9F1CA674885975087EF76BD98BF972A4C";
    public const string LoaderSha256 = "956FB3765572D00F6C08BCAE11E9856A00A68107464A87B6CCC6C1FFED46B88A";
    public const string ReadmeSha256 = "9CD1476DB98143936BA632ED9A25F076EF9953197BEE32C23BD06A5AA4EBAD58";
    public const string ConflictMessage = "An existing Script Hook RDR2 or ASI loader differs from the verified runtime. Existing files were preserved. Move the conflicting files to a backup folder before choosing Install / update.";
    internal sealed record Component(string ArchivePath, string Path, int Length, string Sha256);
    internal sealed record Specification(long ArchiveLength, string Sha256, IReadOnlyDictionary<string, long> Entries, IReadOnlyList<Component> Components);
    internal static readonly Specification Official = new(294614, ArchiveSha256,
        new Dictionary<string, long>(StringComparer.Ordinal) {
            ["bin/"] = 0, ["bin/dinput8.dll"] = 131072, ["bin/NativeTrainer.asi"] = 401408,
            ["bin/ScriptHookRDR2.dll"] = 185344, ["readme.txt"] = 3922, ["www.dev-c.com.url"] = 62
        }, new[] {
            new Component("bin/ScriptHookRDR2.dll", HookPath, 185344, HookSha256),
            new Component("bin/dinput8.dll", LoaderPath, 131072, LoaderSha256),
            new Component("readme.txt", ReadmePath, 3922, ReadmeSha256)
        });

    public enum State { Missing, Ready, Conflict }
    public sealed record Installation(State Status, string Message) { public bool NeedsDownload => Status == State.Missing; }
    internal sealed record RuntimeFile(string Path, byte[] Bytes, string Sha256);
    public sealed class VerifiedRuntime
    {
        internal IReadOnlyList<RuntimeFile> Files { get; }
        internal VerifiedRuntime(IReadOnlyList<RuntimeFile> files) => Files = files;
        internal void Validate(Specification specification)
        {
            if (Files.Count != specification.Components.Count || Files.Select(file => file.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Files.Count)
                throw new IOException("Invalid verified Script Hook RDR2 runtime.");
            foreach (var component in specification.Components) {
                var file = Files.SingleOrDefault(file => file.Path == component.Path);
                if (file == null || file.Bytes.Length != component.Length || !HashBytes(file.Bytes).Equals(component.Sha256, StringComparison.OrdinalIgnoreCase)
                    || !file.Sha256.Equals(component.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Script Hook RDR2 component checksum failed: " + component.Path);
            }
        }
    }

    public static Installation Inspect(string root) => Inspect(root, Official);
    internal static Installation Inspect(string root, Specification specification)
    {
        var missing = false;
        foreach (var component in specification.Components.Where(file => file.Path is HookPath or LoaderPath)) {
            var path = SafePath(root, component.Path);
            if (Directory.Exists(path)) return new(State.Conflict, ConflictMessage);
            if (!File.Exists(path)) { missing = true; continue; }
            if (new FileInfo(path).Length != component.Length || !Hash(path).Equals(component.Sha256, StringComparison.OrdinalIgnoreCase))
                return new(State.Conflict, ConflictMessage);
        }
        return missing ? new(State.Missing, "Install / update will download and verify the official Script Hook RDR2 and ASI loader, then install the Story Mode trainer.")
            : new(State.Ready, "Verified Script Hook RDR2 and ASI loader detected. Existing files will be preserved.");
    }

    public static async Task<VerifiedRuntime> DownloadAsync(IProgress<string>? progress = null, CancellationToken token = default)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        try { return await DownloadAsync(http, Official, progress, token).ConfigureAwait(false); }
        catch (HttpRequestException error) {
            throw new IOException("The official Script Hook RDR2 download failed. Check your connection and choose Install / update again. No game files were changed.", error);
        } catch (OperationCanceledException error) when (!token.IsCancellationRequested) {
            throw new IOException("The official Script Hook RDR2 download timed out. Choose Install / update again when your connection is available. No game files were changed.", error);
        }
    }

    internal static async Task<VerifiedRuntime> DownloadAsync(HttpClient http, Specification specification,
        IProgress<string>? progress = null, CancellationToken token = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(90));
        progress?.Report("Downloading official Script Hook RDR2 and ASI loader from dev-c.com...");
        var current = new Uri(DownloadUrl);
        for (var redirects = 0; ; redirects++) {
            ValidateUrl(current);
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.Referrer = new Uri(AuthorPage);
            request.Headers.UserAgent.ParseAdd("HappyCheats/1.0");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            ValidateUrl(response.RequestMessage?.RequestUri ?? current);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect) {
                if (redirects >= 3 || response.Headers.Location == null) throw new IOException("The official Script Hook RDR2 download redirected unexpectedly.");
                current = new Uri(current, response.Headers.Location);
                continue;
            }
            response.EnsureSuccessStatusCode();
            if (specification.ArchiveLength <= 0 || specification.ArchiveLength > 2 * 1024 * 1024
                || response.Content.Headers.ContentLength is long length && length != specification.ArchiveLength)
                throw new IOException("The official Script Hook RDR2 download has an unexpected size.");
            await using var input = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var output = new MemoryStream(checked((int)specification.ArchiveLength));
            var buffer = new byte[16384];
            int count;
            while ((count = await input.ReadAsync(buffer, deadline.Token).ConfigureAwait(false)) != 0) {
                if (output.Length + count > specification.ArchiveLength) throw new IOException("The official Script Hook RDR2 download exceeds its verified size.");
                output.Write(buffer, 0, count);
            }
            deadline.Token.ThrowIfCancellationRequested();
            progress?.Report("Verifying the official Script Hook RDR2 download...");
            return ReadArchive(output.ToArray(), specification);
        }
    }

    private static void ValidateUrl(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length != 0
            || uri.Fragment.Length != 0 || uri.Host is not ("dev-c.com" or "www.dev-c.com"))
            throw new IOException("The Script Hook RDR2 download must remain on the author's HTTPS website.");
    }

    internal static VerifiedRuntime ReadArchive(byte[] bytes, Specification specification)
    {
        if (bytes.LongLength != specification.ArchiveLength || !HashBytes(bytes).Equals(specification.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The official Script Hook RDR2 download failed SHA-256 verification. No game files were changed.");
        using var input = new MemoryStream(bytes, writable: false);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        if (archive.Entries.Count != specification.Entries.Count || archive.Entries.Count > 16)
            throw new IOException("Unexpected official Script Hook RDR2 archive structure.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expanded = 0;
        foreach (var entry in archive.Entries) {
            var path = entry.FullName;
            var parts = path.TrimEnd('/').Split('/');
            var type = (entry.ExternalAttributes >> 16) & 0xF000;
            if (path.Contains('\\') || path.Contains(':') || parts.Any(part => part is "" or "." or "..") || !seen.Add(path)
                || type is not (0 or 0x8000 or 0x4000) || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0
                || !specification.Entries.TryGetValue(path, out var expectedLength) || entry.Length != expectedLength
                || path.EndsWith('/') && entry.Length != 0 || type == 0x4000 && !path.EndsWith('/'))
                throw new IOException("Unexpected path or entry in the official Script Hook RDR2 archive.");
            expanded = checked(expanded + entry.Length);
            if (expanded > 2 * 1024 * 1024) throw new IOException("The official Script Hook RDR2 archive exceeds its extraction limit.");
        }
        var files = new List<RuntimeFile>();
        foreach (var component in specification.Components) {
            var entry = archive.GetEntry(component.ArchivePath) ?? throw new IOException("The official Script Hook RDR2 archive is incomplete.");
            if (component.Length <= 0 || component.Length > 1024 * 1024 || entry.Length != component.Length)
                throw new IOException("Invalid Script Hook RDR2 component size.");
            using var stream = entry.Open();
            var contents = new byte[component.Length];
            stream.ReadExactly(contents);
            if (stream.ReadByte() != -1 || !HashBytes(contents).Equals(component.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Script Hook RDR2 component checksum failed: " + component.Path);
            if (component.Path == HookPath && !HasExports(contents, Rdr2Module.HookExports)
                || component.Path == LoaderPath && !HasExports(contents, new[] { "DirectInput8Create" }))
                throw new IOException("Script Hook RDR2 runtime architecture or native exports are invalid.");
            files.Add(new(component.Path, contents, component.Sha256));
        }
        var result = new VerifiedRuntime(files);
        result.Validate(specification);
        return result;
    }

    internal static bool IsRuntimePath(string path) => path is HookPath or LoaderPath or ReadmePath;
    internal static bool KnownComponent(string path, string hash, Specification specification) =>
        specification.Components.Any(file => file.Path == path && file.Sha256.Equals(hash, StringComparison.OrdinalIgnoreCase));
    private static string HashBytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static bool HasExports(byte[] bytes, IReadOnlyCollection<string> required)
    {
        try {
            using var stream = new MemoryStream(bytes, writable: false);
            using var pe = new PEReader(stream);
            var headers = pe.PEHeaders;
            if (headers.CoffHeader.Machine != Machine.Amd64 || (headers.CoffHeader.Characteristics & Characteristics.Dll) == 0
                || headers.PEHeader?.Magic != PEMagic.PE32Plus) return false;
            var directory = headers.PEHeader.ExportTableDirectory;
            if (directory.RelativeVirtualAddress == 0 || directory.Size < 40) return false;
            var exports = pe.GetSectionData(directory.RelativeVirtualAddress).GetReader(0, 40);
            exports.Offset = 24; var nameCount = exports.ReadUInt32();
            exports.Offset = 32; var namesRva = exports.ReadInt32();
            if (nameCount > 4096 || nameCount < required.Count || namesRva <= 0) return false;
            var names = pe.GetSectionData(namesRva).GetReader(0, checked((int)nameCount * 4));
            var found = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < nameCount; index++) {
                var rva = names.ReadInt32();
                if (rva <= 0) return false;
                var section = pe.GetSectionData(rva);
                var name = section.GetContent(0, Math.Min(512, section.Length));
                var terminator = name.IndexOf((byte)0);
                if (terminator < 0) return false;
                found.Add(Encoding.ASCII.GetString(name.AsSpan(0, terminator)));
            }
            return required.All(found.Contains);
        } catch (Exception error) when (error is IOException or BadImageFormatException or ArgumentException or OverflowException) { return false; }
    }
}
