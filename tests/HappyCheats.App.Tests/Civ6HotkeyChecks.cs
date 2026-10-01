using System.Reflection;
using System.Windows.Controls;
using HappyCheats.App;
using HappyCheats.Core;

namespace HappyCheats.App.Tests;

internal static class Civ6HotkeyChecks
{
    internal static object Run(MainWindow main)
    {
        var assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("Civ6 hotkeys: " + message);
        }
        var scope = new Civ6HotkeyScope(123, "session-one", false);
        Civ6HotkeyRouter router = new();
        Civ6HotkeyDecision Press(int key, bool down = true, Civ6HotkeyScope? current = null, int foreground = 123)
            => router.Process(key, down, current ?? scope, foreground);
        for (var key = 0x70; key <= 0x7B; key++)
        {
            var press = Press(key);
            Check(press.Suppress && press.Request?.Shortcut == $"F{key - 0x6F}", "mapped key was not intercepted and dispatched");
            Check(Press(key).Suppress && Press(key).Request == null, "held key retriggered");
            Check(Press(key, false).Suppress, "intercepted key-up leaked to the game");
        }
        Press(0xA0);
        for (var key = 0x70; key <= 0x7B; key++)
        {
            var press = Press(key);
            Check(key <= 0x73 ? press.Suppress && press.Request?.Shortcut == $"Shift+F{key - 0x6F}"
                : !press.Suppress && press.Request == null, "shift mapping changed");
            Press(key, false);
        }
        Press(0xA1); Press(0xA0, false);
        Check(Press(0x70).Request?.Shortcut == "Shift+F1", "releasing one shift cleared the other shift");
        Press(0x70, false); Press(0xA1, false);
        foreach (var modifier in new[] { 0x11, 0x12, 0x5B, 0x5C, 0xA2, 0xA3, 0xA4, 0xA5 })
        {
            Press(modifier);
            var press = Press(0x70);
            Check(!press.Suppress && press.Request == null, "Ctrl/Alt/Windows shortcut was intercepted");
            Press(0x70, false); Press(modifier, false);
        }
        Check(Press(0x74, current: scope with { Busy = true }).Suppress, "busy F5 leaked to game quick-save");
        Check(Press(0x74).Request == null, "busy press was retried after command completed");
        Press(0x74, false);
        foreach (var rejectedScope in new[] { default(Civ6HotkeyScope), scope with { ProcessId = 0 }, scope with { SessionId = "" } })
        {
            var press = Press(0x70, current: rejectedScope);
            Check(!press.Suppress && press.Request == null, "disconnected/wrong-page scope intercepted input");
            Check(Press(0x70).Request == null, "held-across-connect key dispatched");
            Press(0x70, false);
        }
        Check(!Press(0x70, foreground: 456).Suppress, "another process lost a function key");
        Check(Press(0x70).Request == null, "held-across-focus key dispatched");
        Press(0x70, false);
        router.SeedHeld(0x70);
        Check(Press(0x70).Request == null, "key held before hook startup dispatched");
        Press(0x70, false);
        var request = Press(0x70).Request!.Value;
        Check(request.Matches(scope, 123), "current queued request rejected");
        Check(!request.Matches(scope, 456) && !request.Matches(scope with { ProcessId = 456 }, 456)
            && !request.Matches(scope with { SessionId = "new-session" }, 123)
            && !request.Matches(scope with { Busy = true }, 123) && !request.Matches(default, 123),
            "queued request survived a focus/session/page/busy change");
        Check(!Press(0x70, false, foreground: 456).Suppress, "key-up was suppressed in another app");
        Check(!Press(0x41).Suppress && !Press(0x7C).Suppress, "unrelated key was intercepted");

