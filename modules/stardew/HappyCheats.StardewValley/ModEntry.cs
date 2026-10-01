using System.Globalization;
using System.Text.Json;
using HarmonyLib;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Tools;

namespace HappyCheats.StardewValley;

public sealed class ModEntry : Mod
{
    internal const string Version = "0.1.1";
    internal static ModEntry? Instance { get; private set; }
    private readonly Dictionary<string, bool> toggles = Protocol.ToggleIds.ToDictionary(id => id, _ => false);
    private readonly HashSet<string> handled = new(StringComparer.Ordinal);
    private string bridge = "", session = Guid.NewGuid().ToString("N"), message = "Load a single-player save to connect.";
    private string? lastCommand;
    private string? initializationError;
    private Farmer? previousPlayer;
    private int speedBonus;
    private bool initialized, saving, faulted;
    private long nextStatus;
    internal bool Ready => initialized && !faulted && !saving && Context.IsWorldReady && !Context.IsMultiplayer && Game1.player is not null;
    internal bool Enabled(string id) => Ready && toggles.TryGetValue(id, out bool value) && value;
    internal int SpeedBonus => Ready ? speedBonus : 0;

    public override void Entry(IModHelper helper)
    {
        Instance = this;
        bridge = Path.Combine(helper.DirectoryPath, "bridge");
        try {
            var gameVersion = typeof(Game1).Assembly.GetName().Version!;
            if (gameVersion.Major != 1 || gameVersion.Minor != 6 || gameVersion.Build < 15)
                throw new NotSupportedException("This trainer requires Stardew Valley 1.6.15 or later in the 1.6 series.");
            Directory.CreateDirectory(Path.Combine(bridge, "commands"));
            Directory.CreateDirectory(Path.Combine(bridge, "responses"));
            GamePatches.Install(new Harmony(ModManifest.UniqueID));
            initialized = true;
            Monitor.Log("HappyCheats connected. Load a single-player save; use the HappyCheats launcher for controls.", LogLevel.Info);
        } catch (Exception ex) {
            message = "Trainer initialization failed: " + ex.Message;
            initializationError = message;
            Monitor.Log(message + Environment.NewLine + ex, LogLevel.Error);
        }
        helper.Events.GameLoop.UpdateTicking += OnUpdate;
        helper.Events.GameLoop.SaveLoaded += (_, _) => ResetSession("Single-player save loaded. Trainer ready.");
        helper.Events.GameLoop.ReturnedToTitle += (_, _) => ResetSession("Load a single-player save to connect.");
        helper.Events.GameLoop.Saving += (_, _) => { saving = true; Publish(); };
        helper.Events.GameLoop.Saved += (_, _) => { saving = false; Publish(); };
        helper.Events.Input.ButtonPressed += OnButtonPressed;
        helper.ConsoleCommands.Add("happycheats_off", "Disable all HappyCheats continuous controls.", (_, _) => { DisableAll(); Publish(); });
        Publish();
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (Game1.game1?.IsActive != true || Helper.Input.IsSuppressed(e.Button)) return;
        string? action = e.Button switch {
            SButton.F1 => "stardew_unlimited_health", SButton.F2 => "stardew_unlimited_energy",
            SButton.F3 => "stardew_easy_crafting", SButton.F4 => "stardew_unlimited_items",
            SButton.F5 => "stardew_unlimited_water", SButton.F6 => "stardew_freeze_time",
            SButton.F7 => "stardew_refill", SButton.F10 => "disable_all", _ => null
        };
        if (action is null || (!Ready && action != "disable_all")) return;
        try {
            message = Execute(new TrainerCommand { action = action, enabled = toggles.TryGetValue(action, out bool value) ? !value : null });
            Helper.Input.Suppress(e.Button);
            Publish();
        } catch (Exception ex) { Monitor.Log(ex.Message, LogLevel.Warn); }
    }

    private void ResetSession(string nextMessage)
    {
        DisableAll();
        previousPlayer = Context.IsWorldReady ? Game1.player : null;
        session = Guid.NewGuid().ToString("N");
        lastCommand = null;
        handled.Clear();
        saving = false;
        faulted = false;
        message = nextMessage;
        Publish();
    }

    private void OnUpdate(object? sender, UpdateTickingEventArgs e)
    {
        try {
            var current = Context.IsWorldReady ? Game1.player : null;
            if (!ReferenceEquals(current, previousPlayer)) ResetSession("Load a single-player save to connect.");
            if (Context.IsMultiplayer) { DisableAll(); message = "Stardew Valley trainer supports single-player saves only."; }
            if (e.IsMultipleOf(6)) PollCommands();
            if (Ready) {
                if (Enabled("stardew_unlimited_health")) Game1.player.health = Game1.player.maxHealth;
                if (Enabled("stardew_unlimited_energy")) { Game1.player.Stamina = Game1.player.MaxStamina; Game1.player.exhausted.Value = false; }
                if (Enabled("stardew_unlimited_water")) RefillWater();
                if (Enabled("stardew_freeze_time")) Game1.gameTimeInterval = 0;
            }
        } catch (Exception ex) {
            DisableAll();
            faulted = true;
            message = "Trainer paused: " + ex.Message;
            Monitor.Log(message + Environment.NewLine + ex, LogLevel.Error);
        }
        if (Environment.TickCount64 >= nextStatus) { nextStatus = Environment.TickCount64 + 500; Publish(); }
    }

