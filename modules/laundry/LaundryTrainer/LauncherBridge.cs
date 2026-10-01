using System.Text.Json;

namespace LaundryTrainer;

// File transport only. Tick and every gameplay callback execute on MelonLoader's main thread.
internal sealed class LauncherBridge : IDisposable
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    private readonly string _directory;
    private readonly string _commands;
    private readonly string _responses;
    private string _session = Guid.NewGuid().ToString("N");
    private readonly HashSet<string> _handled = new(StringComparer.Ordinal);
    private long _nextTick;
    private long _nextStatus;
    private bool _disposed;

    public LauncherBridge(string gameDirectory)
    {
        _directory = Path.Combine(gameDirectory, "UserData", "HappyCheats", "laundry");
        _commands = Path.Combine(_directory, "commands");
        _responses = Path.Combine(_directory, "responses");
        AssertPlain(_directory);
        Directory.CreateDirectory(_commands);
        Directory.CreateDirectory(_responses);
    }

    public void Tick(Func<BridgeState> getState, Func<BridgeCommand, string> execute)
    {
        if (_disposed || Environment.TickCount64 < _nextTick) return;
        _nextTick = Environment.TickCount64 + 100;
        AssertPlain(_directory);
        AssertPlain(_commands);
        AssertPlain(_responses);
        foreach (var path in Directory.EnumerateFiles(_commands, "*.json")
            .Where(path => Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out _)).Take(8))
        {
            var id = Path.GetFileNameWithoutExtension(path);
            if (!Guid.TryParseExact(id, "N", out _)) continue;
            AssertPlain(path);
            var claimed = Path.ChangeExtension(path, ".processing");
            // Claim before invoking an action. Interrupted commands are never replayed automatically.
            try { File.Move(path, claimed); }
            catch (IOException) { continue; }
            var success = false;
            string message;
            try
            {
                if (new FileInfo(claimed).Length > 4096) throw new InvalidDataException("Command is too large.");
                var command = JsonSerializer.Deserialize<BridgeCommand>(File.ReadAllText(claimed), Json)
                    ?? throw new InvalidDataException("Command is empty.");
                if (command.Id != id || command.Session != _session)
                    throw new InvalidOperationException("Game session changed. Refresh and try again.");
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (command.ExpiresUtcMs < now || command.ExpiresUtcMs > now + 30_000)
                    throw new InvalidOperationException("Command expired. Send a new action.");
                if (!_handled.Add(id)) throw new InvalidOperationException("This command was already processed; it was not applied again.");
                message = execute(command);
                success = true;
            }
            catch (Exception error) { message = error.GetBaseException().Message; }
            WriteAtomic(Path.Combine(_responses, id + ".json"), new { id, session = _session, success, message });
            File.Delete(claimed);
            _nextStatus = 0;
        }
        if (Environment.TickCount64 < _nextStatus) return;
        _nextStatus = Environment.TickCount64 + 500;
        var state = getState();
        WriteAtomic(Path.Combine(_directory, "status.json"), new
        {
            schemaVersion = 1, session = _session, pid = Environment.ProcessId,
            updatedUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            compatible = state.Compatible, ready = state.Ready, message = state.Message,
            washEnabled = state.WashEnabled, washMultiplier = state.WashMultiplier,
            dryEnabled = state.DryEnabled, dryMultiplier = state.DryMultiplier,
            washAvailable = state.WashAvailable, dryAvailable = state.DryAvailable, shopAvailable = state.ShopAvailable
        });
    }

    private static void WriteAtomic<T>(string destination, T value)
    {
        AssertPlain(destination);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, Json));
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void AssertPlain(string path)
    {
        for (string? cursor = Path.GetFullPath(path); cursor is not null; cursor = Path.GetDirectoryName(cursor))
            if ((File.Exists(cursor) || Directory.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Launcher bridge path contains a symbolic link or junction.");
    }

    public void ChangeSession()
    {
        _session = Guid.NewGuid().ToString("N");
        _handled.Clear();
        _nextStatus = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        WriteAtomic(Path.Combine(_directory, "status.json"), new
        {
            schemaVersion = 1, session = _session, pid = Environment.ProcessId,
            updatedUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), compatible = false, ready = false,
            message = "Trainer disconnected."
        });
    }
}

internal sealed record BridgeCommand(string Id, string Session, long ExpiresUtcMs, string Action, double? Value, bool? Enabled);
internal sealed record BridgeState(bool Compatible, bool Ready, string Message, bool WashEnabled, float WashMultiplier,
    bool DryEnabled, float DryMultiplier, bool WashAvailable, bool DryAvailable, bool ShopAvailable);
