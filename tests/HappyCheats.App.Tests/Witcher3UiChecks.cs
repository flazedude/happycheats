using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HappyCheats.App;
using HappyCheats.Core;

namespace HappyCheats.App.Tests;

internal static class Witcher3UiChecks
{
    public static object Run(MainWindow main, string? artifacts)
    {
        var originalSelection = main.Selected?.Id;
        var originalLanguage = Localization.Language;
        var originalTheme = main.ThemeToggleLabel == Localization.Text("themeDark") ? "light" : "dark";
        var originalWidth = main.Width;
        var originalHeight = main.Height;
        main.SelectGame("witcher-3");
        Check(main.Selected?.Id == "witcher-3", "The Witcher 3 must appear in the game library.");
        var game = main.Selected!;
        Check(game.Module is Witcher3Module && game.Module.Definition.AppId == "292030", "The Witcher 3 must use its real module and Steam identity.");
        var expected = new[] { "witcher3_health", "witcher3_stamina", "witcher3_adrenaline", "witcher3_toxicity", "witcher3_heal", "witcher3_money", "witcher3_xp", "witcher3_skill" };
        Check(main.Features.Select(f => f.Definition.Id).SequenceEqual(expected), "The Witcher 3 must expose its eight implemented controls in order.");
        Check(main.Features.Select(f => f.Hotkey).SequenceEqual(new[] { "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8" }), "Witcher shortcut mapping is incorrect.");
        Check(main.Features.Take(4).All(f => f.Definition.IsToggle) && main.Features.Skip(4).All(f => !f.Definition.IsToggle), "Witcher controls require four continuous effects and four one-shot actions.");
        var controls = main.Features.Count;
        foreach (var uri in new[] { GameArtwork.IconUri(game.Id), GameArtwork.LogoUri(game.Id) })
        {
            var image = BitmapFrame.Create(uri, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Check(image.PixelWidth > 0 && image.PixelHeight > 0, "The Witcher 3 artwork must load from the bundled application.");
        }
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var refresh = typeof(MainWindow).GetMethod("RefreshBindings", flags)!;
        var busy = typeof(MainWindow).GetField("_busy", flags)!;
        var originalBusy = busy.GetValue(main);
        var originalStatus = game.Status;
        var originalInstallation = game.Installation;
        void Update(TrainerStatus status, bool working = false)
        {
            game.Status = status;
            busy.SetValue(main, working);
            refresh.Invoke(main, null);
            main.UpdateLayout();
        }
        try
        {
            Update(TrainerStatus.Offline());
            Check(!main.CanDisableAll && main.Features.All(f => !f.CanUse), "Disconnected Witcher controls must be disabled.");
            game.Installation = new GameInstallation("C:/WitcherUiFixture", "unknown", true, false, false, false, "Unsupported fixture.");
            refresh.Invoke(main, null);
            Check(!main.CanInstall && !main.CanRemove, "Unsupported and uninstalled Witcher setup must not offer installation or removal.");
            game.Installation = new GameInstallation("C:/WitcherUiFixture", "fixture", true, true, true, true, "Simulated session only.");
            refresh.Invoke(main, null);
            Check(!main.CanRemove, "Witcher setup cannot be removed while the game is running.");
            var enabled = main.Features.Where(f => f.Definition.IsToggle).ToDictionary(f => f.Definition.Id, _ => false);
            var values = main.Features.ToDictionary(f => "available_" + f.Definition.Id, _ => "true");
            var ready = new TrainerStatus(true, true, "UI fixture; no game commands.", "witcher-ui-fixture", enabled, values);
            Update(ready);
            Check(main.Features.All(f => f.CanUse), "Witcher controls must use a fresh ready session.");
            var unavailable = new Dictionary<string, string>(values) { ["available_" + main.Features[0].Definition.Id] = "false" };
            Update(ready with { Values = unavailable });
            Check(!main.Features[0].CanUse && main.Features.Skip(1).All(f => f.CanUse), "Witcher controls must respect per-feature availability.");
            Update(ready, working: true);
            Check(!main.CanDisableAll && main.Features.All(f => !f.CanUse), "Witcher controls must wait during setup or a pending command.");
            var active = new Dictionary<string, bool>(enabled) { ["witcher3_health"] = true };
            Update(ready with { Ready = false, Enabled = active });
            Check(main.CanDisableAll && main.Features[0].CanUse && main.Features.Skip(1).All(f => !f.CanUse),
                "Outside ready gameplay, Witcher cleanup must allow disabling active effects without starting new effects or one-shot changes.");
            Update(ready with { Connected = false, Ready = false, Enabled = active });
            Check(!main.CanDisableAll && main.Features.All(f => !f.CanUse), "A stale Witcher active flag must not enable a disconnected command.");
            Update(ready);
            foreach (var language in new[] { "en", "zh" })
            foreach (var theme in new[] { "dark", "light" })
            {
                main.SetLanguage(language, persist: false);
                main.SetTheme(theme, persist: false);
                main.Width = 1180; main.Height = 820;
                var scroll = (ScrollViewer)main.FindName("DetailScroll");
                scroll.ScrollToTop(); main.UpdateLayout();
                if (language == "zh")
                {
                    Check(game.Name.Contains("巫师", StringComparison.Ordinal), "The Witcher 3 needs its Chinese title.");
                    Check(main.Features.All(f => f.Name != f.Definition.Name && f.Description != f.Definition.Description), "All Witcher controls need Chinese names and descriptions.");
                    Check(main.Features.All(f => f.Group != f.Definition.Group), "Witcher control groups need Chinese labels.");
                    Check(game.HeroLine.Contains("验证", StringComparison.Ordinal), "The Witcher overview must disclose gameplay validation limits in Chinese.");
                }
                else Check(main.Features.All(f => f.Name == f.Definition.Name && f.Description == f.Definition.Description), "English Witcher controls must retain module labels.");
                foreach (var message in TranslatableMessages)
                {
                    var translated = Localization.Message(message);
                    Check(language == "en" ? translated == message : translated != message && translated.Any(c => c is >= '\u4e00' and <= '\u9fff'),
                        "Witcher setup or connection diagnostic has the wrong language: " + message);
                }
                Check(Localization.ValueLabel("Crowns") == (language == "en" ? "Crowns" : "克朗")
                    && Localization.ValueLabel("Stamina") == (language == "en" ? "Stamina" : "活力"), "Witcher resource labels have the wrong language.");
                if (artifacts != null)
                {
                    Capture(main, Path.Combine(artifacts, $"witcher-3-{theme}-{language}.png"));
                    scroll.ScrollToBottom(); main.UpdateLayout();
                    Capture(main, Path.Combine(artifacts, $"witcher-3-bottom-{theme}-{language}.png"));
                }
                var pausedValues = new Dictionary<string, string>(values) { ["witcher3_waiting_for_game"] = bool.TrueString };
                var paused = ready with { Values = pausedValues, Message = PausedMessage };
                Update(paused);
                Check(main.ConnectionLabel == Localization.Choose("Game paused", "游戏已暂停")
                    && main.ControlsHint == Localization.Message(PausedMessage), "A paused Witcher snapshot must explain that the game needs to resume, without claiming live readiness.");
                Check(main.Features.All(f => f.CanUse && f.StateLabel == Localization.Text(f.Definition.IsToggle ? "off" : "ready")),
                    "Paused Witcher controls must retain their normal available state labels.");
                Check(main.Features.Take(4).All(f => f.ActionLabel == Localization.Text("turnOn")),
                    "Paused Witcher toggles must retain their normal Turn on buttons.");
                Check(!main.ValuesLine.Contains("witcher3_waiting_for_game", StringComparison.Ordinal), "The internal pause flag must not appear as a player resource.");
                Update(paused, working: true);
                Check(main.Features.All(f => !f.CanUse && f.StateLabel == Localization.Text("pleaseWait")),
                    "An unrelated setup operation must not tell the user to return to the game while trainer files are changing.");
                Update(paused with { Ready = false });
                Check(main.Features.All(f => !f.CanUse), "A paused snapshot without prior gameplay readiness must not enable new controls.");
                Update(paused with { Connected = false, Ready = false });
                Check(main.ConnectionLabel == Localization.Text("disconnected") && main.Features.All(f => !f.CanUse),
                    "A disconnected snapshot carrying a stale pause flag must remain disconnected.");
                Update(paused);
                scroll.ScrollToTop(); main.UpdateLayout();
                if (artifacts != null) Capture(main, Path.Combine(artifacts, $"witcher-3-paused-{theme}-{language}.png"));
                ValidatePendingCommand(main, game, paused, artifacts == null ? null : Path.Combine(artifacts, $"witcher-3-pending-{theme}-{language}.png"));
                Update(ready);
            }
        }
        finally
        {
            game.Installation = originalInstallation;
            game.Status = originalStatus;
            busy.SetValue(main, originalBusy);
            main.Width = originalWidth; main.Height = originalHeight;
            main.SetLanguage(originalLanguage, persist: false);
            main.SetTheme(originalTheme, persist: false);
            if (originalSelection != null) main.SelectGame(originalSelection);
            refresh.Invoke(main, null);
        }
        return new { Screen = "witcher-3", Controls = controls, NoGameCommands = true, BundledArtwork = true,
            OfflineAndPerFeatureAvailability = true, UnsupportedSetupBlocked = true, PausedIntentAndPendingMessage = true,
            TimedOutIntentDoesNotClaimSuccess = true, EnglishAndChinese = true, LightAndDark = true };
    }

