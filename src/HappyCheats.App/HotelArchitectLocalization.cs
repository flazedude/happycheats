using HappyCheats.Core;

namespace HappyCheats.App;

internal static class HotelArchitectLocalization
{
    private static readonly Dictionary<string, (string Name, string Description)> Features = new()
    {
        ["ha_no_needs"] = ("无需求", "持续清空存活人物的如厕需求，补满心情、健康与员工精力；不改变客人的服务目标。"),
        ["ha_always_noon"] = ("始终正午", "将模拟时钟冻结在 12:00，保留当前日期；时间相关活动也可能停留在正午。编辑时间前请先关闭此功能。"),
        ["ha_furniture_condition"] = ("家具状态与清洁度不减", "开启后持续恢复当前酒店家具的状态与清洁度。"),
        ["ha_game_speed"] = ("游戏速度", "设置游戏原生速度档位：0 暂停、1 正常、2 加快、3 最快。"),
        ["ha_days_passed"] = ("修改已过天数", "修改游戏记录的已过天数。日期变化可能影响酒店经营。"),
        ["ha_disable_aging"] = ("停止衰老", "开启后暂停酒店人物的衰老。请保持 HappyCheats 运行；关闭功能时恢复此前设置。"),
        ["ha_money"] = ("修改金钱", "将当前酒店的资金设为输入的数值。"),
        ["ha_stars"] = ("修改星级", "修改当前酒店的星级。游戏自身的评级规则可能再次更新此数值。"),
        ["ha_day_number"] = ("修改日期编号", "设置游戏时钟的日期编号。"),
        ["ha_hour"] = ("修改小时", "设置游戏时钟的小时。"),
        ["ha_minute"] = ("修改分钟", "设置游戏时钟的分钟。"),
        ["ha_total_hours"] = ("修改总游戏小时数", "按游戏的日期编号 × 24 + 当天时间设置总小时数，同时更新日期和时钟。"),
        ["ha_restroom_need"] = ("修改如厕需求", "设置所选人物的如厕需求。请先在游戏中选择人物。"),
        ["ha_mood"] = ("修改心情", "设置所选人物的心情。请先在游戏中选择人物。"),
        ["ha_health"] = ("修改健康", "设置所选人物的健康值。请先在游戏中选择人物。"),
        ["ha_xp"] = ("修改当前经验", "设置所选员工的当前经验，必须低于当前等级 × 110；等级 10 的经验只能为 0。"),
        ["ha_level"] = ("修改等级", "将所选员工等级设为 1–10，并使当前经验保持在该等级允许的范围内。"),
        ["ha_retirement_days"] = ("修改距退休天数", "设置所选员工距退休的天数。请先选择员工。"),
        ["ha_energy"] = ("修改精力", "设置所选员工的精力。请先选择员工。"),
        ["ha_room_value_multiplier"] = ("修改房间价值倍率", "修改所选家具对房间价值的倍率。请先在游戏中选择家具。"),
        ["ha_cleanliness"] = ("修改清洁度", "设置所选家具的清洁度。请先在游戏中选择家具。"),
        ["ha_attractiveness"] = ("修改吸引力", "设置所选家具的正面吸引力数值。"),
        ["ha_negative_attractiveness"] = ("修改负面吸引力", "设置所选家具的负面吸引力数值。"),
        ["ha_condition"] = ("修改状态", "设置所选家具的状态值。请先在游戏中选择家具。")
    };

    public static string? Name(FeatureDefinition feature) => Features.TryGetValue(feature.Id, out var value) ? value.Name : null;
    public static string? Description(FeatureDefinition feature) => Features.TryGetValue(feature.Id, out var value) ? value.Description : null;
    public static string? ValueLabel(string key)
    {
        if (key == "ha_validation") return Localization.Choose("Validation", "验证情况");
        if (key == "ha_selection") return Localization.Choose("Selection", "当前选择");
        if (!Features.TryGetValue(key, out var value)) return null;
        if (Localization.Chinese) return value.Name.Replace("修改", "", StringComparison.Ordinal);
        return key[3..].Replace('_', ' ');
    }

