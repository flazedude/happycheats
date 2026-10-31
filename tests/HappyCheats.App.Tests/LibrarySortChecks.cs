using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HappyCheats.App;

namespace HappyCheats.App.Tests;

internal static class LibrarySortChecks
{
    public static object Run(MainWindow main, string root, string? artifacts, Action<bool, string> check)
    {
        var library = (ListBox)main.FindName("Library");
        var search = (TextBox)main.FindName("SearchBox");
        var ascending = (RadioButton)main.FindName("SortAscendingButton");
        var descending = (RadioButton)main.FindName("SortDescendingButton");
        var expected = main.Games.OrderBy(game => game.Module.Definition.Name, StringComparer.InvariantCulture)
            .ThenBy(game => game.Id, StringComparer.InvariantCulture).Select(game => game.Id).ToArray();

        string[] VisibleIds() => library.Items.Cast<GameItem>().Select(game => game.Id).ToArray();
        void Click(RadioButton button)
        {
            // OnClick performs the RadioButton toggle and routed Checked/Click events together.
            typeof(RadioButton).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
            Layout(main);
        }
        void CheckOrder(bool reverse, string context)
        {
            var filtered = expected.Where(id => main.Games.Single(game => game.Id == id).SearchText
                .Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase));
            if (reverse) filtered = filtered.Reverse();
            check(VisibleIds().SequenceEqual(filtered), context + ": visible library order is incorrect.");
            check(main.LibrarySortAscending == !reverse && main.LibrarySortDescending == reverse,
                context + ": sort state differs from the displayed order.");
            check(ascending.IsChecked == !reverse && descending.IsChecked == reverse,
                context + ": radio indicators differ from the active sort.");
        }

        CheckOrder(reverse: false, "Fresh settings");
        check(main.Selected?.Id == expected[0], "Default selection must be the first alphabetically displayed game.");
        main.SelectGame("stardew");
        var selected = main.Selected;
        var features = main.Features.ToArray();
        void CheckSelection(string context)
        {
            check(ReferenceEquals(main.Selected, selected) && ReferenceEquals(library.SelectedItem, selected),
                context + ": sorting changed the selected game.");
            check(main.Features.SequenceEqual(features), context + ": sorting rebuilt the current trainer controls.");
        }

        Click(descending);
        CheckOrder(reverse: true, "Z to A click");
        CheckSelection("Z to A click");
        Click(ascending);
        CheckOrder(reverse: false, "A to Z click");
        CheckSelection("A to Z click");
        // Keyboard group navigation can change IsChecked without raising Click.
        descending.SetCurrentValue(RadioButton.IsCheckedProperty, true);
        Layout(main);
        CheckOrder(reverse: true, "Checked-only keyboard path");
        CheckSelection("Checked-only keyboard path");
        Click(ascending);

