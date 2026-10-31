using System.Net;
using System.Text.RegularExpressions;
using HappyCheats.Core.Updates;

var builder = WebApplication.CreateBuilder(args);
var directory = Path.GetFullPath(builder.Configuration["release-root"] ?? throw new ArgumentException("Supply --release-root PATH."));
var address = IPAddress.Parse(builder.Configuration["address"] ?? "127.0.0.1");
var port = int.Parse(builder.Configuration["port"] ?? "17845");
if (!UpdateAddress.IsLocalAddress(address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || port is < 1024 or > 65535)
    throw new ArgumentException("Bind to a private LAN IPv4 address or 127.0.0.1, on port 1024–65535.");
Directory.CreateDirectory(directory);
CheckLinks(directory);
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Listen(address, port);
    options.Limits.MaxRequestBodySize = 0;
});
var app = builder.Build();
app.Use(async (context, next) =>
{
    if (context.Connection.RemoteIpAddress is not { } remote || !UpdateAddress.IsLocalAddress(remote))
    { context.Response.StatusCode = 403; return; }
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    await next(context);
});
app.MapMethods("/happycheats/{name}", new[] { "GET", "HEAD" }, (string name, HttpContext context) =>
{
    var archive = Regex.IsMatch(name, @"^HappyCheats-(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)-win-x64\.zip$", RegexOptions.CultureInvariant);
    if (!archive && name != "latest.json" && name != "HappyCheats.exe") return Results.NotFound();
    var path = Path.Combine(directory, name);
    if (!File.Exists(path)) return Results.NotFound();
    try { CheckLinks(path); }
    catch (IOException) { return Results.NotFound(); }
    // Files are atomically replaced by the publisher. Open once, sharing deletion, so clients
    // already downloading finish the old file while new requests receive the new release.
    FileStream stream;
    try { stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 65536, FileOptions.Asynchronous); }
    catch (FileNotFoundException) { return Results.NotFound(); }
    context.Response.Headers.CacheControl = archive ? "public, max-age=31536000, immutable" : "no-cache, max-age=0, must-revalidate";
    return Results.Stream(stream, name == "latest.json" ? "application/json" : "application/octet-stream",
        fileDownloadName: name == "latest.json" ? null : name, enableRangeProcessing: true);
});
app.MapGet("/health", () => Results.Text("HappyCheats LAN update server"));
// A separate, explicitly allowlisted namespace reuses this host and startup task.
// ProxyKing keeps its own signing identity, manifests and immutable packages.
app.MapMethods("/proxyking/{name}", new[] { "GET", "HEAD" }, (string name, HttpContext context) =>
{
    var version = @"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)";
    var immutable = Regex.IsMatch(name, @"^ProxyKing_" + version + @"_x64-setup\.exe(\.sig)?$", RegexOptions.CultureInvariant)
        || Regex.IsMatch(name, @"^proxyking-" + version + @"-source\.tar\.gz$", RegexOptions.CultureInvariant)
        || name == "sing-box-1.14.2-source.tar.gz";
    if (!immutable && name is not ("latest.json" or "ProxyKing-Setup.exe" or "GPL-3.0.txt" or "THIRD_PARTY_NOTICES.md"
        or "sing-box-LICENSE.txt" or "engine-provenance.txt" or "npm-dependencies.json" or "rust-dependencies.json"))
        return Results.NotFound();
    var path = Path.Combine(directory, "proxyking", name);
    if (!File.Exists(path)) return Results.NotFound();
    try { CheckLinks(path); }
    catch (IOException) { return Results.NotFound(); }
    FileStream stream;
    try { stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 65536, FileOptions.Asynchronous); }
    catch (FileNotFoundException) { return Results.NotFound(); }
    context.Response.Headers.CacheControl = immutable ? "public, max-age=31536000, immutable" : "no-cache, max-age=0, must-revalidate";
    return Results.Stream(stream, name == "latest.json" ? "application/json" : "application/octet-stream",
        fileDownloadName: name == "latest.json" ? null : name, enableRangeProcessing: true);
});
app.Run();

static void CheckLinks(string path)
{
    for (var current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
        if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Linked release files or directories cannot be served.");
}
