using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HappyCheats.App;
using HappyCheats.Core;

namespace HappyCheats.App.Tests;

internal static class GtaVUiChecks
{
    public static object Run(MainWindow main, string? artifacts)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        main.SelectGame("gta-v-enhanced");
        Check(main.Selected?.Id == "gta-v-enhanced", "GTA V Enhanced is missing from the library.");
        var game = main.Selected!;
        Check(game.Module.Definition.AppId == "3240220", "GTA V must use the Enhanced Steam identity.");
        var expected = new[] { "gta_health", "gta_ammo", "gta_wanted", "gta_refill", "gta_repair", "gta_noon", "gta_clear_weather" };
        Check(main.Features.Select(f => f.Definition.Id).SequenceEqual(expected), "GTA V controls differ from its native menu.");
        Check(main.Features.Select(f => f.Hotkey).SequenceEqual(new[] { "F1", "F2", "F3", "F4", "F6", "F7", "F8" }), "GTA V shortcut mapping is incorrect.");
        foreach (var uri in new[] { GameArtwork.IconUri(game.Id), GameArtwork.LogoUri(game.Id) })
        {
            var frame = BitmapFrame.Create(uri, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Check(frame.PixelWidth > 0 && frame.PixelHeight > 0, "GTA V bundled artwork did not load.");
        }
        var busy = typeof(MainWindow).GetField("_busy", flags)!;
        var refresh = typeof(MainWindow).GetMethod("RefreshBindings", flags)!;
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
            Update(TrainerStatus.Offline("GTA Online is unsupported, including invite-only sessions."));
            Check(!main.CanDisableAll && main.Features.All(f => !f.CanUse), "GTA controls must be disabled without a supported Story Mode session.");
            // Discovery may admit the latest game before its external dependencies exist.
            // No actual game directory, DLL, runtime download or game process is used here.
            var latestInstallable = new GameInstallation("C:/GtaFixtureWithoutScriptHook", "25261616 / 1.0.1158.16", true, false, false, true,
                "Pinned GTA V Enhanced 1.0.1158.16 detected. Install / update obtains the verified ASI loader for the included experimental Story Mode backend. Native startup and gameplay are unverified.");
            game.Installation = latestInstallable;
            refresh.Invoke(main, null);
            Check(main.CanInstall && main.Features.All(f => !f.CanUse), "The reviewed latest build must allow setup without an external Script Hook or connected trainer.");
            game.Installation = latestInstallable with { Compatible = false,
                Message = "This GTA V Enhanced 1.0.1158.16 executable does not match the reviewed Steam build. The experimental backend requires its pinned executable hash; game files were preserved." };
            refresh.Invoke(main, null);
            Check(!main.CanInstall, "An incompatible executable must not permit installation even when its version matches the latest target.");
            game.Installation = latestInstallable;
            var ready = new TrainerStatus(true, true, "Story Mode fixture", new string('a', 32),
                expected.Take(3).ToDictionary(id => id, _ => false),
                expected.ToDictionary(id => "available_" + id, id => id == "gta_repair" ? "False" : "True")
                    .Concat(new[] { new KeyValuePair<string, string>("gta_online", "False") }).ToDictionary(pair => pair.Key, pair => pair.Value));
            Update(ready);
            Check(main.Features.Single(f => f.Definition.Id == "gta_repair").CanUse == false
                && main.Features.Where(f => f.Definition.Id != "gta_repair").All(f => f.CanUse), "Vehicle repair must follow native availability.");
            Update(ready, working: true);
            Check(main.Features.All(f => !f.CanUse), "GTA commands must stay disabled while an acknowledgement is pending.");
            Update(ready with { Ready = false, Enabled = expected.Take(3).ToDictionary(id => id, _ => true) });
            Check(main.CanDisableAll && main.Features.Take(3).All(f => f.CanUse)
                && main.Features.Skip(3).All(f => !f.CanUse), "GTA cleanup must be available while one-shot actions are gated.");
            Update(ready with { Ready = false, Values = new Dictionary<string, string> { ["gta_online"] = "True" } });
            Check(!main.CanDisableAll && main.Features.All(f => !f.CanUse), "An Online heartbeat must disable every companion control, including cleanup actions.");
            Update(TrainerStatus.Offline("GTA Online is unsupported, including invite-only sessions."));
            foreach (var language in new[] { "en", "zh" })
            foreach (var theme in new[] { "dark", "light" })
            {
                main.SetLanguage(language, persist: false);
                main.SetTheme(theme, persist: false);
                main.Width = 1180; main.Height = 820;
                ((ScrollViewer)main.FindName("DetailScroll")).ScrollToTop();
                main.UpdateLayout();
                if (language == "zh")
                {
                    Check(game.Name.Contains("增强版", StringComparison.Ordinal), "GTA Chinese name is missing.");
                    Check(main.Features.All(f => f.Name != f.Definition.Name && f.Description != f.Definition.Description), "GTA feature translations are missing.");
                    Check(game.HeroLine.Contains("邀请", StringComparison.Ordinal), "GTA Chinese description must explain invite-only limitation.");
                    Check(game.HeroLine.Contains("最新", StringComparison.Ordinal) && game.HeroLine.Contains("实验", StringComparison.Ordinal)
                        && game.HeroLine.Contains("自动", StringComparison.Ordinal) && game.HeroLine.Contains("尚待", StringComparison.Ordinal), "GTA Chinese description must state latest experimental setup and validation limits.");
                }
                else {
                    Check(game.HeroLine.Contains("invite-only", StringComparison.OrdinalIgnoreCase), "GTA description must explain invite-only limitation.");
                    Check(game.HeroLine.Contains("experimental", StringComparison.OrdinalIgnoreCase)
                        && game.HeroLine.Contains("unverified", StringComparison.OrdinalIgnoreCase), "GTA English description must retain its experimental validation limit.");
                }
                Check(game.HeroLine.Contains("1.0.1158.16", StringComparison.Ordinal) && game.HeroLine.Contains("F11", StringComparison.Ordinal), "GTA description must identify the latest target and menu key.");
                foreach (var message in TranslatableMessages)
                {
                    var translated = Localization.Message(message);
                    Check(language == "en" ? translated == message : translated != message && translated.Any(c => c is >= '\u4e00' and <= '\u9fff'),
                        "GTA setup/runtime diagnostic has the wrong language: " + message);
                }
                if (artifacts != null)
                {
                    var content = (FrameworkElement)main.Content;
                    var target = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    target.Render(content);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(target));
                    using var output = File.Create(Path.Combine(artifacts, $"gta-v-enhanced-{theme}-{language}.png"));
                    encoder.Save(output);
                }
            }
        }
        finally
        {
            game.Installation = originalInstallation;
            busy.SetValue(main, originalBusy);
            game.Status = originalStatus;
            refresh.Invoke(main, null);
        }
        return new { Screen = "gta-v-enhanced-controls", Controls = expected.Length, NoGameCommands = true,
            UnsupportedBuildBlocked = true, LatestBuildSetupWithoutScriptHook = true, RuntimeDiagnosticsTranslated = true,
            OfflineAndVehicleAvailability = true, EnglishAndChinese = true, LightAndDark = true };
    }

    private static readonly string[] TranslatableMessages = {
        "GTA V Enhanced 1.0.1158.13 detected. Install / update obtains the verified Script Hook V and ASI loader from the author. Native startup and gameplay are unverified.",
        "Pinned GTA V Enhanced 1.0.1158.16 detected. Install / update obtains the verified ASI loader for the included experimental Story Mode backend. Native startup and gameplay are unverified.",
        "Experimental GTA V Enhanced Story Mode trainer installed. Native startup and gameplay are unverified.",
        "GTA V Enhanced runtime setup is required. Close the game and choose Install / update.",
        "GTA V Enhanced 1.0.1158.17 is unsupported. Experimental targets are the pinned 1.0.1158.16 Steam build and 1.0.1158.13; game files were preserved.",
        "This GTA V Enhanced 1.0.1158.16 executable does not match the reviewed Steam build. The experimental backend requires its pinned executable hash; game files were preserved.",
        "Obtaining the verified Script Hook V runtime from its author or the local cache.",
        "Obtaining the verified ASI loader from its author or the local cache.",
        "Installing the verified runtime; existing unrecognized loaders and hooks are preserved.",
        "An unrecognized existing dinput8.dll was preserved. Resolve the runtime conflict before installing.",
        "Script Hook V archive failed the pinned size/SHA-256 verification. No runtime was installed.",
        "Could not safely roll back ScriptHookV.dll. Original backup: C:/GtaFixture/backups/ScriptHookV.dll.bak",
        "Could not safely roll back dinput8.dll. The newly installed runtime file remains for inspection.",
        "GTA V Enhanced payload checksum failed: HappyCheatsGtaV/Source.zip",
        "Runtime setup failed and recovery needs attention. Existing backups were retained.",
        "Validating the experimental Enhanced 1.0.1158.16 source runtime.",
        "Enhanced source runtime inactive: Expected one session guard pattern; found 0",
        "Waiting for valid game script TLS",
        "Session guard has invalid data; experimental runtime disabled",
        "Online session detected; source runtime disabled until the game restarts",
        "GTA Online detected. Trainer locked until the game restarts, including invite-only sessions."
    };

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