        var library = (ListBox)main.FindName("Library");
        var original = library.SelectedItem;
        var busy = typeof(MainWindow).GetField("_busy", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var refresh = typeof(MainWindow).GetMethod("RefreshBindings", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var originalBusy = busy.GetValue(main);
        var handoff = typeof(MainWindow).GetField("_updateHandoff", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var originalHandoff = handoff.GetValue(main);
        Check(typeof(MainWindow).GetField("_civ6KeyboardHook", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(main) == null,
            "capture/preview mode installed a global keyboard hook");
        var module = new FixtureModule(main.Games.Single(game => game.Id == "civilization-vi").Module.Definition);
        var fixture = new GameItem(module) { Installation = module.Installation, Status = module.Status };
        void Update(TrainerStatus status, bool working = false)
        {
            module.Status = status; fixture.Status = status; busy.SetValue(main, working); refresh.Invoke(main, null);
        }
        void Apply(string key) => main.ApplyCivilizationShortcutAsync(key).GetAwaiter().GetResult();
        main.Games.Add(fixture);
        try
        {
            library.SelectedItem = fixture;
            Update(module.Status);
            Check(main.Features.Count(f => f.HotkeyVisibility == System.Windows.Visibility.Visible) == 15, "expected 15 visible key labels");
            Apply("F1"); Apply("F1");
            Check(module.Commands.SequenceEqual(new[] { "civ6_movement=True", "civ6_movement=False" }), "toggle keys did not use confirmed current state");
            var gold = main.Features.Single(f => f.Definition.Id == "civ6_gold");
            gold.NumberText = "12345";
            Apply("Shift+F1");
            Check(module.Numbers.Single() == ("civ6_gold", 12345m), "numeric key did not apply the editor's current value");
            gold.NumberText = "NaN"; Apply("Shift+F1");
            Check(module.Numbers.Count == 1 && gold.NumberErrorVisibility == System.Windows.Visibility.Visible, "numeric hotkey bypassed validation");
            var unavailable = new Dictionary<string, string>(module.Status.Values) { ["available_civ6_gold"] = "false" };
            gold.NumberText = "42";
            Update(module.Status with { Values = unavailable }); Apply("Shift+F1");
            Check(module.Numbers.Count == 1, "unavailable numeric editor was invoked");
            var before = module.Commands.Count;
            Update(module.Status, working: true); Apply("F1"); Apply("F12");
            Check(module.Commands.Count == before, "busy command was dispatched");
            Update(module.Status);
            handoff.SetValue(main, true); Apply("F1"); Apply("F12");
            Check(module.Commands.Count == before, "update handoff dispatched a command");
            handoff.SetValue(main, originalHandoff);
            Update(module.Status with { Connected = false, Ready = false }); Apply("F1"); Apply("F12");
            Check(module.Commands.Count == before, "disconnected command was dispatched");
            Update(module.Status with { Connected = true, Ready = false }); Apply("F12");
            Check(module.Commands.Last() == "disable_all=", "F12 could not disable effects between turns");
            Apply("Shift+F12"); Apply("F99");
            Check(module.Commands.Count == before + 1, "unmapped key dispatched");
            library.SelectedItem = main.Games.First(game => game.Id != "civilization-vi");
            Apply("F1");
            Check(module.Commands.Count == before + 1, "Civ6 key was routed from another game page");
        }
        finally
        {
            busy.SetValue(main, originalBusy);
            handoff.SetValue(main, originalHandoff);
            library.SelectedItem = original;
            main.Games.Remove(fixture);
            refresh.Invoke(main, null);
        }
        return new { Suite = "civilization-vi-hotkeys", Assertions = assertions, NativeGameInputTested = false };
    }

    private sealed class FixtureModule : IGameModule, IFeatureValueModule
    {
        public GameDefinition Definition { get; }
        public GameInstallation Installation { get; } = new("C:/Civ6HotkeyFixture", "fixture", true, true, true, true, "Fixture only");
        public TrainerStatus Status { get; set; }
        public List<string> Commands { get; } = new();
        public List<(string Id, decimal Value)> Numbers { get; } = new();
        public FixtureModule(GameDefinition definition)
        {
            Definition = definition;
            Status = new(true, true, "Fixture only", "test-session",
                definition.Features.Where(f => f.IsToggle).ToDictionary(f => f.Id, _ => false),
                definition.Features.ToDictionary(f => "available_" + f.Id, f => string.IsNullOrEmpty(f.Hotkey) ? "false" : "true"));
        }
        public Task SendAsync(GameInstallation installation, TrainerStatus status, string featureId, bool? enabled = null)
        {
            Commands.Add(featureId + "=" + enabled);
            var effects = new Dictionary<string, bool>(Status.Enabled);
            if (enabled.HasValue) effects[featureId] = enabled.Value;
            if (featureId == "disable_all") foreach (var key in effects.Keys) effects[key] = false;
            Status = Status with { Enabled = effects };
            return Task.CompletedTask;
        }
        public Task SetValueAsync(GameInstallation installation, TrainerStatus status, string featureId, decimal value)
        { Numbers.Add((featureId, value)); return Task.CompletedTask; }
        public Task<TrainerStatus> ReadStatusAsync(GameInstallation installation) => Task.FromResult(Status);
        public Task<GameInstallation> DiscoverAsync(string? directory = null) => throw new NotSupportedException();
        public Task InstallAsync(GameInstallation installation, IProgress<string>? progress = null) => throw new NotSupportedException();
        public Task RemoveAsync(GameInstallation installation, IProgress<string>? progress = null) => throw new NotSupportedException();
        public Task LaunchAsync(GameInstallation installation) => throw new NotSupportedException();
    }
}