    private void PollCommands()
    {
        foreach (string path in Directory.EnumerateFiles(Path.Combine(bridge, "commands"), "*.json").OrderBy(File.GetCreationTimeUtc).Take(8)) {
            string id = Path.GetFileNameWithoutExtension(path);
            if (!Guid.TryParseExact(id, "N", out _)) continue;
            string responsePath = Path.Combine(bridge, "responses", id + ".json");
            if (handled.Contains(id) || File.Exists(responsePath)) { TryDelete(path); continue; }
            bool success = false;
            string result;
            try {
                if (new FileInfo(path).Length > 8192) throw new InvalidDataException("Command exceeds the size limit.");
                var command = JsonSerializer.Deserialize<TrainerCommand>(File.ReadAllText(path));
                Protocol.Validate(command, path, session, Protocol.Now());
                // Record before mutation. A failed acknowledgement must never replay add-money.
                handled.Add(id);
                result = Execute(command!);
                success = true;
                message = result;
            } catch (Exception ex) { result = ex.Message; handled.Add(id); }
            lastCommand = id;
            Protocol.Write(responsePath, new { schema_version = 1, session_id = session, request_id = id, success, message = result, updated_utc = Protocol.Now() });
            TryDelete(path);
            Publish();
        }
    }

    private string Execute(TrainerCommand command)
    {
        if (command.action == "disable_all") { DisableAll(); return "All continuous cheats are off."; }
        if (!Ready) throw new InvalidOperationException("Load a single-player save before using trainer controls.");
        if (toggles.ContainsKey(command.action)) {
            toggles[command.action] = command.enabled!.Value;
            return command.enabled.Value ? "Trainer control enabled." : "Trainer control disabled.";
        }
        var player = Game1.player;
        int number = Protocol.Numbers.ContainsKey(command.action) ? Protocol.Number(command.action, command.value) : 0;
        switch (command.action) {
            case "stardew_add_money":
                player.Money = (int)Math.Min(999_999_999L, (long)player.Money + number);
                return "Money added.";
            case "stardew_set_health":
                if (number > player.maxHealth) throw new InvalidOperationException("Health cannot exceed max health. Increase max health first.");
                if (Enabled("stardew_unlimited_health") && number < player.maxHealth) throw new InvalidOperationException("Turn off unlimited health before setting a lower health amount.");
                player.health = number; return "Health updated.";
            case "stardew_set_max_health":
                player.maxHealth = number; player.health = Math.Clamp(player.health, 1, number); return "Max health updated.";
            case "stardew_set_time":
                if (Game1.eventUp || Game1.currentMinigame is not null) throw new InvalidOperationException("Wait for the event or minigame to finish before setting time.");
                Game1.timeOfDay = number; Game1.gameTimeInterval = 0; return "Game time updated.";
            case "stardew_set_speed":
                speedBonus = number; return "Movement speed updated.";
            case "stardew_refill":
                player.health = player.maxHealth; player.Stamina = player.MaxStamina; player.exhausted.Value = false; RefillWater();
                return "Health, energy and watering cans refilled.";
            default: throw new InvalidOperationException("Unknown trainer control.");
        }
    }

    private static void RefillWater()
    {
        foreach (var item in Game1.player.Items)
            if (item is WateringCan can) can.WaterLeft = can.waterCanMax;
    }

    private void DisableAll()
    {
        foreach (string key in Protocol.ToggleIds) toggles[key] = false;
        speedBonus = 0;
    }
    private void Publish()
    {
        try {
            var values = new Dictionary<string, string>();
            if (Ready) {
                var p = Game1.player;
                values["stardew_money"] = p.Money.ToString(CultureInfo.InvariantCulture);
                values["stardew_health"] = p.health.ToString(CultureInfo.InvariantCulture);
                values["stardew_max_health"] = p.maxHealth.ToString(CultureInfo.InvariantCulture);
                values["stardew_energy"] = p.Stamina.ToString("0.0", CultureInfo.InvariantCulture);
                values["stardew_max_energy"] = p.MaxStamina.ToString(CultureInfo.InvariantCulture);
                values["stardew_time"] = Game1.timeOfDay.ToString("0000", CultureInfo.InvariantCulture);
                values["stardew_speed"] = speedBonus.ToString(CultureInfo.InvariantCulture);
                var can = p.Items.OfType<WateringCan>().FirstOrDefault();
                values["stardew_water"] = can is null ? "—" : $"{can.WaterLeft}/{can.waterCanMax}";
            }
            Protocol.Write(Path.Combine(bridge, "status.json"), new {
                schema_version = 1, plugin_version = Version, session_id = session,
                process_id = Environment.ProcessId, ready = Ready, message = initializationError ?? message, enabled = toggles, values,
                last_command = lastCommand, updated_utc = Protocol.Now(), game_version = typeof(Game1).Assembly.GetName().Version?.ToString()
            });
        } catch (Exception ex) { Monitor.Log("Cannot publish trainer status: " + ex.Message, LogLevel.Warn); }
    }
    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
