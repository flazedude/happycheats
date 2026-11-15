using HappyCheats.Core;
using System.Text.RegularExpressions;

namespace HappyCheats.App;

/// <summary>Presentation-only translations; feature IDs and game messages stay unchanged.</summary>
internal static class NomadDriveLocalization
{
    private static readonly Dictionary<string, (string Name, string Description)> Features = new(StringComparer.Ordinal)
    {
        ["heal"] = ("治疗伤害", "恢复伤害造成的生命损失，不能复活已死亡的角色。"),
        ["refill_food"] = ("补满食物", "将营养值补满至当前上限。"),
        ["refill_water"] = ("补满水分", "将水分补满至当前上限。"),
        ["refill_energy"] = ("补满精力", "将精力补满至当前上限。"),
        ["cure"] = ("治疗中毒", "使用游戏的药物效果停止毒素累积，并清除当前中毒值。"),
        ["refuel"] = ("为最近车辆加油", "补满 15 米内车辆现有的汽油箱。请先加入汽油；不会改变液体类型。"),
        ["repair_engine"] = ("维修最近车辆的引擎", "维修 15 米内最近车辆已安装的引擎，不会添加缺失的部件。"),
        ["unlimited_food"] = ("无限食物", "单人游戏的存活角色载入后，持续补满营养值。"),
        ["unlimited_water"] = ("无限水分", "单人游戏的存活角色载入后，持续补满水分。"),
        ["unlimited_energy"] = ("无限精力", "单人游戏的存活角色载入后，持续补满精力。"),
        ["rapid_heal"] = ("快速治疗", "每帧恢复伤害造成的生命损失。无法阻止瞬间致死伤害，也不能复活。")
    };

    public static string? FeatureName(FeatureDefinition feature) => Features.TryGetValue(feature.Id, out var text) ? text.Name : null;
    public static string? FeatureDescription(FeatureDefinition feature) => Features.TryGetValue(feature.Id, out var text) ? text.Description : null;
    public static string? ValueLabel(string key) => key switch {
        "heal" => "生命", "refill_food" => "食物", "refill_water" => "水分", "refill_energy" => "精力", "cure" => "中毒值", _ => null
    };

    private static readonly Dictionary<string, string> Messages = new(StringComparer.Ordinal)
    {
        ["Solo character loaded. Trainer ready."] = "单人角色已载入，修改器已就绪。",
        ["Load a living character in a solo hosted game. Multiplayer sessions are unsupported."] = "请在单人主机游戏中载入存活角色。不支持多人会话。",
        ["Load a living character in a solo hosted game before using trainer actions."] = "使用修改器功能前，请在单人主机游戏中载入存活角色。",
        ["Compatible Steam build · ready to install"] = "Steam 版本兼容 · 可以安装",
        ["Nomad Drive trainer update required. Close the game before installing."] = "Nomad Drive 修改器需要更新。请关闭游戏后再安装。",
        ["Close Nomad Drive before changing trainer files."] = "更改修改器文件前，请先关闭 Nomad Drive。",
        ["Unsupported Nomad Drive game build."] = "不支持此 Nomad Drive 游戏版本。",
        ["This game build needs inspection before the trainer can be installed."] = "此游戏版本需要检查后才能安装修改器。",
        ["Trainer session does not match this game build."] = "修改器会话与当前游戏版本不匹配。",
        ["Move within 15 metres of an initialized vehicle."] = "请靠近已初始化的车辆，距离需在 15 米以内。",
        ["Two vehicles are equally close. Move nearer the intended vehicle and retry."] = "两辆车的距离过于接近。请靠近目标车辆后重试。",
        ["Install a fuel tank on the nearest vehicle first."] = "请先为最近的车辆安装油箱。",
        ["Put the correct gasoline in the tank first; its liquid type is preserved."] = "请先向油箱加入正确的汽油；修改器不会改变液体类型。",
        ["Install an engine on the nearest vehicle first."] = "请先为最近的车辆安装引擎。",
        ["Unknown Nomad Drive action."] = "未知的 Nomad Drive 操作。",
        ["Toggle state is missing."] = "缺少开关状态。",
        ["Unsupported command schema."] = "不支持此指令格式。",
        ["request_id must be a GUID."] = "request_id 必须是有效的 GUID。",
        ["Command filename and request_id differ."] = "指令文件名与 request_id 不一致。",
        ["Command belongs to another game session."] = "此指令属于另一个游戏会话。",
        ["Command has expired or is dated in the future."] = "指令已过期，或其时间晚于当前时间。",
        ["Unknown trainer action."] = "未知的修改器操作。",
        ["Toggle commands require enabled=true or false."] = "开关指令必须指定 enabled=true 或 false。",
        ["Command exceeds the size limit."] = "指令超出大小限制。",
        ["Trainer mailbox links are unsupported."] = "修改器通信文件夹不支持符号链接。",
        ["This game build has not been inspected. Trainer actions are disabled."] = "此游戏版本尚未检查，无法执行修改器操作。",
        ["The Nomad Drive package does not match the supported game build."] = "Nomad Drive 安装包与支持的游戏版本不匹配。",
        ["Invalid Nomad Drive package manifest."] = "Nomad Drive 安装包清单无效。",
        ["Verifying installed Nomad Drive trainer files..."] = "正在校验已安装的 Nomad Drive 修改器文件…",
        ["Nomad Drive trainer installed. Launch the game to connect."] = "Nomad Drive 修改器已安装。启动游戏后即可连接。"
    };

    public static string? Message(string source)
    {
        if (Messages.TryGetValue(source, out var text)) return text;
        foreach (var (id, feature) in Features)
        {
            var wireName = id.Replace('_', ' ');
            if (source == "Applied " + wireName + ".") return feature.Name + "：已应用。";
            if (source == wireName + " enabled.") return feature.Name + "：已开启。";
            if (source == wireName + " disabled.") return feature.Name + "：已关闭。";
        }
        foreach (var (english, chinese) in new[] {
            ("Nomad Drive trainer package unavailable: ", "Nomad Drive 修改器安装包不可用："),
            ("The Nomad Drive package is missing a required runtime file: ", "Nomad Drive 安装包缺少必需的运行文件："),
            ("Nomad Drive installation verification failed: ", "Nomad Drive 安装校验失败：")
        }) if (source.StartsWith(english, StringComparison.Ordinal))
            return chinese + Localization.Message(source[english.Length..], "nomad-drive")
                .Replace(". Close the game and retry Update / Install.", "。请关闭游戏后重试“更新 / 安装”。", StringComparison.Ordinal);
        var loader = Regex.Match(source, @"^Nomad Drive loader failed before the trainer started: (.*?) Close the game and install the latest Nomad Drive trainer update\. Startup log: (.+)$", RegexOptions.Singleline);
        if (loader.Success) return $"Nomad Drive 加载器未能启动修改器：{loader.Groups[1].Value}。请关闭游戏并安装最新的修改器更新。启动日志：{loader.Groups[2].Value}";
        var exception = Regex.Match(source, @"^([A-Za-z0-9_.]+Exception: )(.*)$", RegexOptions.Singleline);
        if (exception.Success) return exception.Groups[1].Value + Localization.Message(exception.Groups[2].Value, "nomad-drive");
        return null;
    }
}