    public static string? Message(string source) => source switch
    {
        "This Hotel Architect build needs a compatibility check." => "此《酒店建筑师》版本需要兼容性检查。",
        "Experimental Hotel Architect trainer installed. Gameplay validation is pending." => "已安装《酒店建筑师》实验性修改器，尚待游戏内验证。",
        "Hotel Architect trainer update required. Installed files differ from the current module." => "《酒店建筑师》修改器需要更新，已安装文件与当前版本不一致。",
        "Hotel Architect Steam build 25340122 detected. Experimental trainer; gameplay validation is pending." => "已识别 Steam 构建 25340122。实验性修改器，尚待游戏内验证。",
        "Close Hotel Architect before changing trainer files." => "修改文件前，请先关闭《酒店建筑师》。",
        "Unsupported Hotel Architect executable. Game files were preserved." => "暂不支持此《酒店建筑师》程序，游戏文件已保留。",
        "Install Hotel Architect through Steam first." => "请先通过 Steam 安装《酒店建筑师》。",
        "Launch Hotel Architect to connect." => "请启动《酒店建筑师》以连接修改器。",
        "Waiting for Hotel Architect trainer status." => "正在等待《酒店建筑师》修改器状态。",
        "Invalid Hotel Architect trainer status." => "《酒店建筑师》修改器状态无效。",
        "Waiting for a fresh Hotel Architect trainer heartbeat." => "正在等待《酒店建筑师》更新连接状态。",
        "Restart Hotel Architect to load the current trainer module." => "请重启《酒店建筑师》以载入当前修改器。",
        "Hotel Architect trainer connected." => "《酒店建筑师》修改器已连接。",
        "Load a single-player save before using Hotel Architect controls." => "请先载入单人酒店存档，再使用修改器功能。",
        "Hotel Architect session changed. Reconnect before trying again." => "酒店或所选对象已改变，请重新连接后再试。",
        "This Hotel Architect control is unavailable in the current game session." => "当前场景或所选对象无法使用此功能。",
        "Hotel Architect rejected this control." => "《酒店建筑师》未接受此操作。",
        "The Hotel Architect trainer command directory is unavailable." => "《酒店建筑师》修改器指令目录不可用。",
        "Hotel Architect trainer starting; waiting for the game thread." => "《酒店建筑师》修改器正在启动，等待游戏就绪。",
        "Unsupported Hotel Architect executable. Reinstall the supported trainer after an update." => "此游戏版本暂不受支持，请在更新后安装匹配的修改器。",
        "Hotel Architect native property registration failed." => "《酒店建筑师》原生属性连接失败，功能已停用。",
        "Load a Hotel Architect hotel to connect." => "请载入酒店以连接修改器。",
        "Hotel Architect connected. Select a person or furniture for its editors." => "修改器已连接。请在游戏中选择人物或家具以使用相应编辑器。",
        "Hotel or selection changed. Reconnect before editing." => "酒店或所选对象已改变，请重新连接后再编辑。",
        "This control needs its supported game binding and an appropriate selected target." => "此功能需要有效的游戏连接及适合的所选对象。",
        "Hotel Architect control enabled." => "《酒店建筑师》功能已开启。",
        "Hotel Architect control disabled." => "《酒店建筑师》功能已关闭。",
        "All Hotel Architect toggles disabled." => "《酒店建筑师》持续生效功能已全部关闭。",
        "Hotel Architect restoration is pending." => "《酒店建筑师》仍在等待恢复此前设置。",
        "Hotel Architect write could not be verified." => "无法确认《酒店建筑师》是否接受了修改。",
        "Hotel Architect write could not be verified; it was not retried." => "无法确认修改结果，未自动重试。",
        "Hotel Architect value updated once." => "《酒店建筑师》数值已修改一次。",
        "Current XP must be below 110 times the selected staff level; level 10 uses zero XP." => "当前经验必须小于所选员工等级 × 110；等级 10 的经验只能为 0。",
        "Experimental; native startup and gameplay remain unverified." => "实验性功能；尚未验证游戏启动与实际效果。",
        "Lua controls are off, but the aging setting could not be restored." => "其他持续功能已关闭，但暂时无法恢复衰老设置。",
        _ => null
    };
}
