using HappyCheats.Core;

namespace HappyCheats.App;

internal static class Witcher3Localization
{
    private static readonly Dictionary<string, (string Name, string Description)> Features = new(StringComparer.Ordinal)
    {
        ["witcher3_health"] = ("无限生命", "持续补满存活的杰洛特的生命；两次恢复之间受到致命伤害仍可能死亡。需要保持此游戏页面打开。"),
        ["witcher3_stamina"] = ("无限活力", "持续补满杰洛特的活力。需要保持此游戏页面打开。"),
        ["witcher3_adrenaline"] = ("满肾上腺素", "持续补满杰洛特的肾上腺素。需要保持此游戏页面打开。"),
        ["witcher3_toxicity"] = ("清除魔药毒性", "持续清空杰洛特的魔药毒性，保留煎药毒性。需要保持此游戏页面打开。"),
        ["witcher3_heal"] = ("恢复生命与活力", "一次性补满存活的杰洛特的生命与活力，不会复活已死亡角色。"),
        ["witcher3_money"] = ("增加 1,000 克朗", "一次性增加 1,000 克朗。增加的金钱可能随正常存档保存；关闭修改器不会撤销。"),
        ["witcher3_xp"] = ("增加 1,000 基础经验", "通过游戏规则增加 1,000 基础经验；受新游戏+倍率和等级上限影响。成长进度可能随正常存档保存，关闭修改器不会撤销。"),
        ["witcher3_skill"] = ("增加 1 技能点", "一次性增加 1 技能点。成长进度可能随正常存档保存；关闭修改器不会撤销。")
    };

    public const string ChineseDescription = "面向 Steam 重制版 5.0.0.1041720 / DX12 的八项实验性角色与成长功能。持续功能需要保持此游戏页面打开；连接中断六秒后自动关闭。金钱和成长修改可能随存档保存。尚未验证游戏内脚本编译、加载与实际效果。";

