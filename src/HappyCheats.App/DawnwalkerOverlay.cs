using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using HappyCheats.Core;

namespace HappyCheats.App;

/// <summary>A non-activating companion panel for windowed/borderless gameplay.</summary>
internal sealed class DawnwalkerOverlay : Window
{
    private readonly StackPanel rows = new();
    private readonly List<(FeatureItem Feature, Button Button, TextBlock Name, TextBlock State)> featureRows = new();
    private readonly Button disable = new() { Focusable = false, Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 9, 0, 0) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock heading = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock hint = new() { TextWrapping = TextWrapping.Wrap, FontSize = 10, Margin = new Thickness(0, 12, 0, 0) };
    private readonly TextBlock values = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock pageLabel = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly ScrollViewer scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = 390, PanningMode = PanningMode.VerticalOnly };
    private readonly Action<int>? selectPage;
    private int shortcutPage;
    private double followAvailableHeight;
    private readonly Func<FeatureDefinition, System.Threading.Tasks.Task> send;
    private readonly Action dismiss;

    internal DawnwalkerOverlay(Func<FeatureDefinition, System.Threading.Tasks.Task> send, Action dismiss) : this(send, dismiss, null) { }

    internal DawnwalkerOverlay(Func<FeatureDefinition, System.Threading.Tasks.Task> send, Action dismiss, Action<int>? selectPage)
    {
        this.send = send;
        this.dismiss = dismiss;
        this.selectPage = selectPage;
        disable.SetResourceReference(BackgroundProperty, "ButtonBrush");
        disable.SetResourceReference(ForegroundProperty, "InkBrush");
        disable.SetResourceReference(BorderBrushProperty, "ButtonBorderBrush");
        disable.Click += async (_, _) => await send(new FeatureDefinition("disable_all", "Disable all", "", ""));
        Title = "HappyCheats · Dawnwalker";
        Width = 340;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        SetResourceReference(ForegroundProperty, "InkBrush");
        status.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        hint.SetResourceReference(TextBlock.ForegroundProperty, "SubtleBrush");
        values.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        FontFamily = new FontFamily("Segoe UI");
        var content = new StackPanel();
        var header = new DockPanel();
        var close = new Button { Content = "×", Width = 44, Height = 44, Padding = new Thickness(0), Margin = new Thickness(8, 0, 0, 0), Focusable = false };
        close.SetResourceReference(BackgroundProperty, "ButtonBrush");
        close.SetResourceReference(ForegroundProperty, "InkBrush");
        close.SetResourceReference(BorderBrushProperty, "ButtonBorderBrush");
        close.Click += (_, _) => dismiss();
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        header.Children.Add(heading);
        content.Children.Add(header);
        var navigation = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        foreach (var direction in new[] { -1, 1 })
        {
            var pageButton = new Button { Content = direction < 0 ? "◀" : "▶", Width = 42, Height = 34, Focusable = false };
            pageButton.SetResourceReference(BackgroundProperty, "ButtonBrush");
            pageButton.SetResourceReference(ForegroundProperty, "InkBrush");
            pageButton.SetResourceReference(BorderBrushProperty, "ButtonBorderBrush");
            pageButton.Click += (_, _) => {
                var next = (ShortcutPage + direction + 3) % 3;
                if (this.selectPage != null) this.selectPage(next); else ShortcutPage = next;
            };
            DockPanel.SetDock(pageButton, direction < 0 ? Dock.Left : Dock.Right);
            navigation.Children.Add(pageButton);
        }
        navigation.Children.Add(pageLabel);
        content.Children.Add(navigation);
        scroll.Content = rows;
        content.Children.Add(scroll);
        content.Children.Add(values);
        content.Children.Add(status);
        content.Children.Add(hint);
        var surface = new Border { Padding = new Thickness(18), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Child = content };
        surface.SetResourceReference(Border.BorderBrushProperty, "StrongLineBrush");
        surface.SetResourceReference(Border.BackgroundProperty, "SidebarBrush");
        Content = surface;
        LayoutUpdated += (_, _) => FitActionList();
        ThemeManager.Attach(this);
        SourceInitialized += (_, _) => {
            var handle = new WindowInteropHelper(this).Handle;
            // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW keeps keyboard/gamepad focus in game.
            SetWindowLongPtr(handle, -20, new IntPtr(GetWindowLongPtr(handle, -20).ToInt64() | 0x08000080));
            HwndSource.FromHwnd(handle)?.AddHook((IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled) => {
                if (msg == 0x0021) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE, accept click.
                return IntPtr.Zero;
            });
        };
    }

    internal int ShortcutPage
    {
        get => shortcutPage;
        set
        {
            var next = Math.Clamp(value, 0, 2);
            if (shortcutPage == next) return;
            shortcutPage = next;
            UpdatePage();
            scroll.ScrollToTop();
        }
    }

    internal static string ControllerDirections(int page) => page switch {
        1 => Localization.Choose("Hold Menu: ↑ Invulnerability · → Damage · ↓ Invisibility · ← Fly", "按住 Menu：↑ 无敌 · → 超级伤害 · ↓ 隐身 · ← 飞行"),
        2 => Localization.Choose("Hold Menu: ↑ Carry weight · → Money · ↓ Item use · ← Add XP", "按住 Menu：↑ 负重 · → 金钱 · ↓ 物品使用 · ← 增加经验"),
        _ => Localization.Choose("Hold Menu: ↑ Health · → Stamina · ↓ Refill · ← Blood", "按住 Menu：↑ 生命 · → 耐力 · ↓ 恢复 · ← 血量")
    };

    private static int PageFor(string id) => id switch {
        "dawnwalker_invulnerable" or "dawnwalker_damage" or "dawnwalker_invisible" or "dawnwalker_fly" => 1,
        "dawnwalker_carry" or "dawnwalker_money" or "dawnwalker_items" or "dawnwalker_xp" => 2,
        "dawnwalker_teleport" or "dawnwalker_auto_teleport" => -1,
        _ => 0
    };

    private void UpdatePage()
    {
        var name = ShortcutPage switch {
            1 => Localization.Choose("Combat & movement", "战斗与移动"),
            2 => Localization.Choose("Inventory & XP", "物品与经验"),
            _ => Localization.Choose("Survival", "生存")
        };
        pageLabel.Text = $"{name}  {ShortcutPage + 1}/3";
        foreach (var row in featureRows)
        {
            var page = PageFor(row.Feature.Definition.Id);
            row.Button.Visibility = page < 0 || page == ShortcutPage ? Visibility.Visible : Visibility.Collapsed;
        }
        hint.Text = ControllerDirections(ShortcutPage) + "\n" + Localization.Choose(
            "Menu+Y: teleport · Menu+X: auto teleport\nMenu+View: next page · F6 / Menu+RB: hide\nWindowed / borderless. Keep HappyCheats open.\nShortcuts also reach the game.",
            "Menu+Y：传送 · Menu+X：自动传送\nMenu+View：下一页 · F6 / Menu+RB：隐藏\n窗口 / 无边框。保持 HappyCheats 运行。\n按键也会传递给游戏。");
    }

    internal void Refresh(IEnumerable<FeatureItem> features, TrainerStatus current, bool busy, string message)
    {
        heading.Text = "DAWNWALKER  /  HappyCheats";
        var items = features.ToArray();
        // Heartbeats update existing controls so a poll cannot remove a button
        // between mouse-down and mouse-up or rebuild the whole visible panel.
        if (rows.Children.Count == 0 || items.Length != featureRows.Count || items.Where((feature, index) => !ReferenceEquals(feature, featureRows[index].Feature)).Any())
        {
            rows.Children.Clear();
            featureRows.Clear();
            foreach (var feature in items)
            {
                var label = new StackPanel();
                var name = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
                label.Children.Add(name);
                var state = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 10, Margin = new Thickness(0, 3, 0, 0) };
                state.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(FeatureItem.StateBrush)) { Source = feature });
                label.Children.Add(state);
                var button = new Button {
                    Content = label,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(10, 9, 10, 9), Margin = new Thickness(0, 9, 0, 0),
                    Focusable = false
                };
                button.SetBinding(BackgroundProperty, new Binding(nameof(FeatureItem.ActionBrush)) { Source = feature });
                button.SetBinding(BorderBrushProperty, new Binding(nameof(FeatureItem.ActionBorderBrush)) { Source = feature });
                button.SetResourceReference(ForegroundProperty, "InkBrush");
                button.Click += async (_, _) => await send(feature.Definition);
                featureRows.Add((feature, button, name, state));
                rows.Children.Add(button);
            }
            rows.Children.Add(disable);
        }
        foreach (var row in featureRows)
        {
            row.Name.Text = $"{row.Feature.Hotkey}  ·  {row.Feature.Name}";
            row.State.Text = row.Feature.StateLabel;
            row.Button.IsEnabled = row.Feature.CanUse;
            row.Button.ToolTip = row.Feature.Description;
        }
        disable.Content = Localization.Choose("F10 / Menu+LB  ·  Disable all", "F10 / Menu+LB  ·  全部关闭");
        disable.IsEnabled = current.Connected && !busy;
        status.Text = message;
        values.Text = MainWindow.FormatValues(current.Values);
        values.Visibility = string.IsNullOrEmpty(values.Text) ? Visibility.Collapsed : Visibility.Visible;
        UpdatePage();
    }

    internal void Follow(IntPtr gameWindow, bool visible)
    {
        if (!visible || gameWindow == IntPtr.Zero || IsIconic(gameWindow) || !GetWindowRect(gameWindow, out var rect)) { if (IsVisible) Hide(); return; }
        if (!IsVisible) Show();
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var corner = transform.Transform(new Point(rect.Right, rect.Top));
        var bottom = transform.Transform(new Point(rect.Right, rect.Bottom));
        followAvailableHeight = bottom.Y - corner.Y;
        FitActionList();
        Left = corner.X - Width - 24;
        Top = corner.Y + 54;
    }

    private void FitActionList()
    {
        if (!IsVisible || followAvailableHeight <= 0 || ActualHeight <= 0) return;
        // Keep the measured status and shortcuts visible as their text wraps or
        // changes language; only the action list scrolls on a small game window.
        var chromeHeight = ActualHeight - scroll.ActualHeight;
        var maximum = Math.Clamp(followAvailableHeight - 54 - 16 - chromeHeight, 0, 390);
        if (Math.Abs(scroll.MaxHeight - maximum) > 0.5) scroll.MaxHeight = maximum;
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rectangle);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
}
