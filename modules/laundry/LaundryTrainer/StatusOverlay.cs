using System.Reflection;

namespace LaundryTrainer;

/// <summary>Uses the game's generated Unity wrappers, without shipping game assemblies.</summary>
internal sealed class StatusOverlay
{
    private readonly ConstructorInfo _rect;
    private readonly ConstructorInfo _content;
    private readonly MethodInfo _box;
    private readonly MethodInfo _label;
    private readonly MethodInfo _calcHeight;
    private readonly MethodInfo _createFont;
    private readonly MethodInfo _hasCharacter;
    private readonly PropertyInfo _skin;
    private readonly PropertyInfo _skinFont;
    private readonly object _labelStyle;
    private readonly object _titleStyle;
    private object? _font;

    public StatusOverlay()
    {
        var core = Assembly.Load("UnityEngine.CoreModule");
        var imgui = Assembly.Load("UnityEngine.IMGUIModule");
        var text = Assembly.Load("UnityEngine.TextRenderingModule");
        var rectType = core.GetType("UnityEngine.Rect", throwOnError: true)!;
        var colorType = core.GetType("UnityEngine.Color", throwOnError: true)!;
        var guiType = imgui.GetType("UnityEngine.GUI", throwOnError: true)!;
        var styleType = imgui.GetType("UnityEngine.GUIStyle", throwOnError: true)!;
        var contentType = imgui.GetType("UnityEngine.GUIContent", throwOnError: true)!;
        var skinType = imgui.GetType("UnityEngine.GUISkin", throwOnError: true)!;
        var fontType = text.GetType("UnityEngine.Font", throwOnError: true)!;
        _rect = rectType.GetConstructor(new[] { typeof(float), typeof(float), typeof(float), typeof(float) })
            ?? throw Missing("UnityEngine.Rect(float,float,float,float)");
        _content = contentType.GetConstructor(new[] { typeof(string) })
            ?? throw Missing("UnityEngine.GUIContent(string)");
        _box = guiType.GetMethod("Box", new[] { rectType, typeof(string) })
            ?? throw Missing("UnityEngine.GUI.Box(Rect,string)");
        _label = guiType.GetMethod("Label", new[] { rectType, typeof(string), styleType })
            ?? throw Missing("UnityEngine.GUI.Label(Rect,string,GUIStyle)");
        _calcHeight = styleType.GetMethod("CalcHeight", new[] { contentType, typeof(float) })
            ?? throw Missing("UnityEngine.GUIStyle.CalcHeight(GUIContent,float)");
        _createFont = fontType.GetMethod("CreateDynamicFontFromOSFont", new[] { typeof(string), typeof(int) })
            ?? throw Missing("UnityEngine.Font.CreateDynamicFontFromOSFont(string,int)");
        _hasCharacter = fontType.GetMethod("HasCharacter", new[] { typeof(char) })
            ?? throw Missing("UnityEngine.Font.HasCharacter(char)");
        _skin = guiType.GetProperty("skin") ?? throw Missing("UnityEngine.GUI.skin");
        _skinFont = skinType.GetProperty("font") ?? throw Missing("UnityEngine.GUISkin.font");

        // Fresh styles inherit the current skin font. This game's stripped Unity
        // wrappers retain GUISkin.font's setter, but not GUIStyle.font's setter.
        var white = Activator.CreateInstance(colorType, 1f, 1f, 1f, 1f)!;
        _labelStyle = CreateStyle(styleType, white, 16);
        _titleStyle = CreateStyle(styleType, white, 18);
    }

    public void Draw(IReadOnlyList<string> lines)
    {
        var skin = _skin.GetValue(null) ?? throw new InvalidOperationException("无法读取游戏界面样式。");
        var originalFont = _skinFont.GetValue(skin);
        _font ??= FindChineseFont(originalFont);
        try
        {
            // Only our drawing uses the Chinese font. Restore the game's skin
            // even if Unity throws so other IMGUI users keep their own settings.
            _skinFont.SetValue(skin, _font);
            var heights = new float[lines.Count];
            var totalHeight = 0f;
            for (var index = 0; index < lines.Count; index++)
            {
                var content = _content.Invoke(new object[] { lines[index] });
                heights[index] = Math.Max(26f,
                    (float)_calcHeight.Invoke(_labelStyle, new[] { content, (object)472f })! + 6f);
                totalHeight += heights[index];
            }
            _box.Invoke(null, new[] { Rect(16, 16, 500, 58 + totalHeight), "" });
            _label.Invoke(null, new[] { Rect(30, 28, 472, 28), "洗衣店模拟器修改器", _titleStyle });
            var y = 62f;
            for (var index = 0; index < lines.Count; index++)
            {
                _label.Invoke(null, new[] { Rect(30, y, 472, heights[index]), lines[index], _labelStyle });
                y += heights[index];
            }
        }
        finally
        {
            _skinFont.SetValue(skin, originalFont);
        }
    }

    private object FindChineseFont(object? originalFont)
    {
        foreach (var name in new[] { "Microsoft YaHei", "Microsoft YaHei UI", "SimHei", "SimSun", "DengXian", "Noto Sans CJK SC", "Noto Sans SC" })
        {
            try
            {
                var font = _createFont.Invoke(null, new object[] { name, 16 });
                if (SupportsChinese(font)) return font!;
            }
            catch (TargetInvocationException) { /* Try the next installed family. */ }
        }
        if (SupportsChinese(originalFont)) return originalFont!;
        throw new InvalidOperationException("未找到支持中文的字体，请在 Windows 中安装微软雅黑或黑体。");
    }

    private bool SupportsChinese(object? font) => font is not null &&
        "洗衣店模拟器金钱经验".All(character =>
            _hasCharacter.Invoke(font, new object[] { character }) is true);

    private static object CreateStyle(Type styleType, object color, int fontSize)
    {
        var style = Activator.CreateInstance(styleType)!;
        styleType.GetProperty("fontSize")!.SetValue(style, fontSize);
        styleType.GetProperty("wordWrap")!.SetValue(style, true);
        var normal = styleType.GetProperty("normal")!.GetValue(style)!;
        normal.GetType().GetProperty("textColor")!.SetValue(normal, color);
        return style;
    }

    private static MissingMethodException Missing(string name) =>
        new($"游戏界面接口不兼容：未找到 {name}。");

    private object Rect(float x, float y, float width, float height) =>
        _rect.Invoke(new object[] { x, y, width, height });
}