    public static string? FeatureName(FeatureDefinition feature) => Features.TryGetValue(feature.Id, out var text) ? text.Name : null;
    public static string? FeatureDescription(FeatureDefinition feature) => Features.TryGetValue(feature.Id, out var text) ? text.Description : null;
    public static string? ValueLabel(string key) => key switch
    {
        "witcher3_validation" => Localization.Choose("Validation", "验证情况"),
        "witcher3_health" => Localization.Choose("Health", "生命"),
        "witcher3_stamina" or "Stamina" => Localization.Choose("Stamina", "活力"),
        "witcher3_adrenaline" => Localization.Choose("Adrenaline", "肾上腺素"),
        "witcher3_toxicity" => Localization.Choose("Toxicity", "毒性"),
        "witcher3_money" or "Crowns" => Localization.Choose("Crowns", "克朗"),
        "witcher3_xp" => Localization.Choose("Experience", "经验"),
        "witcher3_skill" => Localization.Choose("Skill points", "技能点"),
        _ => null
    };
    public static string? Translate(string text)
    {
        if (text == "The Witcher 3 trainer rejected this action: limit") return "巫师 3 修改器未增加数值：已达到游戏上限，或游戏规则阻止了此次增加。";
        foreach (var (english, chinese) in new[]
        {
            ("Modified The Witcher 3 files were preserved: ", "已保留修改过的巫师 3 文件："),
            ("Modified or unowned The Witcher 3 scripts were preserved together: ", "修改过的或不属于本修改器的巫师 3 脚本已一并保留："),
            ("The Witcher 3 trainer removed. Modified bridge files were preserved: ", "已移除巫师 3 修改器；修改过的通信文件已保留："),
            ("A Witcher 3 trainer file path is a directory: ", "巫师 3 修改器文件位置被文件夹占用："),
            ("Another Witcher 3 mod overrides r4game.ws. Disable or remove the conflicting override before installing HappyCheats: ", "其他巫师 3 模组覆盖了 r4game.ws。请先禁用或移除冲突的覆盖文件，再安装 HappyCheats："),
            ("Preserved an existing or modified Witcher 3 bridge file: ", "已保留现有或修改过的巫师 3 通信文件："),
            ("Preserved an existing or modified Witcher 3 mod file: ", "已保留现有或修改过的巫师 3 模组文件："),
            ("The Witcher 3 trainer rejected this action: ", "巫师 3 修改器拒绝了此操作：")
        })
            if (text.StartsWith(english, StringComparison.Ordinal)) return chinese + text[english.Length..];
        return text switch
        {
            "The Witcher 3 trainer installed. Start the game and load a save. Gameplay validation is pending." => "已安装巫师 3 修改器。请启动游戏并载入存档；尚待实际游玩验证。",
            "The Witcher 3 trainer removed. Unowned files were preserved." => "已移除巫师 3 修改器；不属于本修改器的文件已保留。",
            "Invalid The Witcher 3 package or ownership record." => "巫师 3 修改器安装包或文件归属记录无效。",
            "Unsupported The Witcher 3 package identity." => "不支持此巫师 3 修改器安装包标识。",
            "Missing The Witcher 3 package files." => "巫师 3 修改器安装包缺少文件列表。",
            "The Witcher 3 package is missing its trainer script." => "巫师 3 修改器安装包缺少必需的修改器脚本。",
            "The Witcher 3 payload checksum failed." => "巫师 3 修改器安装包校验失败。",
            "Unsupported The Witcher 3 executable. This trainer requires the verified Steam Remastered 5.0 DirectX 12 build." => "不支持此巫师 3 程序。此修改器需要经校验的 Steam 重制版 5.0 DirectX 12 版本。",
            "The Witcher 3 base script is modified or unsupported. Existing game files were preserved." => "巫师 3 基础脚本已被修改或不受支持；现有游戏文件已保留。",
            "The Witcher 3 payload changed during installation." => "巫师 3 修改器安装包在安装期间发生变化。",
            "The verified Witcher 3 script does not have the expected integration points." => "经校验的巫师 3 脚本缺少预期的集成位置。",
            "The Witcher 3 script template has no bridge placeholder." => "巫师 3 脚本模板缺少通信路径占位符。",
            "The Witcher 3 ownership receipt is unexpectedly large." => "巫师 3 文件归属记录异常过大。",
            "Invalid The Witcher 3 ownership receipt." => "巫师 3 文件归属记录无效。",
            "No Witcher 3 ownership receipt was found. Existing files were preserved." => "未找到巫师 3 文件归属记录；现有文件已保留。",
            "A Witcher 3 script changed during removal. Its files were preserved." => "巫师 3 脚本在移除期间发生变化；文件已保留。",
            "A Witcher 3 trainer file is unexpectedly large; it was preserved." => "巫师 3 修改器文件异常过大；该文件已保留。",
            "A Witcher 3 file changed before installation commit; it was preserved." => "巫师 3 文件在完成安装前发生变化；该文件已保留。",
            "The Witcher 3 operation failed; review recovery details." => "巫师 3 操作失败，请查看恢复详情。",
            "This Witcher 3 build needs a compatibility check. Only Steam Remastered 5.0 DX12 is supported." => "此巫师 3 版本需要兼容性检查；仅支持 Steam 重制版 5.0 DX12。",
            "Experimental Witcher 3 trainer installed. Load a save as Geralt; keep this page open. Gameplay is unverified." => "已安装巫师 3 实验性修改器。请载入杰洛特存档并保持此页面打开；实际游玩仍待验证。",
            "Steam Remastered 5.0 DX12 detected. Install the experimental trainer before launching." => "已检测到 Steam 重制版 5.0 DX12。请在启动游戏前安装实验性修改器。",
            "Close The Witcher 3 before changing trainer files." => "更改修改器文件前，请先关闭巫师 3。",
            "Unsupported Witcher 3 executable or vanilla scripts. Game files were preserved." => "巫师 3 程序或基础脚本不受支持；游戏文件已保留。",
            "Waiting for a fresh Witcher 3 trainer heartbeat." => "正在等待巫师 3 修改器更新连接状态。",
            "Waiting for Witcher 3 script initialization." => "正在等待巫师 3 修改器脚本初始化。",
            "Invalid Witcher 3 trainer status. Restart after installing the current trainer." => "巫师 3 修改器状态无效；请安装当前修改器并重启游戏。",
            "Witcher 3 script connected. Keep this page open for continuous effects." => "巫师 3 修改器脚本已连接；请保持此页面打开以使用持续功能。",
            "The Witcher 3 is paused in the background. Select a control, then return to the game to apply it." => "巫师 3 已在后台暂停。请选择一项功能，然后返回游戏使其生效。",
            "Control queued. Resume The Witcher 3 to apply it. Waiting for confirmation…" => "指令已排队。请返回巫师 3 使其生效。正在等待游戏确认……",
            "Load a save as living Geralt to use these controls." => "请载入杰洛特存档，并确保角色存活后再使用功能。",
            "Launch The Witcher 3 and load a save as Geralt." => "请启动巫师 3 并载入杰洛特存档。",
            "Invalid Witcher 3 status size." => "巫师 3 修改器状态文件大小无效。",
            "Waiting for Witcher 3 script initialization. Restart the game after installation." => "正在等待巫师 3 修改器脚本初始化；安装后请重启游戏。",
            "Install the Witcher 3 trainer first." => "请先安装巫师 3 修改器。",
            "The Witcher 3 cannot read its command files. Close the game and choose Install / update." => "巫师 3 无法读取指令文件。请关闭游戏，然后选择“安装 / 更新”。",
            "Witcher 3 session changed. Reconnect before trying again." => "巫师 3 游戏会话已改变；请重新连接后再试。",
            "Unknown Witcher 3 control." => "未知的巫师 3 修改器功能。",
            "Invalid Witcher 3 toggle state." => "巫师 3 修改器开关状态无效。",
            "Load a save as living Geralt before using this control." => "使用此功能前，请载入杰洛特存档并确保角色存活。",
            "Waiting for the previous Witcher 3 command." => "正在等待上一条巫师 3 修改器指令完成。",
            "Witcher 3 session changed. Check the game before repeating this action." => "巫师 3 游戏会话已改变；重复此操作前请先检查游戏。",
            "No Witcher 3 acknowledgement received. This action may have applied; check the game before repeating it." => "未收到巫师 3 修改器确认。此操作可能已经生效，重复操作前请先检查游戏。",
            _ => null
        };
    }
}
