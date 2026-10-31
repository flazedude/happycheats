using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HappyCheats.App;
using HappyCheats.Core;

namespace HappyCheats.App.Tests;

internal static class GameOverlayChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static object Run(MainWindow main, string root, string? artifacts)
    {
        var originalLanguage = Localization.Language;
        var originalTheme = ThemeManager.IsLight ? "light" : "dark";
        var sent = new List<string>();
        var selected = new List<(string Id, string Value)>();
        var dismissals = 0;
        var assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("Shared game menu: " + message);
        }
        var overlay = new GameOverlay(feature => { sent.Add(feature.Id); return Task.CompletedTask; },
            choice => { selected.Add((choice.Owner.Definition.Id, choice.Definition.Value)); return Task.CompletedTask; },
            () => dismissals++) { Left = -32000, Top = -32000 };
        var host = new Window {
            Width = 800, Height = 480, Left = -32000, Top = -32000,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false
        };
        var catalog = ModuleCatalog.Create(root);
        var featureCount = 0;
        var choiceCount = 0;
        var numberCount = 0;
        Button Disable() => (Button)typeof(GameOverlay).GetField("disable", Private)!.GetValue(overlay)!;
        Button[] Actions() => Descendants<Button>(overlay).Where(button => button.Tag is FeatureItem).ToArray();
        Button[] Choices() => Descendants<Button>(overlay).Where(button => button.Tag is FeatureChoiceItem).ToArray();
        Border[] Rows() => Descendants<Border>(overlay).Where(border => border.Tag is FeatureItem).ToArray();
        TextBox[] Inputs() => Descendants<TextBox>(overlay).Where(input => input.Tag is FeatureItem).ToArray();
        var scroll = (ScrollViewer)typeof(GameOverlay).GetField("scroll", Private)!.GetValue(overlay)!;
        var groups = (ComboBox)typeof(GameOverlay).GetField("groupSelector", Private)!.GetValue(overlay)!;
        void Update(GameItem game, FeatureItem[] features, TrainerStatus status, bool busy = false, bool? cleanup = null)
        {
            game.Status = status;
            foreach (var feature in features) feature.Update(status, !busy);
            overlay.Refresh(game, features, busy, cleanup ?? (status.Connected && !busy), status.Message);
            Layout(overlay);
        }
        try
        {
            main.SetLanguage("en", persist: false);
            main.SetTheme("dark", persist: false);
            Check(catalog.Count >= 17 && catalog.Select(module => module.Definition.Id).Distinct().Count() == catalog.Count,
                "the complete catalog must have unique game IDs");
            Check(typeof(MainWindow).GetFields(Private).Any(field => field.FieldType == typeof(GameOverlay)),
                "MainWindow must use the shared menu");
            Check(!typeof(MainWindow).GetFields(Private).Any(field => field.FieldType.Name == "DawnwalkerOverlay"),
                "the legacy Dawnwalker-only window must not remain the live menu");
            foreach (var module in catalog)
            {
                var id = module.Definition.Id;
                Check(module.GameExecutables.Count > 0, id + " has no exact executable metadata for its menu");
                foreach (var executable in module.GameExecutables)
                    Check(!string.IsNullOrWhiteSpace(executable) && !Path.IsPathRooted(executable)
                        && executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                        && !executable.Split('/', '\\').Any(segment => segment is ".." or "."),
                        id + " executable metadata must stay relative to its installation");
                var game = new GameItem(module);
                var features = module.Definition.Features.Select(definition => new FeatureItem(definition)).ToArray();
                var expected = features.Where(feature => feature.Definition.Id != "disable_all").ToArray();
                var ready = Ready(features);
                Update(game, features, ready);
                Check(!overlay.IsVisible, "refreshing hidden menu unexpectedly showed " + id);
                overlay.Show(); Layout(overlay);
                var actions = Actions();
                var choices = Choices();
                var inputs = Inputs();
                Check(overlay.CurrentGameId == id && overlay.Title.Contains(game.Name, StringComparison.Ordinal), id + " menu identity is wrong");
                Check(actions.Select(button => ((FeatureItem)button.Tag).Definition.Id).SequenceEqual(expected.Select(feature => feature.Definition.Id)),
                    id + " omitted, duplicated or reordered an action");
                Check(Rows().Length == expected.Length && actions.All(button => button.IsEnabled), id + " did not render all ready rows");
                Check(inputs.Select(input => (FeatureItem)input.Tag).SequenceEqual(expected.Where(feature => feature.Definition.Number != null)),
                    id + " numeric editors do not match metadata");
                Check(choices.Select(button => (FeatureChoiceItem)button.Tag).SequenceEqual(expected.SelectMany(feature => feature.Choices)),
                    id + " choice buttons do not match metadata");
                Check(Disable().IsEnabled && actions.All(button => ((FeatureItem)button.Tag).Definition.Id != "disable_all"),
                    id + " must have one separate cleanup action");
                if (id == "astroneer")
                {
                    var collection = expected.Single(feature => feature.Definition.Id == "resource_collection_10x");
                    var speed = expected.Single(feature => feature.Definition.Id == "player_run_speed");
                    var invulnerability = expected.Single(feature => feature.Definition.Id == "invulnerability");
                    Check(expected.Length == 6 && invulnerability.Definition.IsToggle && invulnerability.Hotkey == "F5"
                        && speed.Definition.IsToggle && speed.Hotkey == "F6", "Astroneer must expose six controls with distinct F5/F6 toggles");
                    Check(collection.Choices.Select(choice => choice.Definition.Value).SequenceEqual(new[] { "10", "20", "100" }),
                        "Astroneer must expose the three supported multiplier choices in order");
                    Check(speed.Choices.Select(choice => choice.Definition.Value).SequenceEqual(new[] { "3", "5", "10" })
                        && speed.Definition.ChoiceValueKey == "selected_run_speed_multiplier"
                        && collection.Definition.ChoiceValueKey != speed.Definition.ChoiceValueKey,
                        "Astroneer run speed must expose its own three choices and confirmed-value key");
                    var hundred = collection.Choices.Single(choice => choice.Definition.Value == "100");
                    var hundredButton = choices.Single(button => ReferenceEquals(button.Tag, hundred));
                    sent.Clear(); selected.Clear();
                    Click(hundredButton);
                    Check(selected.SequenceEqual(new[] { (collection.Definition.Id, "100") }) && sent.Count == 0,
                        "Astroneer 100x choice dispatched a toggle or changed the choice value");
                    Check(!hundred.IsSelected && collection.Choices[0].IsSelected,
                        "Astroneer 100x click optimistically replaced the confirmed multiplier");
                    var confirmedHundred = ready with { Values = new Dictionary<string, string>(ready.Values) {
                        ["selected_collection_multiplier"] = "100", ["collection_multiplier"] = "1" } };
                    Update(game, features, confirmedHundred);
                    Check(hundred.IsSelected && collection.Choices.Count(choice => choice.IsSelected) == 1
                        && collection.ActionLabel == Localization.Text("turnOn") && game.Status.Values["collection_multiplier"] == "1",
                        "Confirming a 100x selection must leave collection off at effective 1x");
                    Check(speed.Choices[0].IsSelected && speed.ActionLabel == Localization.Text("turnOn"),
                        "Changing collection to 100x altered the default run-speed selection or toggle");
                    foreach (var speedValue in new[] { "5", "10", "3" })
                    {
                        var speedChoice = speed.Choices.Single(choice => choice.Definition.Value == speedValue);
                        var previousChoice = speed.Choices.Single(choice => choice.IsSelected);
                        sent.Clear(); selected.Clear();
                        Click(choices.Single(button => ReferenceEquals(button.Tag, speedChoice)));
                        Check(selected.SequenceEqual(new[] { (speed.Definition.Id, speedValue) }) && sent.Count == 0
                            && previousChoice.IsSelected && !speedChoice.IsSelected,
                            "Run-speed selection dispatched a toggle, used the collection selector, or displayed an optimistic value");
                        confirmedHundred = confirmedHundred with { Values = new Dictionary<string, string>(confirmedHundred.Values) {
                            ["selected_run_speed_multiplier"] = speedValue, ["run_speed_multiplier"] = "1" } };
                        Update(game, features, confirmedHundred);
                        Check(speedChoice.IsSelected && hundred.IsSelected && speed.ActionLabel == Localization.Text("turnOn")
                            && game.Status.Values["run_speed_multiplier"] == "1",
                            "Confirmed speed selection enabled running speed or changed collection state");
                    }
                    var running = confirmedHundred with { Enabled = new Dictionary<string, bool> {
                        [speed.Definition.Id] = true, [invulnerability.Definition.Id] = true } };
                    Update(game, features, running);
                    var tenSpeed = speed.Choices.Single(choice => choice.Definition.Value == "10");
                    sent.Clear(); selected.Clear();
                    Click(choices.Single(button => ReferenceEquals(button.Tag, tenSpeed)));
                    Check(selected.SequenceEqual(new[] { (speed.Definition.Id, "10") }) && sent.Count == 0
                        && speed.Choices[0].IsSelected, "Changing enabled run speed toggled a feature or selected before confirmation");
                    running = running with { Values = new Dictionary<string, string>(running.Values) {
                        ["selected_run_speed_multiplier"] = "10", ["run_speed_multiplier"] = "10" } };
                    Update(game, features, running);
                    Check(tenSpeed.IsSelected && hundred.IsSelected && speed.ActionLabel == Localization.Text("turnOff")
                        && invulnerability.ActionLabel == Localization.Text("turnOff") && collection.ActionLabel == Localization.Text("turnOn"),
                        "Confirmed running-speed change affected independent toggles or the collection selector");
                    var suspendedSpeed = running with { Values = new Dictionary<string, string>(running.Values) {
                        ["run_speed_multiplier"] = "1" } };
                    Update(game, features, suspendedSpeed);
                    Check(tenSpeed.IsSelected && speed.ActionLabel == Localization.Text("turnOff")
                        && game.Status.Values["run_speed_multiplier"] == "1" && hundred.IsSelected,
                        "Temporarily normal movement cleared the confirmed speed selection or enabled state");
                    var blockedSpeed = confirmedHundred with { Values = new Dictionary<string, string>(confirmedHundred.Values) {
                        ["available_player_run_speed"] = bool.FalseString } };
                    Update(game, features, blockedSpeed);
                    var selectedBeforeBlockedClick = selected.Count;
                    Click(choices.Single(button => ReferenceEquals(button.Tag, tenSpeed)));
                    Check(speed.Choices.All(choice => !choice.CanUse) && collection.Choices.All(choice => choice.CanUse)
                        && selected.Count == selectedBeforeBlockedClick + 1, "Run-speed status leaked across selectors or a click was silently dropped");
                    Update(game, features, confirmedHundred);
                    foreach (var choiceButton in choices)
                    {
                        var owner = ((FeatureChoiceItem)choiceButton.Tag).Owner;
                        var row = Rows().Single(surface => ReferenceEquals(surface.Tag, owner));
                        var position = choiceButton.TranslatePoint(new Point(), row);
                        Check(choiceButton.ActualWidth > 0 && choiceButton.ActualHeight > 0
                            && position.X >= 0 && position.Y >= 0
                            && position.X + choiceButton.ActualWidth <= row.ActualWidth + 1
                            && position.Y + choiceButton.ActualHeight <= row.ActualHeight + 1,
                            "Astroneer multiplier button escaped its row layout");
                    }
                    var previousLanguage = Localization.Language;
                    foreach (var language in new[] { "en", "zh" })
                    {
                        main.SetLanguage(language, persist: false);
                        Update(game, features, confirmedHundred);
                        Check(collection.Description.Contains("100", StringComparison.Ordinal)
                            && hundred.Label.Contains("100×", StringComparison.Ordinal),
                            "Astroneer 100x menu copy or confirmed label is missing in " + language);
                        Check(speed.Choices.Select(choice => choice.Definition.Label).SequenceEqual(new[] { "3×", "5×", "10×" })
                            && speed.Description.Contains("3", StringComparison.Ordinal) && speed.Description.Contains("5", StringComparison.Ordinal)
                            && speed.Description.Contains("10", StringComparison.Ordinal)
                            && invulnerability.Name != invulnerability.Definition.Id,
                            "Astroneer speed choices or new feature text are missing in " + language);
                    }
                    Check(Localization.Message("Choose a 10×, 20× or 100× resource collection multiplier.")
                        == "请选择 10 倍、20 倍或 100 倍资源采集倍率。", "Astroneer 100x validation did not translate");
                    Check(Localization.Message("Select a collection multiplier of 10, 20 or 100 without enabling the toggle.")
                        == "请选择 10 倍、20 倍或 100 倍采集倍率，此操作不会开启采集功能。", "Astroneer runtime selection validation did not translate");
                    Check(Localization.Message("Choose a 3×, 5× or 10× run speed multiplier.")
                        == "请选择 3 倍、5 倍或 10 倍奔跑速度倍率。", "Astroneer speed validation did not translate");
                    Check(Localization.Text("multiplier") == "倍率"
                        && Localization.ValueLabel("run_speed_multiplier") == "奔跑速度倍率",
                        "Independent Astroneer selectors reused collection-specific text");
                    foreach (var multiplier in new[] { "3", "5", "10" })
                    {
                        Check(Localization.Message(multiplier + "x on-foot run speed enabled") == "徒步奔跑速度 " + multiplier + " 倍已开启。"
                            && Localization.Message("Run speed multiplier set to " + multiplier + "x") == "奔跑速度倍率已设为 " + multiplier + " 倍。",
                            "Astroneer confirmed speed result did not translate");
                    }
                    Check(Localization.Message("Movement component changed; enable again") == "角色移动状态已更换，请重新开启奔跑速度功能。"
                        && Localization.Message("Run speed is unavailable") == "奔跑速度功能当前不可用。"
                        && Localization.Message("Invulnerability is unavailable") == "无敌功能当前不可用。"
                        && Localization.Message("Invulnerability enabled") == "无敌已开启。",
                        "Astroneer unavailable-feature messages did not translate");
                    var pendingRestoreValues = MainWindow.FormatValues(new Dictionary<string, string> {
                        ["restoration_pending"] = bool.TrueString, ["invulnerability_active"] = bool.TrueString });
                    Check(pendingRestoreValues.Contains("原值待恢复", StringComparison.Ordinal)
                        && pendingRestoreValues.Contains("无敌实际生效", StringComparison.Ordinal)
                        && Localization.Message("Restoration pending; some original values could not be restored.") == "部分原值尚未恢复，正在自动重试。",
                        "Astroneer restoration warning or observed-state metrics were hidden or untranslated");
                    main.SetLanguage(previousLanguage, persist: false);
                    Update(game, features, ready);
                }
                featureCount += actions.Length; choiceCount += choices.Length; numberCount += inputs.Length;
                sent.Clear(); selected.Clear();
                foreach (var action in actions) Click(action);
                foreach (var choice in choices) Click(choice);
                Click(Disable());
                Check(sent.SequenceEqual(expected.Select(feature => feature.Definition.Id).Append("disable_all")), id + " action callbacks changed module IDs");
                Check(selected.SequenceEqual(expected.SelectMany(feature => feature.Choices).Select(choice => (choice.Owner.Definition.Id, choice.Definition.Value))),
                    id + " choice callbacks changed metadata values");
                var dispatched = sent.Count + selected.Count;
                foreach (var (status, busy) in new[] { (TrainerStatus.Offline(), false), (ready, true), (Unavailable(ready, features), false) })
                {
                    Update(game, features, status, busy, cleanup: false);
                    Check(actions.All(button => button.IsEnabled) && choices.All(button => button.IsEnabled)
                        && inputs.All(input => input.IsEnabled) && Disable().IsEnabled,
                        id + " greyed out a control in offline, busy or unavailable state");
                    // Every current row reaches the shared dispatcher; it reports command-time errors.
                    foreach (var action in actions) Click(action);
                    foreach (var choice in choices) Click(choice);
                    Click(Disable());
                    dispatched += actions.Length + choices.Length + 1;
                    Check(sent.Count + selected.Count == dispatched, id + " silently dropped a clicked action");
                }
                foreach (var language in new[] { "en", "zh" })
                foreach (var theme in new[] { "dark", "light" })
                {
                    main.SetLanguage(language, persist: false); main.SetTheme(theme, persist: false);
                    Update(game, features, ready);
                    Check(actions.SequenceEqual(Actions()) && inputs.SequenceEqual(Inputs()) && choices.SequenceEqual(Choices()),
                        id + " language/theme refresh replaced controls");
                    Check(Rows().All(row => Descendants<TextBlock>(row).Any(text => text.Text == ((FeatureItem)row.Tag).Name)),
                        id + " row title failed to refresh its language");
                    Check(actions.All(button => AutomationProperties.GetName(button) == ((FeatureItem)button.Tag).Name),
                        id + " accessibility names failed to follow the feature labels");
                    Check(actions.All(button => Color(button.Background) == Color(ThemeManager.Brush("ButtonBrush"))),
                        id + " action brushes failed to follow the theme");
                    Check(Equals(Disable().Content, Localization.Text("disableAll")), id + " cleanup action did not translate");
                }
                if (artifacts != null && id is "astroneer" or "stardew" or "dawnwalker")
                    Capture(overlay, Path.Combine(artifacts, "shared-menu-" + id + "-zh-light.png"));
                overlay.Hide();
            }

            // Future metadata uses a new ID and every supported control shape. Its
            // module throws if any real command, discovery, install or launch is attempted.
            var future = new FutureModule();
            var futureGame = new GameItem(future);
            var futureFeatures = future.Definition.Features.Select(definition => new FeatureItem(definition)).ToArray();
            var futureReady = Ready(futureFeatures);
            main.SetLanguage("en", persist: false); main.SetTheme("dark", persist: false);
            Update(futureGame, futureFeatures, futureReady);
            overlay.Show(); Layout(overlay);
            var originalRows = Rows();
            var originalActions = Actions();
            var originalChoices = Choices();
            var oldDisable = Disable();
            var editor = Inputs().Single();
            Check(GameOverlay.AllowsKeyboardActivation(editor), "explicit numeric editing must retain keyboard focus support");
            Check(!GameOverlay.AllowsKeyboardActivation(originalActions[0]) && !originalActions[0].Focusable
                && !GameOverlay.AllowsKeyboardActivation(groups) && !groups.Focusable,
                "menu actions or mouse navigation can steal game focus");
            var activation = typeof(GameOverlay).GetMethod("WindowMessage", Private)!;
            object[] activationArguments = { IntPtr.Zero, 0x0021, IntPtr.Zero, IntPtr.Zero, false };
            var activationResult = (IntPtr)activation.Invoke(overlay, activationArguments)!;
            Check(activationResult == new IntPtr(3) && (bool)activationArguments[4],
                "mouse activation must deliver clicks without activating the companion window");
            var foreground = GetForegroundWindow();
            var nativeActivation = SendMessage(new WindowInteropHelper(overlay).Handle, 0x0021, IntPtr.Zero, new IntPtr(0x02010001));
            Check(nativeActivation == new IntPtr(3) && GetForegroundWindow() == foreground,
                "the native menu window did not retain foreground focus during mouse activation");
            var amount = (FeatureItem)editor.Tag;
            editor.SetCurrentValue(TextBox.TextProperty, "37");
            editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            editor.Select(1, 1);
            Check(amount.NumberText == "37", "the numeric editor is not bound to the shared feature draft");
            groups.SelectedIndex = 1;
            var groupKey = ((ComboBoxItem)groups.SelectedItem).Tag;
            var confirmed = new Dictionary<string, string>(futureReady.Values) { ["future_amount_value"] = "250" };
            for (var heartbeat = 0; heartbeat < 100; heartbeat++)
                Update(futureGame, futureFeatures, futureReady with { Values = confirmed });
            Check(originalRows.SequenceEqual(Rows()) && originalActions.SequenceEqual(Actions()) && ReferenceEquals(editor, Inputs().Single()),
                "heartbeats rebuilt retained rows or the numeric editor");
            Check(amount.NumberText == "37" && editor.Text == "37" && editor.SelectionStart == 1 && editor.SelectionLength == 1,
                "status updates destroyed the typed draft or selection");
            Check(amount.ConfirmedNumberLabel.EndsWith("250", StringComparison.Ordinal), "confirmed value did not update alongside the draft");
            Check(Equals(((ComboBoxItem)groups.SelectedItem).Tag, groupKey)
                && Rows().All(row => (row.Visibility == Visibility.Visible) == Equals(((FeatureItem)row.Tag).Definition.Group, groupKey)),
                "heartbeat lost group selection or showed a different group");
            groups.SelectedIndex = 0;
            Update(futureGame, futureFeatures, futureReady with { Enabled = new Dictionary<string, bool> { ["future_switch"] = true } });
            Check(Color(originalActions[0].Background) == Color(ThemeManager.Brush("SuccessSurfaceBrush")), "active state lost the shared theme brush");
            sent.Clear(); selected.Clear();
            foreach (var button in originalActions) Click(button);
            foreach (var button in originalChoices) Click(button);
            Check(sent.SequenceEqual(new[] { "future_switch", "future_amount", "future_mode", "future_action" })
                && selected.SequenceEqual(new[] { ("future_mode", "gentle"), ("future_mode", "strong") }),
                "a future game's controls require an ID allowlist or dispatch different IDs");
            var commandCount = sent.Count + selected.Count;
            futureFeatures = futureFeatures.Select(feature => new FeatureItem(feature.Definition)).ToArray();
            Update(futureGame, futureFeatures, futureReady);
            Check(!ReferenceEquals(originalActions[0], Actions()[0]), "replaced feature models retained stale rows");
            foreach (var button in originalActions) Click(button);
            foreach (var button in originalChoices) Click(button);
            Click(oldDisable);
            Check(sent.Count + selected.Count == commandCount, "a retained row dispatched after its feature model was replaced");
            var staleAfterGame = Actions().First();
            var otherGame = new GameItem(catalog[0]);
            var otherFeatures = otherGame.Module.Definition.Features.Select(definition => new FeatureItem(definition)).ToArray();
            Update(otherGame, otherFeatures, Ready(otherFeatures));
            Click(staleAfterGame);
            Check(sent.Count + selected.Count == commandCount, "a retained row dispatched after switching games");
            Update(futureGame, futureFeatures, futureReady);
            Click(Actions().First());
            Check(sent.Last() == "future_switch" && sent.Count + selected.Count == commandCount + 1, "replacement row failed to dispatch exactly once");
            var close = Descendants<Button>(overlay).Single(button => AutomationProperties.GetName(button) == "Close in-game menu");
            Click(close);
            Check(dismissals == 1, "close button did not use the shared dismiss callback");

            CheckShortcut(Check);
            CheckLocator(Check);
            foreach (var gameBounds in new[] { new Rect(0, 0, 800, 480), new Rect(-1920, 40, 1920, 1080), new Rect(40, 20, 260, 120) })
            {
                var bounds = GameOverlay.CalculateBounds(gameBounds);
                Check(bounds.Width > 0 && bounds.Height > 0 && gameBounds.Contains(bounds), "menu bounds escape a small or secondary-monitor game window");
            }
            Check(GameOverlay.CalculateBounds(Rect.Empty).IsEmpty, "empty game geometry was accepted");
            host.Show(); Layout(host);
            var handle = new WindowInteropHelper(host).Handle;
            overlay.Follow(handle, true); Layout(overlay);
            Check(overlay.IsVisible && overlay.ActualWidth <= 800 && overlay.ActualHeight <= 480
                && overlay.Left >= host.Left && overlay.Top >= host.Top
                && overlay.Left + overlay.ActualWidth <= host.Left + host.ActualWidth + 1
                && overlay.Top + overlay.ActualHeight <= host.Top + host.ActualHeight + 1,
                "menu is not constrained to the 800x480 host window");
            Check(scroll.VerticalScrollBarVisibility == ScrollBarVisibility.Auto && scroll.ScrollableHeight > 0,
                "small windows must keep all actions and the footer reachable by scrolling");
            scroll.ScrollToBottom(); Layout(overlay);
            Check(scroll.VerticalOffset > 0, "small-window menu did not scroll to the footer");
            if (artifacts != null) Capture(overlay, Path.Combine(artifacts, "shared-menu-future-800x480-footer.png"));
            var previousLeft = overlay.Left;
            var previousTop = overlay.Top;
            host.Left += 24; host.Top += 12; Layout(host);
            overlay.Follow(handle, true); Layout(overlay);
            Check(Math.Abs(overlay.Left - previousLeft - 24) < 1 && Math.Abs(overlay.Top - previousTop - 12) < 1,
                "menu did not follow a moved host");
            host.WindowState = WindowState.Minimized; Layout(host);
            overlay.Follow(handle, true);
            Check(!overlay.IsVisible, "menu stayed visible over a minimized host");
            host.WindowState = WindowState.Normal; Layout(host);
            overlay.Follow(handle, true);
            Check(overlay.IsVisible, "menu did not return when the host was restored");
            host.Hide(); overlay.Follow(handle, true);
            Check(!overlay.IsVisible, "menu stayed visible over a hidden host");
            host.Show(); Layout(host); overlay.Follow(handle, false);
            Check(!overlay.IsVisible, "focus scope did not hide the menu");
            overlay.Follow(IntPtr.Zero, true);
            Check(!overlay.IsVisible, "missing game HWND showed the menu");
            host.Close(); overlay.Follow(handle, true);
            Check(!overlay.IsVisible, "closed game HWND showed the menu");
            Check(future.Calls == 0, "UI checks reached a module command or launch");
            return new { Screen = "shared-game-menu", Assertions = assertions, CatalogGames = catalog.Count, Controls = featureCount,
                NumericEditors = numberCount, ChoiceButtons = choiceCount, FutureMetadata = true, StableDrafts = true,
                StaleRowsBlocked = true, EnglishAndChinese = true, LightAndDark = true, SmallWindowFollow = true, NoGameCommands = true };
        }
        finally
        {
            overlay.Close(); host.Close();
            main.SetLanguage(originalLanguage, persist: false); main.SetTheme(originalTheme, persist: false);
        }
    }

    private static TrainerStatus Ready(IEnumerable<FeatureItem> features)
    {
        var values = new Dictionary<string, string> { ["gta_online"] = bool.FalseString };
        foreach (var feature in features)
        {
            values[AvailabilityKey(feature.Definition.Id)] = bool.TrueString;
            if (feature.Definition.ChoiceValueKey is { } key)
                values[key] = feature.Definition.Number?.DefaultValue.ToString(CultureInfo.InvariantCulture)
                    ?? feature.Choices.FirstOrDefault()?.Definition.Value ?? "";
        }
        return new TrainerStatus(true, true, "Synthetic menu fixture; no game commands", "shared-menu-fixture",
            new Dictionary<string, bool>(), values);
    }

    private static TrainerStatus Unavailable(TrainerStatus ready, IEnumerable<FeatureItem> features)
    {
        var values = new Dictionary<string, string>(ready.Values);
        foreach (var feature in features) values[AvailabilityKey(feature.Definition.Id)] = bool.FalseString;
        return ready with { Values = values };
    }

    private static string AvailabilityKey(string id) => id switch
    {
        "wash" => "washAvailable", "dry" or "dry_slower" or "dry_faster" => "dryAvailable", "shop" => "shopAvailable", _ => "available_" + id
    };

    private static void CheckShortcut(Action<bool, string> check)
    {
        check(GameMenuShortcut.VirtualKey == System.Windows.Input.KeyInterop.VirtualKeyFromKey(System.Windows.Input.Key.Delete),
            "shared menu toggle must poll the Delete key");
        check(GameMenuShortcut.VirtualKey != System.Windows.Input.KeyInterop.VirtualKeyFromKey(System.Windows.Input.Key.Home),
            "retired Home key is still bound to the menu");
        var shortcut = new GameMenuShortcut();
        check(!shortcut.Poll(false, true, false, true, false), "Delete alone toggled the menu");
        check(!shortcut.Poll(true, true, false, true, false), "adding Ctrl to held Delete created an edge");
        shortcut.Poll(true, false, false, true, false);
        check(shortcut.Poll(true, true, false, true, false), "Ctrl+Del did not toggle on a fresh edge");
        check(!shortcut.Poll(true, true, false, true, false), "held Ctrl+Del repeated");
        shortcut.Poll(false, false, false, false, false);
        check(!shortcut.Poll(true, true, false, false, false), "shortcut escaped the foreground scope");
        check(!shortcut.Poll(true, true, false, true, false), "focus change retriggered held Ctrl+Del");
        shortcut.Poll(false, false, false, true, false);
        check(!shortcut.Poll(false, false, true, true, false), "legacy F6 affected another game");
        check(!shortcut.Poll(false, false, true, true, true), "changing game scope retriggered held F6");
        shortcut.Poll(false, false, false, true, true);
        check(shortcut.Poll(false, false, true, true, true), "Dawnwalker F6 compatibility shortcut failed");
        check(!shortcut.Poll(false, false, true, true, true), "held Dawnwalker F6 repeated");
    }

    private static void CheckLocator(Action<bool, string> check)
    {
        var reads = new List<int>();
        IntPtr Read(int id) { reads.Add(id); return new IntPtr(123); }
        foreach (var ids in new[] { Array.Empty<int>(), new[] { 0 }, new[] { -1 }, new[] { 1, 2 } })
        {
            var result = GameWindowLocator.SelectUniqueWindow(ids, Read);
            check(result.ProcessIds.Length == 0 && result.Window == IntPtr.Zero, "ambiguous or invalid process targeting was accepted");
        }
        check(reads.Count == 0, "window discovery inspected an ambiguous process");
        var unique = GameWindowLocator.SelectUniqueWindow(new[] { 42, 42 }, Read);
        check(unique.ProcessIds.SequenceEqual(new[] { 42 }) && unique.Window == new IntPtr(123) && reads.SequenceEqual(new[] { 42 }),
            "unique process discovery failed to deduplicate metadata aliases");
        var module = new FutureModule();
        var absent = GameWindowLocator.ReadAsync(module, new GameInstallation("", "fixture", false, false, false, false, "")).GetAwaiter().GetResult();
        check(absent.ProcessIds.Length == 0 && absent.Window == IntPtr.Zero && module.Calls == 0, "uninstalled module triggered discovery or game commands");
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static Color Color(Brush brush) => ((SolidColorBrush)brush).Color;
    private static void Layout(Window window)
    {
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void Capture(Window window, string path)
    {
        var content = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    private sealed class FutureModule : IGameModule
    {
        public GameDefinition Definition { get; } = new("future-menu-fixture", "Future Game", "Fixture", "0", "FG", "Synthetic metadata only", new[] {
            new FeatureDefinition("future_switch", "Future toggle", "Enable a future feature.", "F1", true, "Survival"),
            new FeatureDefinition("future_amount", "Future amount", "Apply a bounded numeric value using the shared input.", "F2", Group: "Resources",
                ChoiceValueKey: "future_amount_value", Number: new FeatureNumber(0, 1000, 10)),
            new FeatureDefinition("future_mode", "Future multiplier", "Choose one of the module's declared values.", "F3", Group: "Resources",
                Choices: new[] { new FeatureChoice("gentle", "1×"), new FeatureChoice("strong", "2×") }, ChoiceValueKey: "future_mode_value"),
            new FeatureDefinition("future_action", "Future action", "Run a single future action.", "F4", Group: "Survival"),
            new FeatureDefinition("disable_all", "Disable all", "Cleanup is shown once in the footer.", "F12")
        });
        public IReadOnlyList<string> GameExecutables => new[] { "bin/FutureGame.exe" };
        public int Calls { get; private set; }
        private Exception Unexpected() { Calls++; return new InvalidOperationException("Menu fixture forbids module operations."); }
        public Task<GameInstallation> DiscoverAsync(string? directory = null) => throw Unexpected();
        public Task InstallAsync(GameInstallation installation, IProgress<string>? progress = null) => throw Unexpected();
        public Task RemoveAsync(GameInstallation installation, IProgress<string>? progress = null) => throw Unexpected();
        public Task<TrainerStatus> ReadStatusAsync(GameInstallation installation) => throw Unexpected();
        public Task SendAsync(GameInstallation installation, TrainerStatus status, string featureId, bool? enabled = null) => throw Unexpected();
        public Task LaunchAsync(GameInstallation installation) => throw Unexpected();
    }
}