    private const string PausedMessage = "The Witcher 3 is paused in the background. Select a control, then return to the game to apply it.";
    private const string WaitingMessage = "Control queued. Resume The Witcher 3 to apply it. Waiting for confirmation…";
    private const string ResumeTimeoutMessage = "No Witcher 3 acknowledgement received. This action may have applied; check the game before repeating it.";

    private static void ValidatePendingCommand(MainWindow main, GameItem original, TrainerStatus paused, string? capturePath)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var selected = typeof(MainWindow).GetField("_selected", flags)!;
        var refresh = typeof(MainWindow).GetMethod("RefreshBindings", flags)!;
        var pending = typeof(MainWindow).GetField("_witcherControlPending", flags)!;
        var module = new PendingCommandFixture(original.Module.Definition, paused, () =>
        {
            main.UpdateLayout();
            Check(!main.CanManage && !main.CanDisableAll && (bool)pending.GetValue(main)!, "A Witcher command must stay busy while waiting for gameplay to resume.");
            Check(main.Message == Localization.Message(WaitingMessage)
                && main.Features.All(f => !f.CanUse && f.StateLabel == Localization.Choose("Queued", "已排队")),
                "Pending Witcher controls must show the command is queued while waiting for game confirmation.");
            if (capturePath != null) Capture(main, capturePath);
        });
        var fixture = new GameItem(module) { Installation = original.Installation, Status = paused };
        try
        {
            selected.SetValue(main, fixture);
            var feature = module.Definition.Features.Single(f => f.Id == "witcher3_health");
            var operation = (Task)typeof(MainWindow).GetMethod("SendFeatureAsync", flags)!.Invoke(main, new object[] { feature })!;
            operation.GetAwaiter().GetResult();
            Check(module.SendCalls == 1 && main.CanManage && !(bool)pending.GetValue(main)!, "A timed-out Witcher intent must release the busy UI without retrying.");
            Check(main.Message == Localization.Message(ResumeTimeoutMessage) && !fixture.Status.Enabled.Values.Any(active => active),
                "An unconfirmed command must retain its acknowledgement error and must not show a successful toggle.");
        }
        finally { selected.SetValue(main, original); refresh.Invoke(main, null); }
    }

