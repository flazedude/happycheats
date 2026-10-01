using HappyCheats.Core;

namespace HappyCheats.App;

internal static class Civ6Localization
{
    internal static readonly Dictionary<string, (string Name, string Description)> Features = new()
    {
        ["civ6_movement"] = ("无限移动", "保持此页面连接时，补满己方单位的移动力。不增加攻击次数，也不恢复已消耗的单位。"),
        ["civ6_units"] = ("快速生产单位", "保持连接时完成己方城市当前正在生产的单位。请先选择生产项目。"),
        ["civ6_buildings"] = ("快速建造建筑与奇观", "保持连接时完成己方城市当前正在建造的建筑或奇观。"),
        ["civ6_districts"] = ("快速建造特色建筑", "对应区域建造：保持连接时完成己方城市当前正在建造的区域。"),
        ["civ6_projects"] = ("快速完成项目", "保持连接时完成己方城市当前项目；可重复项目可能反复完成。"),
        ["civ6_research"] = ("快速研究科技", "保持连接时完成当前选择的科技。请先选择研究目标。"),
        ["civ6_civics"] = ("快速研究市政", "保持连接时完成当前选择的市政。请先选择市政目标。"),
        ["civ6_damage"] = ("修改单位已受伤害", "设置选中己方单位已受伤害；0 表示治疗。此功能不会杀死单位。"),
        ["civ6_moves"] = ("修改剩余移动力", "设置当前选中己方单位的剩余移动点数。"),
        ["civ6_can_level"] = ("修改单位晋升资格", "1 通过补足经验解锁晋升；0 尝试移除已储存的晋升。会核对结果；不支持的更改将报告失败。不是直接修改等级。"),
        ["civ6_xp"] = ("修改单位经验", "设置所选己方单位的经验值；仍受游戏经验和晋升规则影响。"),
        ["civ6_level"] = ("修改单位等级", "暂不可用：Lua 接口没有已验证的原始等级设置方法。可使用经验与晋升功能。"),
        ["civ6_gold"] = ("修改金币", "将己方当前金币设置为输入数值。"),
        ["civ6_faith"] = ("修改信仰", "将己方当前信仰设置为输入数值。"),
        ["civ6_influence"] = ("修改影响力", "暂不可用：Lua 接口没有已验证的原始影响力点数设置方法。使者数量是另一项数值。"),
        ["civ6_favor"] = ("修改外交支持", "设置外交支持。需要风云变幻规则及其外交接口。"),
        ["civ6_population"] = ("修改人口", "设置当前选中己方城市的人口。请先选中城市。"),
        ["civ6_amenities"] = ("修改宜居度加成", "暂不可用：Lua 接口没有已验证的原始宜居度加成设置方法。"),
        ["civ6_housing"] = ("修改住房加成", "暂不可用：Lua 接口没有已验证的原始住房加成设置方法。")
    };

    public static string? FeatureName(FeatureDefinition feature) => Localization.Chinese && Features.TryGetValue(feature.Id, out var text) ? text.Name : null;
    public static string? FeatureDescription(FeatureDefinition feature) => Localization.Chinese && Features.TryGetValue(feature.Id, out var text) ? text.Description : null;
    public const string ChineseDescription = "实验性单人修改器，使用本机 FireTuner 接口。安装 / 更新会启用 Tuner，并禁用成就。保持此页面打开可使用持续功能和游戏内快捷键：F1–F11、Shift+F1–F4；F12 关闭全部。数值快捷键应用输入框中的值。已实现 15 项功能，另有 4 项原始数值编辑暂不可用；尚待游戏内验证。";

    public static string? ValueLabel(string key)
    {
        if (Features.TryGetValue(key, out var feature)) return Localization.Chinese ? feature.Name.Replace("修改", "") : key switch {
            "civ6_damage" => "Unit damage", "civ6_moves" => "Unit moves", "civ6_can_level" => "Promotion available", "civ6_xp" => "Unit XP",
            "civ6_level" => "Unit level", "civ6_gold" => "Gold", "civ6_faith" => "Faith", "civ6_influence" => "Influence", "civ6_favor" => "Diplomatic favor",
            "civ6_population" => "Population", "civ6_amenities" => "Amenities bonus", "civ6_housing" => "Housing bonus", _ => key };
        return key switch {
            "selected_unit_id" => Localization.Choose("Selected unit", "选中单位"), "selected_city_id" => Localization.Choose("Selected city", "选中城市"),
            "player_id" => Localization.Choose("Player", "玩家"), "turn" => Localization.Choose("Turn", "回合"), _ => null
        };
    }

    public static string? Translate(string text) => text switch
    {
        "Civilization VI was not found. Choose its installation root containing Base/Binaries, or install it through Steam." => "未找到文明 VI。请选择包含 Base/Binaries 的安装根目录，或先通过 Steam 安装游戏。",
        "Install / update enables Civilization VI's Tuner. Achievements are disabled while Tuner is enabled." => "安装 / 更新会启用文明 VI 的 Tuner。启用期间无法获得成就。",
        "Tuner setup ready. Load a single-player game; keep FireTuner and other Tuner clients closed. Gameplay is unverified." => "Tuner 设置已就绪。请载入单人游戏，并关闭 FireTuner 等其他连接工具。尚待游戏内验证。",
        "Tuner is enabled. HappyCheats can connect without a setup receipt. Load single-player and close other Tuner clients." => "Tuner 已启用，无需 HappyCheats 安装记录即可连接。请载入单人游戏，并关闭其他 Tuner 连接工具。",
        "No Civilization VI Tuner connection. Close the game, choose Install / update, restart it and load single-player. Close other Tuner clients." => "无法连接文明 VI 的 Tuner。请关闭游戏，点击安装 / 更新，再重启并载入单人游戏。请关闭其他 Tuner 连接工具。",
        "Ready: local single-player game." => "已连接本机单人游戏，可以使用功能。",
        "Waiting for the local player's active turn." => "等待本机玩家的回合。",
        "Single-player games only." => "仅支持单人游戏。",
        "Single-player session checks are unavailable in this game context." => "当前游戏环境无法确认单人模式。",
        "Load a single-player game and select your local player." => "请载入单人游戏并选择本机玩家。",
        "The local player changed; refresh the game selection." => "本机玩家已改变，请刷新游戏选择。",
        "Refresh the verified local player from the in-game UI." => "请返回游戏界面并刷新本机玩家信息。",
        "A living local human player is required." => "需要仍在游戏中的本机真人玩家。",
        "Start Civilization VI and load a single-player game to connect." => "请启动文明 VI 并载入单人游戏，以连接修改器。",
        "Close Civilization VI before changing Tuner settings." => "修改 Tuner 设置前，请先关闭文明 VI。",
        "Close Civilization VI, choose Install / update, then restart the game to enable Tuner." => "请关闭文明 VI，点击安装 / 更新，再重启游戏以启用 Tuner。",
        "This Civilization VI control is unavailable. Check its description and select your own unit or city as needed." => "此文明 VI 功能当前不可用。请阅读说明，并按需选中己方单位或城市。",
        "Civilization VI game session changed. Reconnect before using controls." => "文明 VI 游戏会话已改变。请重新连接后再使用功能。",
        "Load a single-player game and reconnect before using this control." => "请载入单人游戏并重新连接后，再使用此功能。",
        _ => null
    };
}
