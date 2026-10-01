using HappyCheats.Core;

namespace HappyCheats.App;

internal static class Civ7Localization
{
    private static readonly Dictionary<string, (string Name, string Description)> Features = new()
    {
        ["civ7_movement"] = ("无限移动", "保持此页面连接时，补满己方单位的移动力。不增加攻击次数，也不恢复已消耗的单位。"),
        ["civ7_units"] = ("快速生产单位", "保持连接时完成己方城市当前正在生产的单位。请先选择生产项目。"),
        ["civ7_buildings"] = ("快速建造建筑与奇观", "保持连接时完成己方城市当前正在建造的建筑或奇观。"),
        ["civ7_projects"] = ("快速完成项目", "保持连接时完成己方城市当前项目；可重复项目可能反复完成。"),
        ["civ7_research"] = ("快速研究科技", "保持连接时解锁当前选择的科技节点。请先选择研究目标；精通仍遵循游戏规则。"),
        ["civ7_civics"] = ("快速研究市政", "保持连接时解锁当前选择的市政节点。请先选择市政目标；精通仍遵循游戏规则。"),
        ["civ7_damage"] = ("修改单位已受伤害", "设置选中己方单位已受伤害；0 表示治疗。此功能不会杀死单位。"),
        ["civ7_restore_moves"] = ("恢复选中单位移动力", "一次性补满当前选中己方单位的移动力。不增加攻击次数。"),
        ["civ7_xp"] = ("增加单位经验", "为选中的己方单位或指挥官增加输入数量的经验。晋升规则与游戏上限仍然适用。"),
        ["civ7_gold"] = ("修改金币", "将己方当前金币设置为输入数值。"),
        ["civ7_influence"] = ("修改影响力", "将己方当前外交影响力设置为输入数值。"),
        ["civ7_population"] = ("增加乡村人口", "为当前选中的己方聚落增加输入数量的乡村人口。输入的是增加数量，并非总人口。"),
        ["civ7_attributes"] = ("增加通配属性点", "为本机玩家增加输入数量的通配属性点，供领袖属性树使用。"),
        ["civ7_growth"] = ("完成食物队列", "完成选中己方聚落当前使用食物推进的项目，可能是人口成长或通过食物训练的单位。"),
        ["civ7_golden_age"] = ("开始庆典", "使用游戏的黄金时代接口开启庆典。请在游戏中完成庆典选项。")
    };

    public const string ChineseDescription = "实验性 Steam 单人修改器，使用本机 Tuner 接口，提供 15 项单位、生产、科技、市政、金币、影响力、人口、属性点与庆典功能。安装 / 更新会启用 Tuner；对成就的影响尚未验证。保持此页面打开可使用持续功能和游戏内快捷键：F1–F11、Shift+F1–F4；F12 关闭全部。数值快捷键应用输入框中的值。尚待游戏内验证。";

    public static string? FeatureName(FeatureDefinition feature) => Localization.Chinese && Features.TryGetValue(feature.Id, out var text) ? text.Name : null;
    public static string? FeatureDescription(FeatureDefinition feature) => Localization.Chinese && Features.TryGetValue(feature.Id, out var text) ? text.Description : null;

    public static string? ValueLabel(string key) => key switch
    {
        "civ7_damage" => Localization.Choose("Unit damage", "单位已受伤害"),
        "civ7_xp" => Localization.Choose("Unit XP", "单位经验"),
        "civ7_gold" => Localization.Choose("Gold", "金币"),
        "civ7_influence" => Localization.Choose("Influence", "影响力"),
        "civ7_population" => Localization.Choose("Settlement population", "聚落人口"),
        "civ7_attributes" => Localization.Choose("Wildcard attribute points", "通配属性点"),
        "civ7_moves" => Localization.Choose("Unit moves", "单位移动力"),
        "civ7_golden_age" => Localization.Choose("Celebration turns left", "庆典剩余回合"),
        "age" => Localization.Choose("Age", "时代"),
        _ => null
    };

    public static string? Translate(string text) => text switch
    {
        "Civilization VII was not found. Choose its installation root containing Base/Binaries, or install it through Steam." => "未找到文明 VII。请选择包含 Base/Binaries 的安装根目录，或先通过 Steam 安装游戏。",
        "Install / update enables Civilization VII's Tuner. Achievement behavior is unverified." => "安装 / 更新会启用文明 VII 的 Tuner。对成就的影响尚未验证。",
        "Tuner is enabled. Load a single-player game and close other Tuner clients." => "Tuner 已启用。请载入单人游戏，并关闭其他 Tuner 连接工具。",
        "Ready: local single-player game. Gameplay compatibility is unverified." => "已连接本机单人游戏。实际游戏兼容性尚待验证。",
        "Load a single-player game to connect." => "请载入单人游戏，以连接修改器。",
        "A verified single-player game is required; multiplayer and hotseat are unsupported." => "需要确认当前为单人游戏；不支持多人游戏和热座模式。",
        "No local player is available." => "当前没有可用的本机玩家。",
        "Game turn or Age is unavailable." => "无法读取当前回合或时代。",
        "No Civilization VII Tuner connection. Close the game, choose Install / update, restart it and load single-player. Close other Tuner clients." => "无法连接文明 VII 的 Tuner。请关闭游戏，点击安装 / 更新，再重启并载入单人游戏。请关闭其他 Tuner 连接工具。",
        "Start Civilization VII and load a single-player game to connect." => "请启动文明 VII 并载入单人游戏，以连接修改器。",
        "Close Civilization VII before changing Tuner settings." => "修改 Tuner 设置前，请先关闭文明 VII。",
        "Close all Civilization VII installations before changing Tuner settings." => "修改 Tuner 设置前，请先关闭所有文明 VII 游戏进程。",
        "Close Civilization VII, choose Install / update, then restart the game to enable Tuner." => "请关闭文明 VII，点击安装 / 更新，再重启游戏以启用 Tuner。",
        "Civilization VII game session changed. Reconnect before using controls." => "文明 VII 游戏会话已改变。请重新连接后再使用功能。",
        "Launch this Civilization VII installation from its own game launcher, then return to HappyCheats." => "请通过此文明 VII 安装目录自带的启动器启动游戏，再返回 HappyCheats。",
        "No matching Civilization VII acknowledgement. The action was not retried." => "未收到匹配的文明 VII 确认消息。为避免重复操作，未重试。",
        "Civilization VII AppOptions.txt was not found. Launch the game once yourself, then close it before installing the trainer." => "未找到文明 VII 的 AppOptions.txt。请自行启动游戏一次，再关闭游戏并安装修改器。",
        "Civilization VII trainer is already enabled." => "文明 VII 修改器已启用。",
        "Civilization VII trainer enabled. Launch a single-player game to connect." => "文明 VII 修改器已启用。请载入单人游戏，以连接修改器。",
        "Civilization VII trainer removed; the original EnableTuner setting was restored. The configuration backup was kept." => "文明 VII 修改器已移除，并已恢复原来的 EnableTuner 设置。配置备份已保留。",
        "Civilization VII runtime checksum failed. Reinstall HappyCheats before enabling Tuner." => "文明 VII 修改器运行文件校验失败。请重新安装 HappyCheats，再启用 Tuner。",
        "This Civilization VII control is unavailable. Check its description and select your own unit or city as needed." => "此文明 VII 功能当前不可用。请阅读说明，并按需选中己方单位或城市。",
        _ => null
    };
}