        search.Text = " a ";
        Layout(main);
        check(library.Items.Count > 1 && library.Items.Count < main.Games.Count, "Search fixture must contain a filtered group of games.");
        Click(descending);
        CheckOrder(reverse: true, "Filtered Z to A");
        CheckSelection("Filtered Z to A");
        Click(ascending);
        CheckOrder(reverse: false, "Filtered A to Z");
        CheckSelection("Filtered A to Z");
        search.Text = "星露谷";
        Layout(main);
        check(VisibleIds().SequenceEqual(new[] { "stardew" }), "Chinese search no longer finds Stardew Valley.");
        Click(descending);
        CheckSelection("Chinese search sort");
        search.Text = "__no_library_match__";
        Layout(main);
        Click(ascending);
        check(library.Items.Count == 0, "Sorting cleared an empty search filter.");
        search.Clear();
        Layout(main);
        CheckOrder(reverse: false, "Cleared search");
        main.SelectGame("stardew");

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var busy = typeof(MainWindow).GetField("_busy", flags)!;
        var refresh = typeof(MainWindow).GetMethod("RefreshBindings", flags)!;
        var originalBusy = busy.GetValue(main);
        try
        {
            main.Width = 800; main.Height = 480;
            foreach (var language in new[] { "en", "zh" })
            foreach (var theme in new[] { "dark", "light" })
            {
                main.SetLanguage(language, persist: false);
                main.SetTheme(theme, persist: false);
                Layout(main);
                CheckOrder(reverse: false, $"{language}/{theme} A to Z");
                Click(descending);
                CheckOrder(reverse: true, $"{language}/{theme} Z to A");
                Click(ascending);
                check(ascending.IsEnabled && descending.IsEnabled, "Sort controls did not start enabled.");
                var surface = (FrameworkElement)main.Content;
                foreach (var button in new[] { ascending, descending })
                {
                    var bounds = button.TransformToAncestor(surface).TransformBounds(new Rect(button.RenderSize));
                    check(button.IsVisible && bounds.Width >= 35 && bounds.Height >= 22
                        && bounds.Left >= 0 && bounds.Right <= surface.ActualWidth && bounds.Top >= 0 && bounds.Bottom <= surface.ActualHeight,
                        $"{language}/{theme}: library sort button is missing or clipped at 800x480.");
                    check(!string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)),
                        $"{language}/{theme}: library sort button has no accessible name.");
                }
                busy.SetValue(main, true); refresh.Invoke(main, null); Layout(main);
                check(ascending.IsEnabled && descending.IsEnabled && library.IsEnabled,
                    $"{language}/{theme}: library and sorting must stay clickable while the client is busy.");
                busy.SetValue(main, originalBusy); refresh.Invoke(main, null); Layout(main);
                check(ascending.IsEnabled && descending.IsEnabled, "Sort controls did not recover after busy state.");
                if (artifacts != null) Capture(main, Path.Combine(artifacts, $"library-sort-{theme}-{language}-800x480.png"));
            }
        }
        finally
        {
            busy.SetValue(main, originalBusy); refresh.Invoke(main, null);
            search.Clear();
            main.SetLibrarySort("az", persist: false);
        }

        ValidatePersistence(root, check);
        return new { Screen = "library-sorting", DefaultAscending = true, ActualRadioClickPath = true,
            SearchPreserved = true, SelectedTrainerPreserved = true, EnglishAndChinese = true,
            NarrowLayoutAndBusyState = true, IsolatedSettingsRoundTrip = true, NoGameOrNetworkActions = true };
    }

    private static void ValidatePersistence(string root, Action<bool, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "HappyCheats.App.Tests", Guid.NewGuid().ToString("N"));
        var settings = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(directory);
        var original = new Dictionary<string, string>
        {
            ["__theme"] = "dark", ["__language"] = "en", ["__xbox_shortcuts"] = "False",
            ["stardew"] = "C:/LibrarySortFixture/Stardew Valley"
        };
        File.WriteAllText(settings, JsonSerializer.Serialize(original));
        MainWindow? fixture = null;
        try
        {
            // Construct only: no Show or InitializeAsync, so no game discovery, status polling or network checks.
            fixture = new MainWindow(dataRoot: root, settingsPath: settings);
            // Unshown WPF windows still defer ItemsSource/SelectedIndex bindings to the dispatcher.
            Layout(fixture);
            check(fixture.LibrarySortAscending, "Existing settings without a sort must default to A to Z.");
            fixture.SetLibrarySort("za");
            fixture.Close(); fixture = null;
            var saved = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(settings))!;
            check(saved.GetValueOrDefault("__library_sort") == "za", "Z to A preference was not saved.");
            check(original.All(pair => saved.GetValueOrDefault(pair.Key) == pair.Value), "Saving sort changed unrelated preferences or game paths.");
            fixture = new MainWindow(dataRoot: root, settingsPath: settings);
            var library = (ListBox)fixture.FindName("Library");
            var beforeBindings = Describe(fixture, library);
            Layout(fixture);
            var descending = fixture.Games.OrderByDescending(game => game.Module.Definition.Name, StringComparer.InvariantCulture).ToArray();
            check(fixture.LibrarySortDescending && library.Items.Cast<GameItem>().SequenceEqual(descending)
                && ReferenceEquals(fixture.Selected, descending[0]),
                "Saved Z to A preference or its initial selection was not restored. Before binding drain: " + beforeBindings
                + "; after: " + Describe(fixture, library) + "; expected=" + string.Join(",", descending.Select(game => game.Id)));
            fixture.SetLibrarySort("az");
            fixture.Close(); fixture = null;
            saved = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(settings))!;
            check(saved.GetValueOrDefault("__library_sort") == "az", "A to Z preference was not saved.");
            saved["__library_sort"] = "invalid-old-value";
            File.WriteAllText(settings, JsonSerializer.Serialize(saved));
            var before = File.ReadAllText(settings);
            fixture = new MainWindow(dataRoot: root, settingsPath: settings);
            Layout(fixture);
            check(fixture.LibrarySortAscending && ((ListBox)fixture.FindName("Library")).Items.Cast<GameItem>().Select(game => game.Id)
                .SequenceEqual(fixture.Games.OrderBy(game => game.Module.Definition.Name, StringComparer.InvariantCulture).Select(game => game.Id)),
                "Invalid saved sort must safely fall back to A to Z. Actual: " + Describe(fixture, (ListBox)fixture.FindName("Library")));
            check(File.ReadAllText(settings) == before, "Reading an invalid preference rewrote the settings file.");
            fixture.Close(); fixture = null;
        }
        finally
        {
            fixture?.Close();
            // This fixture owns only this exact file and empty unique directory.
            File.Delete(settings);
            Directory.Delete(directory);
        }
    }

    private static string Describe(MainWindow window, ListBox library) =>
        $"az={window.LibrarySortAscending}, za={window.LibrarySortDescending}, count={library.Items.Count}, selected={window.Selected?.Id ?? "none"}, order=[{string.Join(",", library.Items.Cast<GameItem>().Select(game => game.Id))}]";

    private static void Layout(MainWindow window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }

    private static void Capture(MainWindow window, string path)
    {
        var content = (FrameworkElement)window.Content;
        var target = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        target.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var output = File.Create(path);
        encoder.Save(output);
    }
}