    private sealed class PendingCommandFixture(GameDefinition definition, TrainerStatus status, Action inspectPending) : IGameModule
    {
        public GameDefinition Definition => definition;
        public int SendCalls { get; private set; }
        public Task<TrainerStatus> ReadStatusAsync(GameInstallation installation) => Task.FromResult(status);
        public Task SendAsync(GameInstallation installation, TrainerStatus current, string featureId, bool? enabled = null)
        {
            Check(featureId == "witcher3_health" && enabled == true, "Unexpected paused-intent fixture action.");
            SendCalls++;
            inspectPending();
            return Task.FromException(new TimeoutException(ResumeTimeoutMessage));
        }
        public Task<GameInstallation> DiscoverAsync(string? directory = null) => throw new InvalidOperationException("Fixture discovery is forbidden.");
        public Task InstallAsync(GameInstallation installation, IProgress<string>? progress = null) => throw new InvalidOperationException("Fixture installation is forbidden.");
        public Task RemoveAsync(GameInstallation installation, IProgress<string>? progress = null) => throw new InvalidOperationException("Fixture removal is forbidden.");
        public Task LaunchAsync(GameInstallation installation) => throw new InvalidOperationException("Fixture launch is forbidden.");
    }

    private static void Capture(MainWindow main, string path)
    {
        var content = (FrameworkElement)main.Content;
        var target = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        target.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target));
        using var file = File.Create(path); encoder.Save(file);
    }

    private static readonly string[] TranslatableMessages =
    {
        "The Witcher 3 trainer installed. Start the game and load a save. Gameplay validation is pending.",
        "The Witcher 3 trainer removed. Unowned files were preserved.",
        "This Witcher 3 build needs a compatibility check. Only Steam Remastered 5.0 DX12 is supported.",
        "Unsupported The Witcher 3 executable. This trainer requires the verified Steam Remastered 5.0 DirectX 12 build.",
        "The Witcher 3 base script is modified or unsupported. Existing game files were preserved.",
        "Another Witcher 3 mod overrides r4game.ws. Disable or remove the conflicting override before installing HappyCheats: Mods/other/content/scripts/game/r4game.ws",
        "Modified or unowned The Witcher 3 scripts were preserved together: Mods/modHappyCheats/content/scripts/local/happycheats.ws",
        "Waiting for a fresh Witcher 3 trainer heartbeat.",
        "Waiting for Witcher 3 script initialization. Restart the game after installation.",
        "Witcher 3 script connected. Keep this page open for continuous effects.",
        "Load a save as living Geralt to use these controls.",
        "Witcher 3 session changed. Reconnect before trying again.",
        "No Witcher 3 acknowledgement received. This action may have applied; check the game before repeating it.",
        "The Witcher 3 cannot read its command files. Close the game and choose Install / update.",
        PausedMessage,
        WaitingMessage,
        ResumeTimeoutMessage,
        "The Witcher 3 trainer rejected this action: limit"
    };

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
