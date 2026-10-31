using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using HappyCheats.Core;

namespace HappyCheats.App;

/// <summary>One metadata-driven companion menu for windowed and borderless games.</summary>
internal sealed class GameOverlay : Window
{
    private readonly Func<FeatureDefinition, Task> send;
    private readonly Func<FeatureChoiceItem, Task> selectChoice;
    private readonly StackPanel rows = new();
    private readonly ScrollViewer scroll = new() {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        PanningMode = PanningMode.VerticalOnly,
        Focusable = false
    };
    private readonly TextBlock heading = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock hint = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 9, 0, 0) };
    private readonly TextBlock shortcuts = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 9, 0, 0), Visibility = Visibility.Collapsed };
    private readonly TextBlock values = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    private readonly TextBlock status = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    private readonly ComboBox groupSelector = new() { MinHeight = 36, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 5, 0), Focusable = false };
    private readonly Button previous = CreateButton("‹");
    private readonly Button next = CreateButton("›");
    private readonly Button close = CreateButton("×");
    private readonly List<(FeatureItem Feature, Border Surface)> featureRows = new();
    private readonly Dictionary<string, string?> rememberedGroups = new(StringComparer.Ordinal);
    private GameItem? currentGame;
    private Button? disable;
    private int generation;
    private bool updatingGroups;

    internal string? CurrentGameId => currentGame?.Id;

    internal GameOverlay(Func<FeatureDefinition, Task> send, Func<FeatureChoiceItem, Task> selectChoice, Action dismiss)
    {
        this.send = send;
        this.selectChoice = selectChoice;
        Title = "HappyCheats";
        Width = 380;
        Height = 680;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowActivated = false;
        Focusable = false;
        ShowInTaskbar = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        FontFamily = new FontFamily("Segoe UI");
        SetResourceReference(ForegroundProperty, "InkBrush");
        hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        shortcuts.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        values.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        status.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        groupSelector.SetResourceReference(BackgroundProperty, "InputBrush");
        groupSelector.SetResourceReference(ForegroundProperty, "InkBrush");
        groupSelector.SetResourceReference(BorderBrushProperty, "StrongLineBrush");

        close.Width = close.Height = 38;
        close.Padding = new Thickness(0);
        close.Margin = new Thickness(10, 0, 0, 0);
        close.Click += (_, _) => dismiss();
        var header = new DockPanel();
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        header.Children.Add(heading);

        var navigation = new DockPanel { Margin = new Thickness(0, 13, 0, 0) };
        previous.Width = next.Width = 36;
        previous.Padding = next.Padding = new Thickness(0);
        previous.Click += (_, _) => MoveGroup(-1);
        next.Click += (_, _) => MoveGroup(1);
        DockPanel.SetDock(previous, Dock.Left);
        DockPanel.SetDock(next, Dock.Right);
        navigation.Children.Add(previous);
        navigation.Children.Add(next);
        navigation.Children.Add(groupSelector);
        groupSelector.SelectionChanged += (_, _) => {
            if (updatingGroups) return;
            ApplyGroup();
            scroll.ScrollToTop();
        };

        var content = new StackPanel { Margin = new Thickness(15) };
        content.Children.Add(header);
        content.Children.Add(hint);
        content.Children.Add(shortcuts);
        content.Children.Add(navigation);
        content.Children.Add(rows);
        content.Children.Add(values);
        content.Children.Add(status);
        // The whole surface can scroll, including its footer. Even an unusually
        // small game window must not make an action permanently unreachable.
        scroll.Content = content;
        var surface = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Child = scroll };
        surface.SetResourceReference(Border.BackgroundProperty, "SidebarBrush");
        surface.SetResourceReference(Border.BorderBrushProperty, "StrongLineBrush");
        Content = surface;
        ThemeManager.Attach(this);
        SourceInitialized += (_, _) => {
            var handle = new WindowInteropHelper(this).Handle;
            // Only explicit text editing may activate this companion window.
            // Cheat buttons and mouse navigation must leave focus in the game.
            SetWindowLongPtr(handle, -20, new IntPtr(GetWindowLongPtr(handle, -20).ToInt64() | 0x00000080));
            HwndSource.FromHwnd(handle)?.AddHook(WindowMessage);
        };
        Closed += (_, _) => { generation++; currentGame = null; };
    }

    private IntPtr WindowMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != 0x0021) return IntPtr.Zero; // WM_MOUSEACTIVATE
        if (AllowsKeyboardActivation(InputHitTest(Mouse.GetPosition(this)) as DependencyObject)) return IntPtr.Zero;
        handled = true;
        return new IntPtr(3); // MA_NOACTIVATE: deliver the click without changing foreground windows.
    }

    internal static bool AllowsKeyboardActivation(DependencyObject? target)
    {
        while (target != null)
        {
            if (target is TextBoxBase) return true;
            target = target is Visual ? VisualTreeHelper.GetParent(target) : LogicalTreeHelper.GetParent(target);
        }
        return false;
    }

    internal void Refresh(GameItem game, IEnumerable<FeatureItem> features, bool busy, bool canDisableAll, string message, string shortcutHint = "")
    {
        var items = features.Where(feature => feature.Definition.Id != "disable_all").ToArray();
        var changed = !ReferenceEquals(currentGame, game) || items.Length != featureRows.Count
            || items.Where((feature, index) => !ReferenceEquals(feature, featureRows[index].Feature)).Any();
        currentGame = game;
        Title = $"{game.Name} · HappyCheats";
        heading.Text = Title;
        hint.Text = Localization.Choose(
            "Ctrl+Del  ·  Show / hide menu\nController: hold Menu (☰) 0.35s, then tap RB.\nClick a control to use it. Close the menu to return to your game.\nWindowed / borderless · Keep HappyCheats open.",
            "Ctrl+Del  ·  显示 / 隐藏菜单\n手柄：按住 Menu（☰）0.35 秒，再按 RB。\n点击功能即可操作，关闭菜单后返回游戏。\n窗口 / 无边框模式 · 请保持 HappyCheats 运行。");
        AutomationProperties.SetName(close, Localization.Choose("Close in-game menu", "关闭游戏内菜单"));
        AutomationProperties.SetName(previous, Localization.Choose("Previous control group", "上一组功能"));
        AutomationProperties.SetName(next, Localization.Choose("Next control group", "下一组功能"));
        AutomationProperties.SetName(groupSelector, Localization.Choose("Control group", "功能分组"));
        AutomationProperties.SetName(scroll, Localization.Choose("In-game trainer controls", "游戏内修改器功能"));
        close.ToolTip = Localization.Choose("Close menu (Ctrl+Del / hold Menu + RB)", "关闭菜单（Ctrl+Del / 按住 Menu + RB）");
        shortcuts.Text = shortcutHint;
        shortcuts.Visibility = string.IsNullOrWhiteSpace(shortcutHint) ? Visibility.Collapsed : Visibility.Visible;
        values.Text = MainWindow.FormatValues(game.Status.Values);
        values.Visibility = string.IsNullOrWhiteSpace(values.Text) ? Visibility.Collapsed : Visibility.Visible;
        status.Text = message;
        status.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;

        if (changed)
        {
            var rowGeneration = ++generation;
            rows.Children.Clear();
            featureRows.Clear();
            foreach (var feature in items)
            {
                var surface = CreateFeatureRow(feature, rowGeneration);
                featureRows.Add((feature, surface));
                rows.Children.Add(surface);
            }
            disable = CreateButton("");
            disable.Margin = new Thickness(0, 12, 0, 0);
            disable.MinHeight = 38;
            disable.Click += async (_, _) => {
                if (rowGeneration != generation || currentGame == null) return;
                await this.send(new FeatureDefinition("disable_all", "Disable all", "", ""));
            };
            rows.Children.Add(disable);
            scroll.ScrollToTop();
        }
        if (disable != null)
        {
            disable.Content = Localization.Text("disableAll");
            AutomationProperties.SetName(disable, Localization.Text("disableAll"));
        }
        UpdateGroups(items);
    }

    private Border CreateFeatureRow(FeatureItem feature, int rowGeneration)
    {
        var content = new StackPanel();
        content.Children.Add(BoundText(feature, nameof(FeatureItem.Name), 13, bold: true));
        var description = BoundText(feature, nameof(FeatureItem.Description), 10);
        description.Margin = new Thickness(0, 4, 0, 0);
        description.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        content.Children.Add(description);
        var hotkey = BoundText(feature, nameof(FeatureItem.Hotkey), 10);
        hotkey.Margin = new Thickness(0, 5, 0, 0);
        hotkey.SetResourceReference(TextBlock.ForegroundProperty, "SubtleBrush");
        hotkey.SetBinding(VisibilityProperty, Bind(feature, nameof(FeatureItem.HotkeyVisibility)));
        content.Children.Add(hotkey);

        if (feature.Choices.Count != 0)
        {
            var choices = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
            foreach (var choice in feature.Choices)
            {
                var button = CreateButton("");
                button.Padding = new Thickness(9, 7, 9, 7);
                button.Margin = new Thickness(0, 0, 5, 5);
                button.Tag = choice;
                button.SetBinding(ContentControl.ContentProperty, Bind(choice, nameof(FeatureChoiceItem.Label)));
                button.SetBinding(BackgroundProperty, Bind(choice, nameof(FeatureChoiceItem.Background)));
                button.SetBinding(BorderBrushProperty, Bind(choice, nameof(FeatureChoiceItem.BorderBrush)));
                button.SetBinding(ForegroundProperty, Bind(choice, nameof(FeatureChoiceItem.Foreground)));
                var accessibleName = new MultiBinding { StringFormat = "{0}: {1}" };
                accessibleName.Bindings.Add(Bind(feature, nameof(FeatureItem.Name)));
                accessibleName.Bindings.Add(Bind(choice, nameof(FeatureChoiceItem.Label)));
                button.SetBinding(AutomationProperties.NameProperty, accessibleName);
                button.Click += async (_, _) => {
                    if (!CanDispatch(feature, rowGeneration)) return;
                    await selectChoice(choice);
                };
                choices.Children.Add(button);
            }
            content.Children.Add(choices);
        }
        if (feature.Definition.Number != null)
        {
            var number = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var unit = BoundText(feature, nameof(FeatureItem.NumberUnit), 10);
            unit.Margin = new Thickness(7, 0, 0, 0);
            unit.VerticalAlignment = VerticalAlignment.Center;
            unit.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            DockPanel.SetDock(unit, Dock.Right);
            number.Children.Add(unit);
            var input = new TextBox { MinHeight = 38, Padding = new Thickness(8, 5, 8, 5), MaxLength = 32, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center, Tag = feature };
            input.SetResourceReference(BackgroundProperty, "InputBrush");
            input.SetResourceReference(ForegroundProperty, "InkBrush");
            input.SetResourceReference(BorderBrushProperty, "StrongLineBrush");
            input.SetResourceReference(TextBox.CaretBrushProperty, "InkBrush");
            input.SetBinding(TextBox.TextProperty, new Binding(nameof(FeatureItem.NumberText)) { Source = feature, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            input.SetBinding(ToolTipProperty, Bind(feature, nameof(FeatureItem.NumberHint)));
            input.SetBinding(AutomationProperties.NameProperty, Bind(feature, nameof(FeatureItem.Name)));
            input.SetBinding(AutomationProperties.HelpTextProperty, Bind(feature, nameof(FeatureItem.NumberHint)));
            number.Children.Add(input);
            content.Children.Add(number);
            var confirmed = BoundText(feature, nameof(FeatureItem.ConfirmedNumberLabel), 10);
            confirmed.Margin = new Thickness(0, 4, 0, 0);
            confirmed.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            confirmed.SetBinding(VisibilityProperty, Bind(feature, nameof(FeatureItem.ConfirmedNumberVisibility)));
            content.Children.Add(confirmed);
            var error = BoundText(feature, nameof(FeatureItem.NumberError), 11);
            error.Margin = new Thickness(0, 4, 0, 0);
            error.SetResourceReference(TextBlock.ForegroundProperty, "ErrorBrush");
            error.SetBinding(VisibilityProperty, Bind(feature, nameof(FeatureItem.NumberErrorVisibility)));
            AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Polite);
            content.Children.Add(error);
        }

        var actionRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var action = CreateButton("");
        action.Tag = feature;
        action.MinHeight = 36;
        action.Margin = new Thickness(8, 0, 0, 0);
        action.SetBinding(ContentControl.ContentProperty, Bind(feature, nameof(FeatureItem.ActionLabel)));
        action.SetBinding(BackgroundProperty, Bind(feature, nameof(FeatureItem.ActionBrush)));
        action.SetBinding(BorderBrushProperty, Bind(feature, nameof(FeatureItem.ActionBorderBrush)));
        action.SetBinding(AutomationProperties.NameProperty, Bind(feature, nameof(FeatureItem.Name)));
        action.SetBinding(AutomationProperties.HelpTextProperty, Bind(feature, nameof(FeatureItem.Description)));
        action.Click += async (_, _) => {
            if (!CanDispatch(feature, rowGeneration)) return;
            await send(feature.Definition);
        };
        DockPanel.SetDock(action, Dock.Right);
        actionRow.Children.Add(action);
        var state = BoundText(feature, nameof(FeatureItem.StateLabel), 10);
        state.VerticalAlignment = VerticalAlignment.Center;
        state.SetBinding(TextBlock.ForegroundProperty, Bind(feature, nameof(FeatureItem.StateBrush)));
        actionRow.Children.Add(state);
        content.Children.Add(actionRow);
        var surface = new Border { Child = content, Tag = feature, Padding = new Thickness(11), Margin = new Thickness(0, 10, 0, 0), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7) };
        surface.SetResourceReference(Border.BackgroundProperty, "RaisedBrush");
        surface.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        return surface;
    }

    private bool CanDispatch(FeatureItem feature, int rowGeneration) => rowGeneration == generation && currentGame != null
        && featureRows.Any(row => ReferenceEquals(row.Feature, feature));

    private void UpdateGroups(FeatureItem[] items)
    {
        var keys = items.Select(feature => feature.Definition.Group).Distinct(StringComparer.Ordinal).ToArray();
        var selected = currentGame != null && rememberedGroups.TryGetValue(currentGame.Id, out var remembered) ? remembered : null;
        var existing = groupSelector.Items.Cast<ComboBoxItem>().ToArray();
        updatingGroups = true;
        try
        {
            if (existing.Length != keys.Length + 1 || keys.Where((key, index) => !Equals(existing[index + 1].Tag, key)).Any())
            {
                groupSelector.Items.Clear();
                groupSelector.Items.Add(new ComboBoxItem { Focusable = false });
                foreach (var key in keys) groupSelector.Items.Add(new ComboBoxItem { Tag = key, Focusable = false });
            }
            foreach (ComboBoxItem item in groupSelector.Items)
            {
                item.Content = item.Tag is string key ? Localization.Group(key) : Localization.Choose("All controls", "全部功能");
                item.SetResourceReference(ForegroundProperty, "InkBrush");
                item.SetResourceReference(BackgroundProperty, "SidebarBrush");
            }
            var selectedIndex = 0;
            for (var index = 1; index < groupSelector.Items.Count; index++)
                if (Equals(((ComboBoxItem)groupSelector.Items[index]).Tag, selected)) selectedIndex = index;
            if (groupSelector.SelectedIndex != selectedIndex) groupSelector.SelectedIndex = selectedIndex;
        }
        finally { updatingGroups = false; }
        ApplyGroup();
    }

    private void MoveGroup(int direction)
    {
        if (groupSelector.Items.Count == 0) return;
        groupSelector.SelectedIndex = (groupSelector.SelectedIndex + direction + groupSelector.Items.Count) % groupSelector.Items.Count;
    }

    private void ApplyGroup()
    {
        var selected = (groupSelector.SelectedItem as ComboBoxItem)?.Tag as string;
        if (currentGame != null) rememberedGroups[currentGame.Id] = selected;
        foreach (var row in featureRows)
            row.Surface.Visibility = selected == null || row.Feature.Definition.Group == selected ? Visibility.Visible : Visibility.Collapsed;
    }

    internal void Follow(IntPtr gameWindow, bool visible)
    {
        if (!visible || gameWindow == IntPtr.Zero || !IsWindow(gameWindow) || !IsWindowVisible(gameWindow) || IsIconic(gameWindow)
            || !GetWindowRect(gameWindow, out var rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
        {
            if (IsVisible) Hide();
            return;
        }
        // Create the HWND before looking up its WPF transform, without activating.
        new WindowInteropHelper(this).EnsureHandle();
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = transform.Transform(new Point(rect.Left, rect.Top));
        var bottomRight = transform.Transform(new Point(rect.Right, rect.Bottom));
        var bounds = CalculateBounds(new System.Windows.Rect(topLeft, bottomRight));
        Width = bounds.Width;
        Height = bounds.Height;
        Left = bounds.Left;
        Top = bounds.Top;
        if (!IsVisible) Show();
    }

    internal static System.Windows.Rect CalculateBounds(System.Windows.Rect gameBounds)
    {
        if (gameBounds.IsEmpty || gameBounds.Width <= 0 || gameBounds.Height <= 0) return System.Windows.Rect.Empty;
        var margin = Math.Min(16, Math.Min(gameBounds.Width, gameBounds.Height) / 20);
        var width = Math.Min(380, gameBounds.Width - margin * 2);
        var height = Math.Min(680, gameBounds.Height - margin * 2);
        return new System.Windows.Rect(gameBounds.Right - margin - width, gameBounds.Top + margin, width, height);
    }

    private static Binding Bind(object source, string path) => new(path) { Source = source };

    private static TextBlock BoundText(object source, string path, double size, bool bold = false)
    {
        var text = new TextBlock { FontSize = size, TextWrapping = TextWrapping.Wrap, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
        text.SetBinding(TextBlock.TextProperty, Bind(source, path));
        return text;
    }

    private static Button CreateButton(string text)
    {
        var button = new Button { Content = text, Padding = new Thickness(11, 7, 11, 7), Focusable = false };
        button.SetResourceReference(BackgroundProperty, "ButtonBrush");
        button.SetResourceReference(ForegroundProperty, "InkBrush");
        button.SetResourceReference(BorderBrushProperty, "ButtonBorderBrush");
        return button;
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
}
