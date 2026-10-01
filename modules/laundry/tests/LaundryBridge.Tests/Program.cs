using System.Text.Json;
using HappyCheats.Core;
using LaundryTrainer;

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Laundry launcher integration tests require Windows.");
var root = Path.Combine(Path.GetTempPath(), "HappyCheats-LaundryBridgeTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var tests = 0;
var calls = 0;
var bridgeDirectory = Path.Combine(root, "UserData", "HappyCheats", "laundry");
var commandDirectory = Path.Combine(bridgeDirectory, "commands");
var responseDirectory = Path.Combine(bridgeDirectory, "responses");
var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
var state = new BridgeState(true, true, "Ready", false, 5, false, 5, true, true, true);

void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    tests++;
    Console.WriteLine("PASS " + message);
}
string Session()
{
    using var status = JsonDocument.Parse(File.ReadAllText(Path.Combine(bridgeDirectory, "status.json")));
    return status.RootElement.GetProperty("session").GetString()!;
}
void Tick(LauncherBridge bridge, Func<BridgeCommand, string>? action = null)
{
    Thread.Sleep(110);
    var callerThread = Environment.CurrentManagedThreadId;
    bridge.Tick(() => state, command =>
    {
        if (Environment.CurrentManagedThreadId != callerThread) throw new Exception("Wrong callback thread.");
        calls++;
        return action?.Invoke(command) ?? "Applied";
    });
}
string Submit(LauncherBridge bridge, string? session = null, long? expiry = null, string? id = null,
    Func<BridgeCommand, string>? action = null)
{
    id ??= Guid.NewGuid().ToString("N");
    var command = new BridgeCommand(id, session ?? Session(), expiry ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 5000,
        "cash", 10000, null);
    File.WriteAllText(Path.Combine(commandDirectory, id + ".json"), JsonSerializer.Serialize(command, json));
    Tick(bridge, action);
    return id;
}
bool Success(string id)
{
    using var response = JsonDocument.Parse(File.ReadAllText(Path.Combine(responseDirectory, id + ".json")));
    return response.RootElement.GetProperty("success").GetBoolean();
}
try
{
    using var bridge = new LauncherBridge(root);
    Tick(bridge);
    var session = Session();
    Check(Guid.TryParseExact(session, "N", out _), "A fresh bridge advertises an unpredictable session ID");
    var first = Submit(bridge);
    Check(Success(first) && calls == 1, "A valid command runs once on the Tick caller thread and receives an acknowledgement");
    File.Delete(Path.Combine(responseDirectory, first + ".json"));
    Submit(bridge, id: first);
    Check(!Success(first) && calls == 1, "A repeated command ID is rejected even after the first acknowledgement is removed");
    var stale = Submit(bridge, session: Guid.NewGuid().ToString("N"));
    Check(!Success(stale) && calls == 1, "A command from another session cannot mutate state");
    var expired = Submit(bridge, expiry: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1000);
    Check(!Success(expired) && calls == 1, "Expired commands cannot mutate state");
    var distant = Submit(bridge, expiry: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 60_000);
    Check(!Success(distant) && calls == 1, "Commands with excessive lifetime are rejected");
    var failed = Submit(bridge, action: _ => throw new IOException("Game callback failed after a possible partial effect"));
    Check(!Success(failed) && calls == 2, "A failed gameplay callback receives a failure response");
    Submit(bridge, id: failed);
    Check(!Success(failed) && calls == 2, "A failed callback is never automatically replayed");
    var badId = Guid.NewGuid().ToString("N");
    File.WriteAllText(Path.Combine(commandDirectory, badId + ".json"), "{invalid");
    Tick(bridge);
    Check(!Success(badId) && calls == 2, "Malformed JSON is acknowledged without applying an action");
    var largeId = Guid.NewGuid().ToString("N");
    File.WriteAllText(Path.Combine(commandDirectory, largeId + ".json"), new string(' ', 4097));
    Tick(bridge);
    Check(!Success(largeId) && calls == 2, "Oversized commands are rejected");
    var pendingId = Guid.NewGuid().ToString("N");
    File.WriteAllText(Path.Combine(commandDirectory, pendingId + ".tmp"), "{} ");
    Tick(bridge);
    Check(calls == 2 && File.Exists(Path.Combine(commandDirectory, pendingId + ".tmp")), "Unpublished temporary commands are ignored");
    bridge.ChangeSession();
    Tick(bridge);
    Check(Session() != session, "A scene change rotates the session");
    var oldScene = Submit(bridge, session: session);
    Check(!Success(oldScene) && calls == 2, "Commands queued for the previous scene are rejected");
    var recovered = Submit(bridge);
    Check(Success(recovered) && calls == 3, "A fresh command still succeeds after malformed and stale input");
    bridge.Dispose();
    using (var disconnected = JsonDocument.Parse(File.ReadAllText(Path.Combine(bridgeDirectory, "status.json"))))
        Check(!disconnected.RootElement.GetProperty("ready").GetBoolean(), "Disposal publishes disconnected state");

    var gameRoot = Path.Combine(root, "fake-game");
    Directory.CreateDirectory(Path.Combine(gameRoot, "Mods"));
    var trainerPath = Path.Combine(gameRoot, "Mods", "LaundryTrainer.dll");
    File.WriteAllText(trainerPath, "test-only trainer fixture");
    var module = new LaundryModule(root);
    var installation = new GameInstallation(gameRoot, "test", true, true, false, true, "fixture");
    await module.RemoveAsync(installation);
    Check(!File.Exists(trainerPath), "RemoveAsync removes its trainer with no progress reporter supplied");
    Check(Directory.EnumerateFiles(Path.Combine(gameRoot, "UserData", "HappyCheats", "removed"), "LaundryTrainer.dll", SearchOption.AllDirectories).Count() == 1,
        "Removal preserves a trainer backup");
    await module.RemoveAsync(installation);
    Check(!File.Exists(trainerPath), "Trainer removal is idempotent");
    Console.WriteLine($"{tests} checks passed. Only temporary fixtures were modified.");
}
finally
{
    // This exact path was constructed above, contains a UUID, and belongs to this test invocation.
    if (Path.GetFileName(root).StartsWith("HappyCheats-LaundryBridgeTests-", StringComparison.Ordinal) &&
        Path.GetDirectoryName(root) == Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar))
        Directory.Delete(root, true);
}
