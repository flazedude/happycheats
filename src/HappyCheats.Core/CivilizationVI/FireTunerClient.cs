using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace HappyCheats.Core.CivilizationVI;

/// <summary>
/// Persistent, loopback-only Firaxis Nexus transport. A command is never replayed after
/// a lost acknowledgement: callers must obtain fresh game state before trying again.
/// Protocol reference: https://github.com/lmwilki/civ6-mcp/blob/main/src/civ_mcp/tuner_client.py
/// and connection.py (MIT, Liam Wilkinson, 2026). This implementation is independent.
/// </summary>
public sealed class FireTunerClient : IAsyncDisposable
{
    private const int MaxFrameBytes = 1_048_576;
    private const int MaxMessages = 512;
    private const int MaxReceivedBytes = 4_194_304;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly int port;
    private readonly TimeSpan timeout;
    private TcpClient? client;
    private NetworkStream? stream;
    private bool disposed;
    private IReadOnlyDictionary<int, string> contexts = new ReadOnlyDictionary<int, string>(new Dictionary<int, string>());

    public FireTunerClient(int port = 4318, TimeSpan? timeout = null)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        this.port = port;
        this.timeout = timeout ?? TimeSpan.FromSeconds(5);
        if (this.timeout <= TimeSpan.Zero || this.timeout > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    public bool IsConnected => !disposed && stream != null;
    internal int LocalPort => client?.Client.LocalEndPoint is IPEndPoint endpoint ? endpoint.Port
        : throw new IOException("The Civilization VI Tuner connection is closed.");
    public string AppIdentity { get; private set; } = "";
    public IReadOnlyDictionary<int, string> Contexts => contexts;

    public Task ConnectAsync(CancellationToken cancellationToken = default) =>
        RunAsync(async token => { await ConnectCoreAsync(token); return true; }, cancellationToken);

    /// <summary>
    /// Executes a Lua chunk in the named VM and returns one printed line beginning with
    /// responseMarkerPrefix, including the prefix. Use a fresh unpredictable marker for
    /// every call. A missing completion sentinel or response is an error, never success.
    /// </summary>
    public Task<string> ExecuteAsync(string contextName, string script, string responseMarkerPrefix,
        CancellationToken cancellationToken = default)
    {
        if (contextName is not ("GameCore_Tuner" or "InGame"))
            throw new ArgumentException("Choose the exact GameCore_Tuner or InGame Lua context.", nameof(contextName));
        if (string.IsNullOrWhiteSpace(script) || script.Contains('\0') || Utf8.GetByteCount(script) > MaxFrameBytes / 2)
            throw new ArgumentException("The Civilization VI Lua command is empty or too large.", nameof(script));
        if (string.IsNullOrEmpty(responseMarkerPrefix) || responseMarkerPrefix.Length > 256
            || responseMarkerPrefix.Any(char.IsControl))
            throw new ArgumentException("A bounded response marker without control characters is required.", nameof(responseMarkerPrefix));

        return RunAsync(async token => {
            await ConnectCoreAsync(token);
            // Lua state IDs can change when a save is loaded without restarting the process.
            await RefreshContextsAsync(token);
            var candidates = contexts.Where(pair => pair.Value == contextName).ToArray();
            if (candidates.Length != 1)
                throw new IOException("Load a Civilization VI game before using this control; its Lua context is unavailable or ambiguous.");
            var nonce = Guid.NewGuid().ToString("N");
            var done = "__HC_DONE_" + nonce;
            var error = "__HC_ERROR_" + nonce + ":";
            // Do not shadow print: functions defined in a loaded runtime must not capture
            // an old request's marker. The module supplies its own per-request print marker.
            var wrapped = "local __hc_ok,__hc_err=pcall(function()\n" + script
                + "\nend); if not __hc_ok then print('" + error + "'..tostring(__hc_err)) end; print('" + done + "')";
            await SendAsync(3, "CMD:" + candidates[0].Key.ToString(CultureInfo.InvariantCulture) + ":" + wrapped, token);
            string? result = null;
            string? failure = null;
            var received = 0;
            for (var count = 0; count < MaxMessages; count++) {
                var message = await ReceiveAsync(token);
                received += message.Bytes;
                if (received > MaxReceivedBytes) throw new IOException("Civilization VI returned too much tuner output.");
                if (message.Payload.StartsWith("ERR:", StringComparison.Ordinal))
                    throw new IOException("Civilization VI rejected the Lua command: " + message.Payload[..Math.Min(message.Payload.Length, 1024)]);
                var output = ParseOutput(message.Payload, contextName);
                if (output == null) continue; // Ignore ACKs, stale results and other contexts.
                if (output == done) {
                    if (failure != null) throw new IOException("Civilization VI rejected the Lua command: " + failure);
                    return result ?? throw new IOException("Civilization VI did not acknowledge this command. Its outcome is unconfirmed.");
                }
                if (output.StartsWith(error, StringComparison.Ordinal)) failure = output[error.Length..Math.Min(output.Length, error.Length + 1024)];
                if (!output.StartsWith(responseMarkerPrefix, StringComparison.Ordinal)) continue;
                if (result != null) throw new IOException("Civilization VI returned duplicate acknowledgements. Its outcome is unconfirmed.");
                result = output;
            }
            throw new IOException("Civilization VI returned too many tuner messages without confirming this command.");
        }, cancellationToken);
    }

    private async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var entered = false;
        try {
            await gate.WaitAsync(deadline.Token);
            entered = true;
            ObjectDisposedException.ThrowIf(disposed, this);
            return await operation(deadline.Token);
        } catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested) {
            if (entered) Reset();
            throw new IOException("Civilization VI tuner timed out. The command outcome is unconfirmed; reconnect and read the current game state before retrying.", exception);
        } catch {
            if (entered) Reset();
            throw;
        } finally { if (entered) gate.Release(); }
    }

    private async Task ConnectCoreAsync(CancellationToken token)
    {
        if (stream != null) return;
        client = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
        await client.ConnectAsync(IPAddress.Loopback, port, token);
        stream = client.GetStream();
        await SendAsync(4, "APP:", token);
        var identity = await ReceiveHandshakeAsync(token);
        if (identity.Length == 0 || identity.Length > 4096) throw new IOException("Invalid Civilization VI tuner identity.");
        AppIdentity = identity;
        await RefreshContextsAsync(token);
    }

    private async Task RefreshContextsAsync(CancellationToken token)
    {
        await SendAsync(4, "LSQ:", token);
        var payload = await ReceiveHandshakeAsync(token);
        var parts = payload.Split(new[] { '\0', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var found = new Dictionary<int, string>();
        for (var index = 0; index + 1 < parts.Length; index++) {
            if (!int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out var id)) continue;
            var name = parts[++index];
            if (id < 0 || name.Length > 256 || name.Any(char.IsControl) || !found.TryAdd(id, name))
                throw new IOException("Invalid or duplicate Civilization VI Lua context.");
            if (found.Count > 2048) throw new IOException("Too many Civilization VI Lua contexts.");
        }
        contexts = new ReadOnlyDictionary<int, string>(found);
    }

    private async Task<string> ReceiveHandshakeAsync(CancellationToken token)
    {
        var received = 0;
        for (var count = 0; count < MaxMessages; count++) {
            var message = await ReceiveAsync(token);
            received += message.Bytes;
            if (received > MaxReceivedBytes) break;
            if (message.Payload.StartsWith("ERR:", StringComparison.Ordinal)) throw new IOException("Civilization VI tuner handshake failed.");
            if (message.Tag == 4 && !message.Payload.StartsWith("O\0", StringComparison.Ordinal)) return message.Payload;
        }
        throw new IOException("Civilization VI did not complete its tuner handshake.");
    }

    private async Task SendAsync(int tag, string payload, CancellationToken token)
    {
        var length = checked(Utf8.GetByteCount(payload) + 1);
        if (length > MaxFrameBytes) throw new IOException("Civilization VI tuner command is too large.");
        var frame = new byte[length + 8];
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), (uint)length);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4, 4), tag);
        Utf8.GetBytes(payload, frame.AsSpan(8));
        await stream!.WriteAsync(frame, token);
    }

    private async Task<(int Tag, string Payload, int Bytes)> ReceiveAsync(CancellationToken token)
    {
        var header = new byte[8];
        await stream!.ReadExactlyAsync(header, token);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0, 4));
        if (length is 0 or > MaxFrameBytes) throw new IOException("Invalid Civilization VI tuner frame size.");
        var payload = new byte[(int)length];
        await stream.ReadExactlyAsync(payload, token);
        if (payload[^1] != 0) throw new IOException("Civilization VI tuner frame is not terminated.");
        var count = payload.Length;
        while (count > 0 && payload[count - 1] == 0) count--;
        try { return (BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4, 4)), Utf8.GetString(payload, 0, count), (int)length + 8); }
        catch (DecoderFallbackException exception) { throw new IOException("Civilization VI tuner returned invalid UTF-8.", exception); }
    }

    private static string? ParseOutput(string payload, string contextName)
    {
        var prefix = "O\0" + contextName + ": ";
        return payload.StartsWith(prefix, StringComparison.Ordinal) ? payload[prefix.Length..] : null;
    }

    private void Reset()
    {
        stream?.Dispose();
        client?.Dispose();
        stream = null;
        client = null;
        AppIdentity = "";
        contexts = new ReadOnlyDictionary<int, string>(new Dictionary<int, string>());
    }

    public async Task DisconnectAsync()
    {
        await gate.WaitAsync();
        try { Reset(); } finally { gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync();
        try { disposed = true; Reset(); } finally { gate.Release(); }
    }
}
