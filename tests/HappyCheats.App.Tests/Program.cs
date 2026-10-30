using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HappyCheats.App;
using HappyCheats.Core;

namespace HappyCheats.App.Tests;

internal static class Program
{
    private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly List<string> Failures = new();
    private static readonly List<object> Results = new();
    private static int assertions;

    [STAThread]
    private static int Main(string[] args)
    {
        var artifacts = Option(args, "--artifacts");
        if (artifacts != null) { artifacts = Path.GetFullPath(artifacts); Directory.CreateDirectory(artifacts); }
        var root = Path.GetFullPath(Option(args, "--data-root") ?? Directory.GetCurrentDirectory());
        var settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HappyCheats", "settings.json");
        var originalSettings = Hash(settings);
        // The settings path is deliberately absent and capture mode never writes it.
        var isolatedSettings = Path.Combine(Path.GetTempPath(), "HappyCheats.App.Tests", Guid.NewGuid().ToString("N"), "settings.json");
        var app = CreateAppWithoutStartup();
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        EventManager.RegisterClassHandler(typeof(UpdateWindow), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                // Exercise the actual window lifecycle; hide only the native surface.
                // This avoids displaying a test window when WindowLayout fits it to the monitor.
                if (sender is UpdateWindow window) ShowWindow(new WindowInteropHelper(window).Handle, 0);
            }));
        var main = new MainWindow(captureMode: true, dataRoot: root, settingsPath: isolatedSettings)
        {
            ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -32000, Top = -32000
        };
        try
        {
            main.Show();
            main.InitializeAsync(preview: true).GetAwaiter().GetResult();
            Layout(main);
            Check(app.Windows.Count == 1, "App startup unexpectedly created another window.");
            if (args.Contains("--crimson"))
            {
                ValidateCrimsonAutoTeleport(main);
                ValidateCrimsonUnavailableReasons(main, artifacts);
                ValidateCrimsonInitializationFailure(main);
                ValidateCrimsonTeleportReporting(main);
            }
            else if (args.Contains("--witcher3")) Results.Add(Witcher3UiChecks.Run(main, artifacts));
            else if (args.Contains("--civ7")) Results.Add(Civ7UiChecks.Run(main, artifacts));
            else if (args.Contains("--elden-ring")) Results.Add(EldenRingUiChecks.Run(main, artifacts));
            else if (args.Contains("--phantom-blade")) Results.Add(PhantomBladeUiChecks.Run(main, artifacts));
            else if (args.Contains("--background-updates")) Results.Add(BackgroundUpdateUiChecks.Run(root));
            else
            {
            Results.Add(LibrarySortChecks.Run(main, root, artifacts, Check));
            foreach (var size in new[] { (Width: 1180, Height: 820), (Width: 854, Height: 534), (Width: 800, Height: 480) })
            foreach (var theme in new[] { "dark", "light" })
            foreach (var language in new[] { "en", "zh" })
            {
                main.Width = size.Width; main.Height = size.Height;
                main.SetLanguage(language, persist: false);
                main.SetTheme(theme, persist: false);
                main.SelectGame("stardew");
                Layout(main);
                var library = (ListBox)main.FindName("Library");
                var selected = library.SelectedItem;
                library.ScrollIntoView(selected);
                Layout(main);
                for (var repeat = 0; repeat < 2; repeat++)
                {
                    var name = $"{theme}-{language}-{size.Width}x{size.Height}-open{repeat + 1}";
                    var before = InspectLibrary(library);
                    Check(main.CanManage && library.IsEnabled, $"{name}: library did not begin enabled.");
                    OpenUpdatesAndInspect(main, library, theme, name, size.Width, size.Height,
                        repeat == 0 ? artifacts : null, before);
                    Layout(main);
                    Check(main.CanManage && library.IsEnabled, $"{name}: closing Updates disabled library controls.");
                    Check(ReferenceEquals(library.SelectedItem, selected) && main.Selected?.Id == "stardew", $"{name}: selection changed across modeless Updates.");
                    Check(typeof(MainWindow).GetField("_updateWindow", Private)!.GetValue(main) == null, $"{name}: closed updater reference was not cleared.");
                    ValidateScrolling(library, name);
                    Check(((ScrollViewer)main.FindName("DetailScroll")).PanningMode == PanningMode.VerticalOnly, $"{name}: detail touch panning changed.");
                }
            }
            Results.Add(GameOverlayChecks.Run(main, root, artifacts));
            Results.Add(GameMenuLifecycleChecks.Run(root));
            Results.Add(ControllerMenuChecks.Run(root));
            Results.Add(BackgroundUpdateUiChecks.Run(root));
            ValidateCrimsonAutoTeleport(main);
            ValidateCrimsonUnavailableReasons(main, artifacts);
            ValidateWukongControls(main, artifacts);
            ValidateRdr2Controls(main, artifacts);
            Results.Add(GtaVUiChecks.Run(main, artifacts));
            Results.Add(ProjectHospitalUiChecks.Run(main, artifacts));
            Results.Add(HotelArchitectUiChecks.Run(main, artifacts));
            Results.Add(Civ6UiChecks.Run(main, artifacts));
            Results.Add(Civ6HotkeyChecks.Run(main));
            Results.Add(Civ7UiChecks.Run(main, artifacts));
            Results.Add(EldenRingUiChecks.Run(main, artifacts));
            Results.Add(PhantomBladeUiChecks.Run(main, artifacts));
            Results.Add(Witcher3UiChecks.Run(main, artifacts));
            ValidateCrimsonInitializationFailure(main);
            ValidateCrimsonTeleportReporting(main);
            }
        }
        catch (Exception error) { Failures.Add(error.ToString()); }
        finally
        {
            foreach (var window in app.Windows.OfType<UpdateWindow>().ToArray()) window.Close();
            main.Close();
            app.Shutdown();
        }
        Check(Hash(settings) == originalSettings, "User settings changed during preview UI tests.");
        Check(!File.Exists(isolatedSettings), "Capture-mode test unexpectedly wrote its isolated settings.");
        if (artifacts != null)
            File.WriteAllText(Path.Combine(artifacts, "verification.json"), JsonSerializer.Serialize(new
            {
                Assembly = typeof(App).Assembly.GetName().FullName, ActualUpdatesClickPath = true,
                StartupCallbackSuppressed = true, NoGameOrNetworkActions = true, SettingsUnchanged = Hash(settings) == originalSettings,
                Assertions = assertions, Cases = Results, Failures
            }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"HappyCheats WPF regression: {Results.Count} cases, {assertions} assertions, {Failures.Count} failures.");
        foreach (var failure in Failures) Console.WriteLine("FAIL " + failure);
        return Failures.Count == 0 ? 0 : 1;
    }

    private static void ValidateRdr2Controls(MainWindow main, string? artifacts)
    {
        main.SetLanguage("en", persist: false);
        main.SelectGame("red-dead-redemption-2");
        main.Width = 1180; main.Height = 820;
        Layout(main);
        var game = main.Selected!;
        Check(game.Id == "red-dead-redemption-2" && game.Module.Definition.AppId == "1174180", "RDR2 is missing from the library.");
        foreach (var uri in new[] { GameArtwork.IconUri(game.Id), GameArtwork.LogoUri(game.Id) })
        {
            var image = BitmapFrame.Create(uri, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Check(image.PixelWidth > 0 && image.PixelHeight > 0, "RDR2 artwork failed to load.");
        }
        var expected = new[] { "rdr2_health", "rdr2_stamina", "rdr2_deadeye", "rdr2_ammo", "rdr2_horse_health", "rdr2_horse_stamina", "rdr2_clear_wanted" };
        Check(main.Features.Select(item => item.Definition.Id).SequenceEqual(expected), "RDR2 controls are incomplete or out of order.");
        Check(main.Features.Take(6).All(item => item.Definition.IsToggle) && !main.Features.Last().Definition.IsToggle, "RDR2 control types are incorrect.");
        var original = game.Status;
        var originalInstallation = game.Installation;
        var busyField = typeof(MainWindow).GetField("_busy", Private)!;
        var originalBusy = (bool)busyField.GetValue(main)!;
        var refresh = typeof(MainWindow).GetMethod("RefreshBindings", Private)!;
        void Update(TrainerStatus status, bool busy = false)
        {
            game.Status = status; busyField.SetValue(main, busy); refresh.Invoke(main, null); Layout(main);
        }
        try
        {
            const string runtimeSetup = "Install / update will download and verify the official Script Hook RDR2 and ASI loader, then install the Story Mode trainer.";
            game.Installation = new GameInstallation("fixture-rdr2", "13773296", true, false, false, true, runtimeSetup);
            Update(TrainerStatus.Offline(runtimeSetup));
            Check(main.CanInstall, "RDR2 missing downloadable runtime must not disable Install / update.");
            game.Installation = game.Installation with { Compatible = false, Message = Rdr2RuntimePackage.ConflictMessage };
            Update(TrainerStatus.Offline(Rdr2RuntimePackage.ConflictMessage));
            Check(!main.CanInstall, "RDR2 conflicting runtime must disable installation.");
            var active = new TrainerStatus(true, false, "Waiting for player", "rdr2-ui",
                main.Features.Take(6).ToDictionary(item => item.Definition.Id, _ => true),
                expected.ToDictionary(id => "available_" + id, _ => "False"));
            Update(active);
            Check(main.CanDisableAll && main.Features.Take(6).All(item => item.CanUse) && !main.Features.Last().CanUse,
                "RDR2 must allow cleanup without player readiness but block wanted clearing.");
            Update(active, busy: true);
            Check(!main.CanDisableAll && main.Features.All(item => !item.CanUse), "RDR2 commands remained usable while busy.");
            Update(active with { Connected = false });
            Check(!main.CanDisableAll && main.Features.All(item => !item.CanUse), "Disconnected RDR2 controls remained enabled.");
        }
        finally { game.Installation = originalInstallation; Update(original, originalBusy); }
        foreach (var language in new[] { "en", "zh" })
        foreach (var theme in new[] { "dark", "light" })
        {
            main.SetLanguage(language, persist: false); main.SetTheme(theme, persist: false); Layout(main);
            Check(main.Features.All(feature => Descendants<Button>(main).Any(button => ReferenceEquals(button.Tag, feature))), "An RDR2 action is missing from the panel.");
            if (language == "zh")
            {
                Check(game.Name == "荒野大镖客：救赎 2" && game.HeroLine.Contains("不支持官方线上模式") && game.HeroLine.Contains("尚待游戏内验证"), "RDR2 Chinese scope/validation labels are missing.");
                Check(game.HeroLine.Contains("自动配置") && game.HeroLine.Contains("1.0.1491.50") && game.HeroLine.Contains("首次配置需联网"), "RDR2 automatic runtime setup translation is missing.");
                Check(main.Features.All(feature => feature.Name != feature.Definition.Name && feature.Description != feature.Definition.Description), "RDR2 feature translation is missing.");
            }
            if (artifacts != null) { ((ScrollViewer)main.FindName("DetailScroll")).ScrollToTop(); Layout(main); Capture(main, Path.Combine(artifacts, $"rdr2-{theme}-{language}.png")); }
        }
        Results.Add(new { Screen = "rdr2-controls", Controls = 7, EnglishAndChinese = true, CleanupWithoutPlayerReadiness = true, NoGameCommands = true });
    }

    private static void ValidateWukongControls(MainWindow main, string? artifacts)
    {
        main.SetLanguage("en", persist: false);
        main.SelectGame("black-myth-wukong");
        main.Width = 1180; main.Height = 820;
        Layout(main);
        Check(main.Selected?.Id == "black-myth-wukong", "Black Myth: Wukong is missing from the library.");
        var game = main.Selected!;
        Check(game.Name == "Black Myth: Wukong" && game.Module.Definition.AppId == "2358720",
            "Black Myth: Wukong's display name or Steam identity is incorrect.");
        foreach (var uri in new[] { GameArtwork.IconUri(game.Id), GameArtwork.LogoUri(game.Id) })
        {
            var image = BitmapFrame.Create(uri, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Check(image.PixelWidth >= 256 && image.PixelHeight >= 256,
                "Black Myth: Wukong's bundled artwork is missing or has unexpectedly low resolution.");
        }
        var expected = new[] { "wukong_health", "wukong_mana", "wukong_stamina", "wukong_damage", "wukong_gourd",
            "wukong_focus", "wukong_xp", "wukong_sparks", "wukong_will" };
        Check(main.Features.Select(item => item.Definition.Id).SequenceEqual(expected), "The Wukong panel must expose all nine requested controls.");
        Check(main.Features.Select(item => item.Hotkey).SequenceEqual(Enumerable.Range(1, 9).Select(number => $"F{number}")),
            "Wukong's F1–F9 shortcuts do not match the control order.");
        Check(main.Features.Take(5).All(item => item.Definition.IsToggle)
            && main.Features.Skip(5).All(item => !item.Definition.IsToggle), "Wukong's toggles and one-shot actions have incorrect control types.");
        foreach (var (id, amount) in new[] { ("wukong_xp", 1000m), ("wukong_sparks", 1m), ("wukong_will", 1000m) })
        {
            var feature = main.Features.Single(item => item.Definition.Id == id);
            Check(feature.NumberVisibility == Visibility.Visible && feature.NumberCaption == "Amount"
                && feature.TryGetNumber(out var value) && value == amount, $"{id}: numeric grant input or default is incorrect.");
            feature.NumberText = "-1";
            Check(!feature.TryGetNumber(out _), $"{id}: progression inputs must reject negative grants.");
            feature.NumberText = amount.ToString(System.Globalization.CultureInfo.CurrentCulture);
        }
        var originalStatus = game.Status;
        var busyField = typeof(MainWindow).GetField("_busy", Private)!;
        var originalBusy = (bool)busyField.GetValue(main)!;
        var refresh = typeof(MainWindow).GetMethod("RefreshBindings", Private)!;
        void Update(TrainerStatus status, bool busy = false)
        {
            game.Status = status;
            busyField.SetValue(main, busy);
            refresh.Invoke(main, null);
            Layout(main);
        }
        try
        {
            var activeWithoutPlayer = new TrainerStatus(true, false, "Waiting for player", "wukong-ui-test",
                main.Features.Take(5).ToDictionary(feature => feature.Definition.Id, _ => true),
                main.Features.ToDictionary(feature => "available_" + feature.Definition.Id, _ => "False"));
            Update(activeWithoutPlayer);
            Check(main.CanDisableAll && main.Features.Take(5).All(feature => feature.CanUse && feature.ActionLabel == "Turn off")
                && main.Features.Skip(5).All(feature => !feature.CanUse),
                "Wukong must allow active-toggle cleanup after player readiness is lost, while blocking one-shot actions.");
            Update(activeWithoutPlayer with { Ready = true });
            Check(main.Features.Take(5).All(feature => feature.CanUse) && main.Features.Skip(5).All(feature => !feature.CanUse),
                "Wukong's missing capability prevented owned-toggle cleanup or enabled unsupported actions.");
            Update(activeWithoutPlayer, busy: true);
            Check(!main.CanDisableAll && main.Features.All(feature => !feature.CanUse),
                "Wukong controls remained clickable while a command was pending.");
            Update(activeWithoutPlayer with { Connected = false });
            Check(!main.CanDisableAll && main.Features.All(feature => !feature.CanUse),
                "Wukong controls remained usable after disconnecting.");
        }
        finally { Update(originalStatus, originalBusy); }
        foreach (var language in new[] { "en", "zh" })
        foreach (var theme in new[] { "dark", "light" })
        {
            main.SetLanguage(language, persist: false);
            main.SetTheme(theme, persist: false);
            Layout(main);
            Check(main.Features.All(feature => Descendants<Button>(main).Any(button => ReferenceEquals(button.Tag, feature))),
                $"Wukong {language}/{theme}: a trainer action is missing from the bound panel.");
            if (language == "zh")
            {
                Check(game.Name == "黑神话：悟空" && game.HeroLine.Contains("尚待游戏内验证", StringComparison.Ordinal),
                    "Wukong's Chinese title or gameplay-validation limitation is missing.");
                Check(main.Features.All(feature => feature.Name != feature.Definition.Name && feature.Description != feature.Definition.Description),
                    "A Wukong feature is missing its Chinese name or description.");
                Check(main.Features.Where(feature => feature.Definition.Number != null).All(feature => feature.NumberCaption == "增加数量"
                    && feature.NumberUnit != feature.Definition.Number!.Unit), "Wukong's Chinese grant captions or units are missing.");
                foreach (var (id, name, unit) in new[] { ("wukong_xp", "增加经验", "经验"),
                    ("wukong_sparks", "增加灵光点", "灵光点"), ("wukong_will", "增加灵蕴", "灵蕴") })
                {
                    var feature = main.Features.Single(item => item.Definition.Id == id);
                    Check(feature.Name == name && feature.NumberUnit == unit, $"{id}: the Chinese progression label or unit is incorrect.");
                }
                Check(Localization.ValueLabel("wukong_stamina") == "气力" && Localization.ValueLabel("wukong_stamina_max") == "气力上限"
                    && Localization.ValueLabel("stamina") == "体魄", "Wukong's runtime stamina labels must not change Chinese Parents' attribute label.");
                Check(Localization.Message("Progression command sent once; check the in-game total.") == "已发送一次成长指令；请检查游戏内的总数值。",
                    "Wukong's localized progression acknowledgement must preserve the request to check the in-game total.");
            }
            if (artifacts != null)
            {
                var detail = (ScrollViewer)main.FindName("DetailScroll");
                detail.ScrollToTop(); Layout(main);
                Capture(main, Path.Combine(artifacts, $"black-myth-wukong-{theme}-{language}.png"));
                detail.ScrollToBottom(); Layout(main);
                Capture(main, Path.Combine(artifacts, $"black-myth-wukong-progression-{theme}-{language}.png"));
                detail.ScrollToTop(); Layout(main);
            }
        }
        Results.Add(new { Screen = "black-myth-wukong-controls", Controls = 9, NumericGrants = 3, BundledArtwork = true,
            EnglishAndChinese = true, LightAndDark = true, CleanupWithoutPlayerReadiness = true, NoGameCommands = true });
    }

    private static void ValidateCrimsonAutoTeleport(MainWindow main)
    {
        main.SetLanguage("en", persist: false);
        main.SelectGame("crimson-desert");
        Layout(main);
        var game = main.Selected!;
        var originalStatus = game.Status;
        var busyField = typeof(MainWindow).GetField("_busy", Private)!;
        var originalBusy = (bool)busyField.GetValue(main)!;
        var refresh = typeof(MainWindow).GetMethod("RefreshBindings", Private)!;
        var feature = main.Features.Single(item => item.Definition.Id == "crimson_auto_teleport");
        var manual = main.Features.Single(item => item.Definition.Id == "crimson_teleport");
        var button = Descendants<Button>(main).Single(item => ReferenceEquals(item.Tag, feature));
        var manualButton = Descendants<Button>(main).Single(item => ReferenceEquals(item.Tag, manual));
        // Availability comes from the native capability, before any marker exists.
        // Manual teleport is deliberately unavailable in the same synthetic status.
        var waitingForMarker = new TrainerStatus(true, true, "Place a new custom marker", "crimson-ui-test",
            new Dictionary<string, bool>(), new Dictionary<string, string>
            {
                ["available_crimson_auto_teleport"] = "True",
                ["available_crimson_teleport"] = "False"
            });
        void Update(TrainerStatus status, bool busy = false)
        {
            game.Status = status;
            busyField.SetValue(main, busy);
            refresh.Invoke(main, null);
            Layout(main);
        }
        try
        {
            Update(waitingForMarker);
            Check(feature.Definition.IsToggle && feature.Hotkey == "F9" && !manual.Definition.IsToggle,
                "Crimson auto teleport must remain an F9 toggle independent of manual teleport.");
            Check(feature.Name == "Auto teleport" && feature.CanUse && button.IsEnabled
                && !manual.CanUse && !manualButton.IsEnabled,
                "Crimson auto teleport cannot be enabled before placing a marker, or inherits manual availability.");
            Check((string)button.Content == "Turn on", "Inactive Crimson auto teleport has the wrong bound action label.");
            var unavailable = waitingForMarker with { Values = new Dictionary<string, string>
            {
                ["available_crimson_auto_teleport"] = "False", ["available_crimson_teleport"] = "True"
            } };
            Update(unavailable);
            Check(!feature.CanUse && !button.IsEnabled && manual.CanUse && manualButton.IsEnabled,
                "Crimson auto teleport ignored its own unavailable capability or disabled the independent manual action.");
            Update(waitingForMarker with { Ready = false });
            Check(!feature.CanUse && !button.IsEnabled, "Inactive Crimson auto teleport remained usable without gameplay readiness.");
            var activeUnavailable = unavailable with { Enabled = new Dictionary<string, bool> { [feature.Definition.Id] = true } };
            Update(activeUnavailable);
            Check(feature.CanUse && button.IsEnabled && (string)button.Content == "Turn off",
                "Active Crimson auto teleport cannot be turned off after its capability becomes unavailable.");
            Update(activeUnavailable with { Ready = false });
            Check(feature.CanUse && button.IsEnabled && (string)button.Content == "Turn off" && !manualButton.IsEnabled,
                "Active Crimson auto teleport cannot be turned off after gameplay readiness is lost.");
            Update(activeUnavailable with { Ready = false }, busy: true);
            Check(!feature.CanUse && !button.IsEnabled, "Crimson auto teleport cleanup remained clickable while a command was pending.");
            Update(activeUnavailable with { Ready = false, Connected = false });
            Check(!feature.CanUse && !button.IsEnabled, "Crimson auto teleport cleanup remained clickable after disconnecting.");
            main.SetLanguage("zh", persist: false);
            Update(waitingForMarker);
            Check(feature.Name == "自动传送" && feature.Hotkey == "F9" && button.IsEnabled
                && Descendants<TextBlock>(main).Any(item => ReferenceEquals(item.DataContext, feature) && item.Text == "自动传送"),
                "Crimson auto teleport's translated name, F9 shortcut, or bound control is missing.");
            Results.Add(new { Screen = "crimson-auto-teleport-availability", EnableBeforeMarker = true,
                IndependentManualAvailability = true, ActiveCleanupWithoutReadiness = true, NoGameCommands = true });
        }
        finally
        {
            game.Status = originalStatus;
            busyField.SetValue(main, originalBusy);
            refresh.Invoke(main, null);
        }
    }

    private static void ValidateCrimsonUnavailableReasons(MainWindow main, string? artifacts)
    {
        main.SetLanguage("en", persist: false);
        main.SelectGame("crimson-desert");
        Layout(main);
        var game = main.Selected!;
        var originalStatus = game.Status;
        var busyField = typeof(MainWindow).GetField("_busy", Private)!;
        var originalBusy = (bool)busyField.GetValue(main)!;
        var originalWidth = main.Width;
        var originalHeight = main.Height;
        var refresh = typeof(MainWindow).GetMethod("RefreshBindings", Private)!;
        var features = main.Features.Where(item => item.Definition.Id is "crimson_teleport" or "crimson_auto_teleport" or "crimson_speed").ToArray();
        var overlay = new GameOverlay(_ => Task.CompletedTask, _ => Task.CompletedTask, () => { })
        {
            Left = -32000, Top = -32000, Width = 380, Height = 448, ShowActivated = false, ShowInTaskbar = false
        };
        var reasons = new[]
        {
            ("The controlled client player is not recognized.", "无法识别当前受控角色的客户端对象。"),
            ("The controlled player is not recognized.", "无法识别当前受控角色。"),
            ("The player movement component is unavailable.", "角色移动组件不可用。"),
            ("The map teleport context is unavailable.", "地图传送对象或坐标暂不可用。"),
            ("Place a custom map marker first.", "请先放置自定义地图标记。"),
            ("The marker teleport hooks are unavailable.", "地图标记传送绑定不可用。"),
            ("The movement hook is unavailable.", "角色移动绑定不可用。")
        };
        TrainerStatus Unavailable(string reason) => new(true, true, "Connected", "crimson-reason-test",
            new Dictionary<string, bool>(), features.SelectMany(item => new[]
            {
                new KeyValuePair<string, string>("available_" + item.Definition.Id, bool.FalseString),
                new KeyValuePair<string, string>("reason_" + item.Definition.Id, reason)
            }).ToDictionary(pair => pair.Key, pair => pair.Value));
        void Update(TrainerStatus status, bool busy = false)
        {
            game.Status = status;
            busyField.SetValue(main, busy);
            refresh.Invoke(main, null);
            Layout(main);
        }
        try
        {
            foreach (var language in new[] { "en", "zh" })
            {
                main.SetLanguage(language, persist: false);
                foreach (var (english, chinese) in reasons)
                {
                    Update(Unavailable(english));
                    var expected = language == "en" ? english : chinese;
                    foreach (var feature in features)
                    {
                        Check(!feature.CanUse && feature.StateLabel == expected,
                            "Crimson unavailable reason was lost, untranslated, or enabled a control.");
                        Check(Descendants<Button>(main).Single(item => ReferenceEquals(item.Tag, feature)).IsEnabled == false
                            && Descendants<TextBlock>(main).Any(item => ReferenceEquals(item.DataContext, feature) && item.Text == expected),
                            "Crimson unavailable reason did not reach its disabled bound UI row.");
                    }
                    Check(!main.ValuesLine.Contains("reason_", StringComparison.OrdinalIgnoreCase)
                        && !main.ValuesLine.Contains(expected), "Crimson unavailable reason leaked into raw values.");
                }
            }
            main.SetLanguage("en", persist: false);
            var unavailable = Unavailable(reasons[0].Item1);
            Update(unavailable, busy: true);
            Check(features.All(item => !item.CanUse && item.StateLabel == "Please wait…"),
                "A Crimson unavailable reason replaced the pending-command status.");
            Update(unavailable with { Connected = false });
            Check(features.All(item => !item.CanUse && item.StateLabel != reasons[0].Item1),
                "A stale Crimson unavailable reason survived disconnect.");
            Update(unavailable with { Ready = false });
            Check(features.All(item => !item.CanUse && item.StateLabel == "Waiting for gameplay"),
                "A stale Crimson unavailable reason survived loss of readiness.");
            Update(unavailable with { Enabled = new Dictionary<string, bool> { ["crimson_auto_teleport"] = true } });
            var auto = features.Single(item => item.Definition.Id == "crimson_auto_teleport");
            Check(auto.CanUse && auto.ActionLabel == "Turn off" && auto.StateLabel == "●  Enabled",
                "A Crimson unavailable reason prevented active auto-teleport cleanup.");
            Update(unavailable with { Values = unavailable.Values.ToDictionary(pair => pair.Key,
                pair => pair.Key.StartsWith("available_", StringComparison.Ordinal) ? bool.TrueString : pair.Value) });
            Check(features.All(item => item.CanUse && item.StateLabel != reasons[0].Item1),
                "A stale Crimson unavailable reason survived restored availability.");
            foreach (var invalidReason in new[] { "", " ", new string('x', 161) })
            {
                Update(Unavailable(invalidReason));
                Check(features.All(item => !item.CanUse && item.StateLabel == "Unavailable in this scene"),
                    "A missing or unbounded Crimson reason did not use the safe fallback label.");
            }
            foreach (var language in new[] { "en", "zh" })
            {
                main.SetLanguage(language, persist: false);
                foreach (var (width, height) in new[] { (1180, 820), (800, 480) })
                {
                    main.Width = width; main.Height = height;
                    Update(unavailable);
                    var labels = Descendants<TextBlock>(main).Where(item => item.DataContext is FeatureItem feature
                        && features.Contains(feature) && item.Text == feature.StateLabel).ToArray();
                    Check(labels.Length == features.Length && labels.All(LabelFits), "Crimson reason is clipped in a main-window row.");
                    labels[0].BringIntoView(); Layout(main);
                    if (artifacts != null) Capture(main, Path.Combine(artifacts, $"crimson-reason-main-{width}-{language}.png"));
                }
                overlay.Refresh(game, features, false, true, "Synthetic unavailable-reason layout check");
                if (!overlay.IsVisible) overlay.Show();
                Layout(overlay);
                var overlayLabels = Descendants<TextBlock>(overlay).Where(item => item.Text == features[0].StateLabel).ToArray();
                Check(overlayLabels.Length == features.Length && overlayLabels.All(LabelFits), "Crimson reason is clipped in a shared-menu row.");
                Check(Descendants<Button>(overlay).Where(item => item.Tag is FeatureItem).All(item => !item.IsEnabled),
                    "The shared menu enabled a Crimson action while displaying an unavailable reason.");
                if (artifacts != null) Capture(overlay, Path.Combine(artifacts, $"crimson-reason-menu-{language}.png"));
            }
            Results.Add(new { Screen = "crimson-unavailable-reasons", EnglishAndChinese = true,
                NativeReasonsBound = true, MainAndSharedMenuLayout = true, AvailabilityUnchanged = true, CleanupPreserved = true, NoGameCommands = true });
        }
        finally
        {
            overlay.Close();
            main.Width = originalWidth; main.Height = originalHeight;
            game.Status = originalStatus;
            busyField.SetValue(main, originalBusy);
            refresh.Invoke(main, null);
        }
        static bool LabelFits(TextBlock label) => label.TextWrapping == TextWrapping.Wrap
            && label.ActualWidth > 0 && label.ActualHeight > 0
            && label.DesiredSize.Width <= label.ActualWidth + label.Margin.Left + label.Margin.Right + 1
            && label.DesiredSize.Height <= label.ActualHeight + label.Margin.Top + label.Margin.Bottom + 1;
    }

    private static void ValidateCrimsonInitializationFailure(MainWindow main)
    {
        main.SetLanguage("en", persist: false);
        main.SelectGame("crimson-desert");
        var selectedField = typeof(MainWindow).GetField("_selected", Private)!;
        var original = main.Selected!;
        var refresh = typeof(MainWindow).GetMethod("RefreshBindings", Private)!;
        var failed = new TrainerStatus(true, false, "Executable PE image size mismatch; no hooks installed.", "crimson-error-test",
            new Dictionary<string, bool>(), new Dictionary<string, string> { ["diagnostic_crimson_initialization_failed"] = bool.TrueString });
        var module = new ConnectStatusFixture(original.Module.Definition, failed);
        var game = new GameItem(module)
        {
            Installation = new GameInstallation("synthetic-no-game", "test", true, true, true, true, "Fixture"),
            Status = failed with { Ready = true, Message = "Game found. Choose Connect trainer.", Values = new Dictionary<string, string>() }
        };
        try
        {
            // Exercise the actual Connect UI handler using a fixture that cannot access a game.
            selectedField.SetValue(main, game);
            var connect = module.Definition.Features.Single(feature => feature.Id == "crimson_connect");
            var send = (Task)typeof(MainWindow).GetMethod("SendFeatureAsync", Private)!.Invoke(main, new object[] { connect })!;
            send.GetAwaiter().GetResult();
            Layout(main);
            Check(module.ConnectCalls == 1 && main.Message == failed.Message && main.ControlsHint == failed.Message,
                "Connect replaced the native initialization failure with a false applied-in-game confirmation.");
            Check(main.ConnectionLabel == "Initialization failed" && main.Features.All(feature => !feature.CanUse && feature.StateLabel == "Initialization failed"),
                "Native initialization failure was mislabeled as waiting for gameplay.");
            Check(Descendants<Button>(main).Where(button => button.Tag is FeatureItem).All(button => !button.IsEnabled),
                "Initialization failure left a trainer action enabled.");
            game.Status = failed with { Values = new Dictionary<string, string>() };
            refresh.Invoke(main, null);
            Check(main.ConnectionLabel == "Waiting for gameplay" && main.Features.All(feature => feature.StateLabel == "Waiting for gameplay"),
                "The initialization label leaked into an ordinary waiting status without the explicit diagnostic.");
            game.Status = failed;
            main.SetLanguage("zh", persist: false);
            Layout(main);
            Check(main.ConnectionLabel == "修改器初始化失败" && main.Features.All(feature => feature.StateLabel == "修改器初始化失败")
                && main.Message == "游戏程序的内存映像大小不匹配；未安装任何功能绑定。",
                "The initialization failure label or retained diagnostic was not translated.");
            Results.Add(new { Screen = "crimson-initialization-failure", NativeReasonRetained = true,
                AvailabilityUnchanged = true, SimulatedConnectCalls = module.ConnectCalls, NoGameCommands = true });
        }
        finally
        {
            selectedField.SetValue(main, original);
            refresh.Invoke(main, null);
        }
    }

    private static void ValidateCrimsonTeleportReporting(MainWindow main)
    {
        main.SetLanguage("en", persist: false);
        main.SelectGame("crimson-desert");
        var selectedField = typeof(MainWindow).GetField("_selected", Private)!;
        var original = main.Selected!;
        var refresh = typeof(MainWindow).GetMethod("RefreshBindings", Private)!;
        var send = typeof(MainWindow).GetMethod("SendFeatureAsync", Private)!;
        var poll = typeof(MainWindow).GetMethod("PollAsync", Private)!;
        var pollNumber = typeof(MainWindow).GetField("_pollNumber", Private)!;
        var originalPollNumber = pollNumber.GetValue(main);
        var setMessage = typeof(MainWindow).GetMethod("SetMessage", Private)!;
        var errorField = typeof(MainWindow).GetField("_messageIsError", Private)!;
        var ready = new TrainerStatus(true, true, "Connected", "crimson-teleport-ui",
            new Dictionary<string, bool>(), new Dictionary<string, string>
            {
                ["available_crimson_teleport"] = bool.TrueString,
                ["available_crimson_auto_teleport"] = bool.TrueString
            });
        var module = new TeleportStatusFixture(original.Module.Definition, ready);
        var game = new GameItem(module)
        {
            Installation = new GameInstallation("synthetic-no-game", "test", true, true, true, true, "Fixture"),
            Status = ready
        };
        const string queued = "Waiting for the new map marker to be accepted. Terrain height is not checked.";
        const string canceled = "Marker teleport canceled: destination changed, cleared, or was not accepted in time.";
        const string armed = "Armed for a NEW map marker. Existing markers will not teleport; terrain height is not checked.";
        const string readbackOnly = "Marker teleport applied; terrain height and landing are unverified.";
        const string unverified = "Marker position update sent; character movement and landing have not been verified.";
        TrainerStatus State(string message, bool auto = false) => ready with
        {
            Enabled = new Dictionary<string, bool> { ["crimson_auto_teleport"] = auto },
            Values = new Dictionary<string, string>(ready.Values) { ["crimson_teleport_status"] = message }
        };
        void Send(string feature)
        {
            ((Task)send.Invoke(main, new object[] { module.Definition.Features.Single(item => item.Id == feature) })!).GetAwaiter().GetResult();
            Layout(main);
        }
        void Poll(TrainerStatus status)
        {
            module.NextStatus = status;
            pollNumber.SetValue(main, 0); // Fixture deliberately forbids discovery and game actions.
            ((Task)poll.Invoke(main, null)!).GetAwaiter().GetResult();
            Layout(main);
        }
        try
        {
            selectedField.SetValue(main, game);
            module.NextStatus = State(queued);
            Send("crimson_teleport");
            Check(module.SentFeatures.SequenceEqual(new[] { "crimson_teleport" }) && main.Message == queued
                && !(bool)errorField.GetValue(main)!, "A queued manual teleport was reported as applied in game.");
            Poll(State(canceled));
            Check(main.Message == canceled && (bool)errorField.GetValue(main)!,
                "Later teleport cancellation did not replace the queued acknowledgement while connection stayed ready.");
            setMessage.Invoke(main, new object[] { "Another action result", false });
            Poll(State(canceled));
            Check(main.Message == "Another action result", "An unchanged teleport diagnostic overwrote a newer action result.");
            module.NextStatus = State(armed, true);
            Send("crimson_auto_teleport");
            Check(main.Message == armed && !(bool)errorField.GetValue(main)!, "Arming auto teleport was reported as character travel.");
            Poll(State(queued, true));
            Check(main.Message == queued, "An automatically queued marker did not update the main message.");
            var movementOutcomes = new (string Message, string Chinese, bool Failed)[]
            {
                ("Marker position update submitted; movement has not been confirmed.", "已提交标记位置更新；尚未确认移动。", false),
                ("Position updates submitted; waiting for a later movement update to observe the result.", "已提交位置更新；等待后续移动更新以检查结果。", false),
                ("Position observed near the marker after a later movement update; terrain landing is unverified.", "后续移动更新后检测到位置已接近标记；地形落点尚未验证。", false),
                ("Marker teleport not retained after a later movement update; no deferred retry.", "后续移动更新未保留地图传送位置；不会延迟重试。", true),
                ("Marker teleport expired before movement observation completed; no deferred retry.", "移动结果检查尚未完成，地图传送请求已过期；不会延迟重试。", true),
                ("Marker teleport stopped: position write readback failed; no deferred retry.", "地图传送已停止：位置更新校验失败；不会延迟重试。", true)
            };
            foreach (var outcome in movementOutcomes)
            {
                Poll(State(outcome.Message, true));
                Check(main.Message == outcome.Message && main.ValuesLine.Contains(outcome.Message)
                    && (bool)errorField.GetValue(main)! == outcome.Failed,
                    "The later movement outcome was hidden or its failure state was incorrect: " + outcome.Message);
                main.SetLanguage("zh", persist: false);
                Check(main.Message == outcome.Chinese && main.ValuesLine.Contains(outcome.Chinese)
                    && (bool)errorField.GetValue(main)! == outcome.Failed,
                    "The later movement outcome was not translated consistently: " + outcome.Message);
                main.SetLanguage("en", persist: false);
            }
            main.SetLanguage("zh", persist: false);
            Check(Localization.Message("Outer movement hook bytes mismatch; marker controls are unavailable.")
                == "角色移动更新结束位置的绑定校验失败；地图标记功能不可用。", "The new outer movement compatibility diagnostic was not translated.");
            main.SetLanguage("en", persist: false);
            Poll(State(readbackOnly, true));
            Check(main.Message == unverified && main.ValuesLine.Contains(unverified)
                && !main.ValuesLine.Contains("teleport applied"), "Proxy readback was presented as verified character movement.");
            main.SetLanguage("zh", persist: false);
            Check(main.Message == "已发送标记位置更新；尚未确认角色移动或落点。"
                && main.ValuesLine.Contains("已发送标记位置更新；尚未确认角色移动或落点。"),
                "The unverified movement outcome was not translated consistently.");
            Poll(State(canceled, true));
            Check(main.Message == "地图传送已取消：目的地已改变、被删除或未及时确认。" && (bool)errorField.GetValue(main)!,
                "The asynchronous cancellation reason was not retained in Chinese.");
            main.SetLanguage("en", persist: false);
            Poll(ready with { SessionId = "new-crimson-session", Message = "Game found. Choose Connect trainer." });
            Check(main.Message == "Game found. Choose Connect trainer.", "A previous-session teleport result survived a new connection session.");
            game.Status = ready;
            module.NextStatus = ready;
            Send("crimson_teleport");
            Check(main.Message == "Marker teleport request accepted; waiting for a game status update.",
                "A missing post-acknowledgement diagnostic became a false success claim.");
            game.Status = ready;
            module.NextStatus = TrainerStatus.Offline("Waiting for a fresh Crimson Desert trainer heartbeat.");
            Send("crimson_teleport");
            Check(main.Message == module.NextStatus.Message && (bool)errorField.GetValue(main)!,
                "Losing the heartbeat after acknowledgement was reported as successful teleportation.");
            game.Status = State(queued);
            module.SendError = "This Crimson Desert control is unavailable in the current game session.";
            Send("crimson_teleport");
            Check(main.Message == module.SendError && (bool)errorField.GetValue(main)!, "A rejected request lost its native rejection reason.");
            Results.Add(new { Screen = "crimson-teleport-reporting", QueueIsNotCompletion = true,
                AsynchronousOutcomesVisible = true, LaterMovementOutcomes = movementOutcomes.Length, ReadbackIsNotTravel = true,
                SessionChangesClearOutcome = true, NoGameCommands = true });
        }
        finally
        {
            selectedField.SetValue(main, original);
            pollNumber.SetValue(main, originalPollNumber);
            refresh.Invoke(main, null);
        }
    }

    private sealed class TeleportStatusFixture(GameDefinition definition, TrainerStatus status) : IGameModule
    {
        public GameDefinition Definition => definition;
        public TrainerStatus NextStatus { get; set; } = status;
        public string? SendError { get; set; }
        public List<string> SentFeatures { get; } = new();
        public Task<TrainerStatus> ReadStatusAsync(GameInstallation installation) => Task.FromResult(NextStatus);
        public Task SendAsync(GameInstallation installation, TrainerStatus current, string featureId, bool? enabled = null)
        {
            if (featureId is not ("crimson_teleport" or "crimson_auto_teleport")) throw new InvalidOperationException("Unexpected fixture action.");
            SentFeatures.Add(featureId);
            return SendError == null ? Task.CompletedTask : Task.FromException(new InvalidOperationException(SendError));
        }
        public Task<GameInstallation> DiscoverAsync(string? directory = null) => throw new InvalidOperationException("Fixture discovery is forbidden.");
        public Task InstallAsync(GameInstallation installation, IProgress<string>? progress = null) => throw new InvalidOperationException("Fixture installation is forbidden.");
        public Task RemoveAsync(GameInstallation installation, IProgress<string>? progress = null) => throw new InvalidOperationException("Fixture removal is forbidden.");
        public Task LaunchAsync(GameInstallation installation) => throw new InvalidOperationException("Fixture launch is forbidden.");
    }

    private sealed class ConnectStatusFixture(GameDefinition definition, TrainerStatus status) : IGameModule
    {
        public GameDefinition Definition => definition;
        public int ConnectCalls { get; private set; }
        public Task<TrainerStatus> ReadStatusAsync(GameInstallation installation) => Task.FromResult(status);
        public Task SendAsync(GameInstallation installation, TrainerStatus current, string featureId, bool? enabled = null)
        {
            if (featureId != "crimson_connect" || enabled.HasValue) throw new InvalidOperationException("Unexpected fixture action.");
            ConnectCalls++;
            return Task.CompletedTask;
        }
        public Task<GameInstallation> DiscoverAsync(string? directory = null) => throw new InvalidOperationException("Fixture discovery is forbidden.");
        public Task InstallAsync(GameInstallation installation, IProgress<string>? progress = null) => throw new InvalidOperationException("Fixture installation is forbidden.");
        public Task RemoveAsync(GameInstallation installation, IProgress<string>? progress = null) => throw new InvalidOperationException("Fixture removal is forbidden.");
        public Task LaunchAsync(GameInstallation installation) => throw new InvalidOperationException("Fixture launch is forbidden.");
    }

    private static void OpenUpdatesAndInspect(MainWindow main, ListBox library, string theme, string name,
        int width, int height, string? artifacts, LibrarySnapshot before)
    {
        var field = typeof(MainWindow).GetField("_updateWindow", Private)!;
        var click = typeof(MainWindow).GetMethod("UpdatesClicked", Private)!;
        var returned = false;
        // If a modal loop is reintroduced, close it from that loop instead of hanging the suite.
        var operation = main.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            if (returned) return;
            Check(false, $"{name}: Updates entered a modal dispatcher loop.");
            (field.GetValue(main) as UpdateWindow)?.Close();
        }));
        click.Invoke(main, new object[] { main, new RoutedEventArgs(ButtonBase.ClickEvent) });
        returned = true;
        operation.Abort();
        var update = field.GetValue(main) as UpdateWindow;
        try
        {
            Check(update != null, $"{name}: the real modeless Updates window did not open.");
            if (update == null) return;
            update.ShowInTaskbar = false;
            update.Width = Math.Min(740, width - 240);
            update.Height = Math.Min(720, height - 45);
            Layout(update); Layout(main);
            Check(update.IsLoaded, $"{name}: the modeless Updates window did not load.");
            Check(main.CanManage && library.IsEnabled && IsWindowEnabled(new WindowInteropHelper(main).Handle),
                $"{name}: Updates disabled the cheat window or library.");
            Check(update.Owner == main && !update.CanCheck && !update.CanAct, $"{name}: preview isolation was lost.");
            click.Invoke(main, new object[] { main, new RoutedEventArgs(ButtonBase.ClickEvent) });
            Check(ReferenceEquals(update, field.GetValue(main)) && main.OwnedWindows.OfType<UpdateWindow>().Count() == 1,
                $"{name}: repeated clicks created another Updates window.");
            var during = InspectLibrary(library);
            var chrome = Descendants<Border>(library).FirstOrDefault();
            Check(chrome != null, $"{name}: library template chrome was missing.");
            if (chrome != null)
            {
                Check(Color(chrome.Background) == Color(library.Background),
                    $"{name}: library chrome overrides its background ({Color(chrome.Background)} vs {Color(library.Background)}).");
            }
            foreach (var border in Descendants<Border>(library).Where(Arranged))
            {
                if (theme == "dark" && border.Background is SolidColorBrush fill && fill.Color.A > 240)
                    Check((fill.Color.R + fill.Color.G + fill.Color.B) / (3d * 255) < .72,
                        $"{name}: dark library paints a light/white border ({fill.Color}, {border.ActualWidth:0}x{border.ActualHeight:0}).");
            }
            var names = Descendants<TextBlock>(library).Where(t => Arranged(t) && t.DataContext is GameItem game && t.Text == game.Name).ToArray();
            Check(names.Length > 0, $"{name}: game names disappeared while Updates was open.");
            foreach (var label in names)
                Check(Color(label.Foreground) == Color((Brush)main.FindResource("InkBrush")), $"{name}: game name uses an OS/default foreground.");
            Check(before.GameNames.SequenceEqual(during.GameNames), $"{name}: visible library game labels changed while Updates was open.");
            Check(library.Items.Count == main.Games.Count, $"{name}: library games were lost while Updates was open.");
            var selected = (GameItem)library.SelectedItem;
            var other = main.Games.First(game => game.Id != selected.Id);
            main.SelectGame(other.Id);
            Layout(main);
            Check(ReferenceEquals(main.Selected, other) && ReferenceEquals(update, field.GetValue(main)),
                $"{name}: the user could not change game pages while Updates was open.");
            main.SelectGame(selected.Id);
            library.ScrollIntoView(selected);
            Layout(main);
            ValidateScrolling(library, name + "-updates-open");
            if (artifacts != null)
            {
                Capture(main, Path.Combine(artifacts, name + "-owner.png"));
                Capture(update, Path.Combine(artifacts, name + "-updates.png"));
                CaptureComposite(main, update, Path.Combine(artifacts, name + "-modeless.png"));
            }
            Results.Add(new { Screen = name, OwnerEnabled = main.CanManage, LibraryEnabled = library.IsEnabled,
                Modeless = true, RepeatedClickReusedWindow = true, GameSelectionWhileOpen = true,
                Before = before, During = during, WindowTitle = update.WindowTitle });
        }
        finally { update?.Close(); }
    }

    private static void ValidateScrolling(ListBox library, string name)
    {
        var scroll = Descendants<ScrollViewer>(library).First();
        Check(scroll.PanningMode == PanningMode.VerticalOnly && !scroll.CanContentScroll, $"{name}: library touch/pixel scrolling was lost.");
        var previousOffset = scroll.VerticalOffset;
        scroll.ScrollToEnd(); library.UpdateLayout();
        library.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Check(Math.Abs(scroll.VerticalOffset - scroll.ScrollableHeight) < 1, $"{name}: library bottom became unreachable.");
        scroll.ScrollToVerticalOffset(previousOffset); library.UpdateLayout();
    }

    private static LibrarySnapshot InspectLibrary(ListBox library) => new(Color(library.Background), Color(library.Foreground),
        Descendants<Border>(library).Where(Arranged).Select(b => Color(b.Background)).Distinct().ToArray(),
        Descendants<TextBlock>(library).Where(t => Arranged(t) && t.DataContext is GameItem game && t.Text == game.Name).Select(t => t.Text).ToArray());

    private static App CreateAppWithoutStartup()
    {
        // Application queues OnStartup from its constructor even when Run is not called.
        // Keep the real compiled App.xaml resources while preventing app startup/discovery.
        var dispatcher = Dispatcher.CurrentDispatcher;
        var operations = new List<DispatcherOperation>();
        DispatcherHookEventHandler posted = (_, e) => operations.Add(e.Operation);
        dispatcher.Hooks.OperationPosted += posted;
        App app;
        try { app = new App(); } finally { dispatcher.Hooks.OperationPosted -= posted; }
        var aborted = 0;
        foreach (var operation in operations)
        {
            var callback = typeof(DispatcherOperation).GetField("_method", Private)?.GetValue(operation) as Delegate;
            if (callback?.Method.DeclaringType?.FullName?.StartsWith("System.Windows.Application", StringComparison.Ordinal) == true
                && callback.Method.Name.Contains(".ctor", StringComparison.Ordinal) && operation.Abort()) aborted++;
        }
        if (aborted != 1) throw new InvalidOperationException("Could not suppress exactly one Application startup callback before dispatching UI work.");
        return app;
    }

    private static string? Option(string[] args, string name) { var index = Array.IndexOf(args, name); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
    private static string? Hash(string path) => File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : null;
    private static void Check(bool condition, string message) { assertions++; if (!condition) Failures.Add(message); }
    private static bool Arranged(FrameworkElement item) => item.IsVisible && item.ActualWidth > 0 && item.ActualHeight > 0;
    private static string Color(Brush? brush) => brush is SolidColorBrush color ? color.Color.ToString() : brush?.ToString() ?? "null";
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T item) yield return item;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void Layout(Window window)
    {
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }
    private static RenderTargetBitmap Render(Window window)
    {
        var visual = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); return bitmap;
    }
    private static void Capture(Window window, string path) => Save(Render(window), path);
    private static void CaptureComposite(Window owner, Window update, string path)
    {
        var background = Render(owner); var dialog = Render(update); var drawing = new DrawingVisual();
        var left = background.PixelWidth + 24;
        var width = left + dialog.PixelWidth;
        var height = Math.Max(background.PixelHeight, dialog.PixelHeight);
        using (var context = drawing.RenderOpen())
        {
            context.DrawImage(background, new Rect(0, 0, background.PixelWidth, background.PixelHeight));
            context.DrawImage(dialog, new Rect(left, 0, dialog.PixelWidth, dialog.PixelHeight));
        }
        var composite = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        composite.Render(drawing); Save(composite, path);
    }
    private static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private sealed record LibrarySnapshot(string Background, string Foreground, string[] BorderFills, string[] GameNames);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
}
