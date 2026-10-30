using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Threading;
using HappyCheats.App;
using HappyCheats.Core;

namespace HappyCheats.App.Tests;

internal static class ControllerMenuChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static object Run(string root)
    {
        var settings = Path.Combine(Path.GetTempPath(), "HappyCheats.App.Tests", Guid.NewGuid().ToString("N"), "settings.json");
        var originalLanguage = Localization.Language;
        var originalTheme = ThemeManager.IsLight ? "light" : "dark";
        // Neither InitializeAsync nor Show is called: no discovery, input timer,
        // keyboard hook, updater request, game launch or game process is involved.
        var main = new MainWindow(dataRoot: root, settingsPath: settings);
        var host = new Window { Left = -32000, Top = -32000, Width = 800, Height = 480,
            ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        using var http = new HttpClient();
        var update = UpdateWindow.CreatePreview(http);
        var assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("Controller menu: " + message);
        }
        var controller = new ControllerInput((int _, out ushort buttons) => { buttons = 0; return false; }, () => 0);
        Set(main, "_controller", controller);
        var fixtures = new List<FixtureModule>();
        try
        {
            Drain(main);
            var checkbox = LogicalDescendants(main).OfType<CheckBox>().Single(control =>
                BindingOperations.GetBinding(control, ToggleButton.IsCheckedProperty)?.Path.Path == nameof(MainWindow.ControllerEnabled));
            var panel = LogicalDescendants(main).OfType<Border>().Single(control =>
                BindingOperations.GetBinding(control, UIElement.VisibilityProperty)?.Path.Path == nameof(MainWindow.ControllerVisibility));
            var catalog = main.Games.ToArray();
            foreach (var language in new[] { "en", "zh" })
            {
                main.SetLanguage(language, persist: false);
                foreach (var game in catalog)
                {
                    main.SelectGame(game.Id);
                    Drain(main);
                    Check(main.ControllerVisibility == Visibility.Visible && panel.Visibility == Visibility.Visible,
                        game.Id + " does not expose the controller checkbox in " + language);
                    Check(checkbox.IsChecked == true && !string.IsNullOrWhiteSpace(checkbox.Content?.ToString()),
                        game.Id + " has an unlabeled or disabled controller preference in " + language);
                    Check(controller.Profile == (game.Id is "onimusha" or "dawnwalker" ? game.Id : "shared"),
                        game.Id + " retained another game's controller profile");
                    var hints = main.ControllerDirections + " " + main.ControllerBumpers;
                    Check(hints.Contains("Menu", StringComparison.Ordinal) && hints.Contains("RB", StringComparison.Ordinal),
                        game.Id + " does not explain the shared menu controller chord in " + language);
                    Check(!string.IsNullOrWhiteSpace(main.ControllerLabel), game.Id + " lost the controller connection status");
                }
            }

            // Both legacy shortcut profiles and an unknown future module must
            // route the shared action to the menu, never to a trainer HUD command.
            main.Games.Clear();
            foreach (var id in new[] { "onimusha", "dawnwalker", "future-controller-fixture" })
            {
                var module = new FixtureModule(id);
                fixtures.Add(module);
                var game = new GameItem(module) {
                    Installation = new GameInstallation("C:/HappyCheats-Inert-Controller-Fixture", "fixture", true, true, true, true, "Inert fixture only."),
                    Status = module.Status
                };
                main.Games.Add(game);
                main.SelectGame(id);
                Drain(main);
                var hostHandle = new WindowInteropHelper(host).EnsureHandle();
                Set(main, "_gameWindow", hostHandle);
                Set(main, "_busy", false);
                Set(main, "_controllerEnabled", true);
                Invoke(main, "RefreshBindings");
                Check(main.ControllerVisibility == Visibility.Visible && controller.Profile == (id == "future-controller-fixture" ? "shared" : id),
                    id + " requires an explicit catalog allowlist for controller menu support");

                Dispatch(main, "game_menu");
                var overlay = (GameOverlay?)Get(main, "_gameOverlay");
                Check((bool)Get(main, "_overlayRequested")! && overlay?.CurrentGameId == id,
                    id + " did not open the selected game's shared menu");
                Check(module.Calls == 0, id + " sent a trainer command while opening the menu");
                overlay!.Left = overlay.Top = -32000;
                overlay.Show();
                Drain(main);
                var mainHandle = new WindowInteropHelper(main).EnsureHandle();
                var overlayHandle = new WindowInteropHelper(overlay).Handle;
                Check(MenuScope(main, hostHandle) && MenuScope(main, mainHandle) && MenuScope(main, overlayHandle),
                    id + " rejected the matched game, client or shared menu foreground window");
                Check(!MenuScope(main, IntPtr.Zero) && !MenuScope(main, new IntPtr(-1)),
                    id + " accepted an absent or unrelated foreground window");

                game.Status = TrainerStatus.Offline("Synthetic trainer disconnect.");
                Set(main, "_busy", true);
                Invoke(main, "RefreshBindings");
                Check(MenuScope(main, hostHandle), id + " gated menu focus on trainer readiness or client busy state");
                Dispatch(main, "game_menu");
                Check(!(bool)Get(main, "_overlayRequested")! && !overlay.IsVisible,
                    id + " could not close the menu while the trainer was offline and the client was busy");
                Dispatch(main, "game_menu");
                Check((bool)Get(main, "_overlayRequested")! && ReferenceEquals(overlay, Get(main, "_gameOverlay")),
                    id + " could not reopen the shared menu independently of trainer readiness");
                Check(module.Calls == 0, id + " bypassed trainer availability while toggling the menu");

                Set(main, "_controllerEnabled", false);
                Invoke(main, "RefreshBindings");
                Dispatch(main, "game_menu");
                Check((bool)Get(main, "_overlayRequested")! && checkbox.IsChecked == false,
                    id + " dispatched the menu chord after controller shortcuts were disabled");
                Check(main.ControllerLabel == Localization.Text("controllerDisabled"),
                    id + " did not explain that controller shortcuts are disabled");
                Set(main, "_controllerEnabled", true);
                Dispatch(main, "game_menu");
                Check(!(bool)Get(main, "_overlayRequested")!, id + " failed to resume controller menu toggling");

                Set(main, "_gameWindow", IntPtr.Zero);
                Dispatch(main, "game_menu");
                Check(!(bool)Get(main, "_overlayRequested")! && !MenuScope(main, mainHandle),
                    id + " allowed a menu without a matched game window");
                Set(main, "_gameWindow", hostHandle);
                Set(main, "_updateHandoff", true);
                Dispatch(main, "game_menu");
                Check(!(bool)Get(main, "_overlayRequested")! && !MenuScope(main, hostHandle),
                    id + " allowed a menu during updater handoff");
                Set(main, "_updateHandoff", false);
                Set(main, "_updateWindow", update);
                Dispatch(main, "game_menu");
                Check((bool)Get(main, "_overlayRequested")! && MenuScope(main, hostHandle),
                    id + " blocked the shared menu while the Updates window was open");
                Dispatch(main, "game_menu");
                Check(!(bool)Get(main, "_overlayRequested")!, id + " could not close the shared menu while Updates was open");
                Set(main, "_installingUpdate", true);
                Dispatch(main, "game_menu");
                Check(!(bool)Get(main, "_overlayRequested")! && !MenuScope(main, hostHandle),
                    id + " allowed a menu while preparing update installation");
                Set(main, "_installingUpdate", false);
                Set(main, "_updateWindow", null);
                Check(module.Calls == 0, id + " performed a game operation during controller menu checks");
            }
            Set(main, "_selected", null);
            Dispatch(main, "game_menu");
            Check(!(bool)Get(main, "_overlayRequested")! && !MenuScope(main, new WindowInteropHelper(host).Handle),
                "a controller menu action ran without a selected module");
            Check(!File.Exists(settings), "checks persisted controller preferences or other settings");
            Check(fixtures.All(fixture => fixture.Calls == 0), "checks reached a game, trainer, installation or discovery action");
            return new { Screen = "controller-shared-menu", Assertions = assertions, CatalogGames = catalog.Length,
                EnglishAndChinese = true, FutureModule = true, SharedMenuNotTrainerCommand = true,
                OfflineAndBusyToggle = true, DisabledControllerBlocked = true, ForegroundScope = true,
                ModelessUpdatesAllowMenu = true, InstallationAndMissingWindowBlocked = true,
                NoGameOrNetworkActions = true };
        }
        finally
        {
            Set(main, "_updateWindow", null);
            Set(main, "_installingUpdate", false);
            main.Close();
            host.Close();
            update.Close();
            Localization.Language = originalLanguage;
            ThemeManager.Apply(originalTheme);
        }
    }

    private static IEnumerable<DependencyObject> LogicalDescendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var descendant in LogicalDescendants(child)) yield return descendant;
    }

    private static void Drain(MainWindow main) => main.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static object? Get(MainWindow main, string name) => typeof(MainWindow).GetField(name, Private)!.GetValue(main);
    private static void Set(MainWindow main, string name, object? value) => typeof(MainWindow).GetField(name, Private)!.SetValue(main, value);
    private static object? Invoke(MainWindow main, string name, params object[] arguments) =>
        main.Dispatcher.Invoke(() => typeof(MainWindow).GetMethod(name, Private)!.Invoke(main, arguments));
    private static bool MenuScope(MainWindow main, IntPtr window) => (bool)Invoke(main, "IsControllerMenuScope", window)!;
    private static void Dispatch(MainWindow main, string action)
    {
        var task = (Task)Invoke(main, "DispatchControllerActionAsync", action)!;
        if (!task.IsCompleted) throw new InvalidOperationException("A menu-only action unexpectedly awaited a trainer operation.");
        task.GetAwaiter().GetResult();
        Drain(main);
    }

    private sealed class FixtureModule(string id) : IGameModule
    {
        public GameDefinition Definition { get; } = new(id, "Inert controller fixture", "Fixture", "0", "CF", "No game actions.", [
            new("hud", "Native trainer status", "Must not receive the shared menu action.", "F6", true),
            new("fixture_feature", "Inert feature", "Unavailable offline.", "F1", true)
        ]);
        public IReadOnlyList<string> GameExecutables => ["Fixture.exe"];
        public TrainerStatus Status { get; } = new(true, true, "Inert connected fixture", "fixture-session",
            new Dictionary<string, bool>(), new Dictionary<string, string>());
        public int Calls { get; private set; }
        private Exception Unexpected() { Calls++; return new InvalidOperationException("Controller menu fixture forbids game operations."); }
        public Task<GameInstallation> DiscoverAsync(string? directory = null) => throw Unexpected();
        public Task InstallAsync(GameInstallation installation, IProgress<string>? progress = null) => throw Unexpected();
        public Task RemoveAsync(GameInstallation installation, IProgress<string>? progress = null) => throw Unexpected();
        public Task<TrainerStatus> ReadStatusAsync(GameInstallation installation) => throw Unexpected();
        public Task SendAsync(GameInstallation installation, TrainerStatus status, string featureId, bool? enabled = null) => throw Unexpected();
        public Task LaunchAsync(GameInstallation installation) => throw Unexpected();
    }
}
