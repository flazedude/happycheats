using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace HappyCheats.Core.CivilizationVII;

/// <summary>
/// Civilization VII's JavaScript Tuner protocol. The owner of the opened socket must
/// be verified before ExecuteAsync. Connections never reopen or replay commands implicitly.
/// Protocol reference: https://github.com/ghost-ng/FiretunerTerminal/blob/main/civ7_terminal/protocol.py
/// and connection.py; this implementation is independent of that Python client.
/// </summary>
public sealed class Civ7TunerClient : IAsyncDisposable
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

    public Civ7TunerClient(int port = 4318, TimeSpan? timeout = null)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        this.port = port;
        this.timeout = timeout ?? TimeSpan.FromSeconds(5);
        if (this.timeout <= TimeSpan.Zero || this.timeout > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    public bool IsConnected => !disposed && stream != null;
    internal int LocalPort => client?.Client.LocalEndPoint is IPEndPoint endpoint ? endpoint.Port
        : throw new IOException("The Civilization VII Tuner connection is closed.");

    /// <summary>Only opens the IPv4 loopback socket. No JavaScript or handshake is sent.</summary>
    public Task ConnectAsync(CancellationToken cancellationToken = default) =>
        RunAsync(async token => {
            if (stream == null) {
                client = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
                await client.ConnectAsync(IPAddress.Loopback, port, token);
                stream = client.GetStream();
            }
            return true;
        }, cancellationToken);

    /// <summary>
    /// Evaluates JavaScript once in the game's global context and returns its result.
    /// Object results are JSON; every request has distinct unpredictable success/error
    /// markers so unsolicited output cannot acknowledge it. A timeout leaves its outcome unknown.
    /// </summary>
    public Task<string> ExecuteAsync(string javascript, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(javascript) || javascript.Contains('\0') || Utf8.GetByteCount(javascript) > MaxFrameBytes / 8)
            throw new ArgumentException("The Civilization VII JavaScript command is empty or too large.", nameof(javascript));
        return RunAsync(async token => {
            if (stream == null) throw new IOException("Connect and authenticate the Civilization VII Tuner socket before sending trainer commands.");
            var marker = "__HC_C7_" + Guid.NewGuid().ToString("N");
            var success = marker + "_OK:";
            var failure = marker + "_ERROR:";
            // Indirect eval installs the runtime in the global context; the string result
            // is returned by VII's Tuner directly, unlike VI's print/context envelopes.
            var wrapped = "(()=>{try{const __hc_r=(0,eval)(" + JsonSerializer.Serialize(javascript)
                + ");return " + JsonSerializer.Serialize(success)
                + "+(typeof __hc_r==='object'&&__hc_r!==null?JSON.stringify(__hc_r,(k,v)=>typeof v==='bigint'?v.toString():v):String(__hc_r));}"
                + "catch(__hc_e){return " + JsonSerializer.Serialize(failure) + "+String(__hc_e);}})()";
            await SendAsync("CMD:65535:" + wrapped, token);
            var received = 0;
            for (var count = 0; count < MaxMessages; count++) {
                var message = await ReceiveAsync(token);
                received += message.Bytes;
                if (received > MaxReceivedBytes) throw new IOException("Civilization VII returned too much Tuner output; the command outcome is unconfirmed.");
                if (message.Tag != 3) continue;
                if (message.Payload.StartsWith(failure, StringComparison.Ordinal))
                    throw new IOException("Civilization VII rejected the JavaScript command: " + message.Payload[failure.Length..Math.Min(message.Payload.Length, failure.Length + 1024)]);
                if (message.Payload.StartsWith(success, StringComparison.Ordinal)) {
                    var result = message.Payload[success.Length..];
                    if (result.Contains('\0')) throw new IOException("Civilization VII Tuner returned an embedded null character in the command result.");
                    return result;
                }
                // Stale responses, engine logs, ACKs and other tools' output are not our result.
            }
            throw new IOException("Civilization VII returned too many Tuner messages without confirming this command.");
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
            throw new IOException("Civilization VII Tuner timed out. The command outcome is unconfirmed; reconnect and read the current game state before retrying.", exception);
        } catch {
            if (entered) Reset();
            throw;
        } finally { if (entered) gate.Release(); }
    }

    private async Task SendAsync(string payload, CancellationToken token)
    {
        var length = checked(Utf8.GetByteCount(payload) + 1);
        if (length > MaxFrameBytes) throw new IOException("Civilization VII Tuner command is too large.");
        var frame = new byte[length + 8];
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), (uint)length);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4, 4), 3);
        Utf8.GetBytes(payload, frame.AsSpan(8));
        await stream!.WriteAsync(frame, token);
    }

    private async Task<(int Tag, string Payload, int Bytes)> ReceiveAsync(CancellationToken token)
    {
        var header = new byte[8];
        await stream!.ReadExactlyAsync(header, token);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0, 4));
        if (length is 0 or > MaxFrameBytes) throw new IOException("Invalid Civilization VII Tuner frame size.");
        var payload = new byte[(int)length];
        await stream.ReadExactlyAsync(payload, token);
        if (payload[^1] != 0) throw new IOException("Civilization VII Tuner frame is not terminated.");
        try { return (BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4, 4)), Utf8.GetString(payload, 0, payload.Length - 1), (int)length + 8); }
        catch (DecoderFallbackException exception) { throw new IOException("Civilization VII Tuner returned invalid UTF-8.", exception); }
    }

    private void Reset()
    {
        stream?.Dispose();
        client?.Dispose();
        stream = null;
        client = null;
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
