using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace HappyCheats.App;

internal static class ThemeManager
{
    private static readonly Dictionary<string, ResourceDictionary> Palettes = new(StringComparer.Ordinal);
    private static readonly DependencyProperty IsAttachedProperty = DependencyProperty.RegisterAttached(
        "IsAttached", typeof(bool), typeof(ThemeManager), new PropertyMetadata(false));

    public static bool IsLight { get; private set; }
    public static event EventHandler? Changed;

    public static void Apply(string theme)
    {
        var application = Application.Current ?? throw new InvalidOperationException("An application is required to apply a theme.");
        if (!application.Dispatcher.CheckAccess())
        {
            application.Dispatcher.Invoke(() => Apply(theme));
            return;
        }

        var light = string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase);
        var name = light ? "Light" : "Dark";
        var dictionaries = application.Resources.MergedDictionaries;
        var current = dictionaries.FirstOrDefault(dictionary => IsPalette(dictionary.Source));
        if (light == IsLight && current?.Source?.OriginalString.EndsWith($"/{name}.xaml", StringComparison.OrdinalIgnoreCase) == true)
            return;

        if (!Palettes.TryGetValue(name, out var palette))
        {
            palette = new ResourceDictionary { Source = new Uri($"/HappyCheats;component/Themes/{name}.xaml", UriKind.Relative) };
            foreach (var value in palette.Values.OfType<Freezable>())
                if (value.CanFreeze) value.Freeze();
            Palettes.Add(name, palette);
        }

        if (current == null) dictionaries.Insert(0, palette);
        else dictionaries[dictionaries.IndexOf(current)] = palette;
        IsLight = light;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static Brush Brush(string resourceKey) => Application.Current?.TryFindResource(resourceKey) as Brush
        ?? throw new ArgumentException($"Theme brush '{resourceKey}' was not found.", nameof(resourceKey));

    public static void Attach(Window window)
    {
        if ((bool)window.GetValue(IsAttachedProperty)) return;
        window.SetValue(IsAttachedProperty, true);
        void UpdateTitleBar(object? sender, EventArgs args) => ApplyTitleBar(window);
        window.SourceInitialized += UpdateTitleBar;
        Changed += UpdateTitleBar;
        window.Closed += (_, _) =>
        {
            window.SourceInitialized -= UpdateTitleBar;
            Changed -= UpdateTitleBar;
        };
        ApplyTitleBar(window);
    }

    private static bool IsPalette(Uri? source) => source?.OriginalString.EndsWith("/Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase) == true
        || source?.OriginalString.EndsWith("/Themes/Light.xaml", StringComparison.OrdinalIgnoreCase) == true
        || source?.OriginalString.Equals("Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase) == true
        || source?.OriginalString.Equals("Themes/Light.xaml", StringComparison.OrdinalIgnoreCase) == true;

    private static void ApplyTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var dark = IsLight ? 0 : 1;
        // Windows 10 used 19 before the attribute was formalized as 20. Older
        // systems may decline both while WPF content still follows the theme.
        if (DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
