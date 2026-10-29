namespace HappyCheats.App;

/// <summary>Presentation-only translations; diagnostic details remain unchanged.</summary>
internal static class PhantomBladeLocalization
{
    public static string? ValueLabel(string key) => key switch {
        "pbz_health" => Localization.Choose("Health", "生命"),
        "pbz_health_max" => Localization.Choose("Max health", "生命上限"),
        "pbz_stamina" => Localization.Choose("Sha-Chi", "杀气"),
        "pbz_stamina_max" => Localization.Choose("Max Sha-Chi", "杀气上限"),
        "pbz_damage" => Localization.Choose("Attack multiplier", "攻击力倍率"),
        "pbz_attack" => Localization.Choose("Attack power", "攻击力"),
        _ => null
    };

    public static string? Translate(string source)
    {
        foreach (var (english, chinese) in new[] {
            ("Phantom Blade Zero trainer paused: ", "影之刃零修改器已暂停："),
            ("Phantom Blade Zero command failed: ", "影之刃零指令失败："),
            ("Phantom Blade Zero trainer files were modified and preserved: ", "影之刃零修改器文件已修改，已予保留："),
            ("The Phantom Blade Zero package is missing a required runtime file: ", "影之刃零安装包缺少必需的运行文件：")
        }) if (source.StartsWith(english, StringComparison.Ordinal)) return chinese + source[english.Length..];
        var build = System.Text.RegularExpressions.Regex.Match(source, @"^Phantom Blade Zero Steam build ([0-9]+) detected\. Experimental trainer; gameplay validation is pending\.$");
        if (build.Success) return $"已检测到影之刃零 Steam 构建 {build.Groups[1].Value}。实验性修改器，尚待实际游玩验证。";
        return source switch {
            "This Phantom Blade Zero build needs a compatibility check." => "此影之刃零版本需要兼容性检查。",
            "Experimental Phantom Blade Zero trainer installed. Gameplay validation is pending." => "已安装影之刃零实验性修改器，尚待实际游玩验证。",
            "Phantom Blade Zero trainer update required. Installed files differ from the current module." => "影之刃零修改器需要更新；已安装文件与当前模块不同。",
            "Close Phantom Blade Zero before changing trainer files." => "更改修改器文件前，请先关闭影之刃零。",
            "Unsupported Phantom Blade Zero executable. Game files were preserved." => "不支持此影之刃零程序；游戏文件已保留。",
            "Install Phantom Blade Zero through Steam first." => "请先通过 Steam 安装影之刃零。",
            "Launch Phantom Blade Zero to connect." => "请启动影之刃零以连接修改器。",
            "Waiting for Phantom Blade Zero trainer status." => "正在等待影之刃零修改器状态。",
            "Invalid Phantom Blade Zero trainer status." => "影之刃零修改器状态无效。",
            "Waiting for a fresh Phantom Blade Zero trainer heartbeat." => "正在等待影之刃零修改器更新连接状态。",
            "Restart Phantom Blade Zero to load the current trainer module." => "请重启影之刃零以加载当前修改器模块。",
            "Phantom Blade Zero trainer connected." => "影之刃零修改器已连接。",
            "Load a single-player save before using Phantom Blade Zero controls." => "使用影之刃零功能前，请先载入单人存档。",
            "Phantom Blade Zero session changed. Reconnect before trying again." => "影之刃零游戏会话已改变，请重新连接后再试。",
            "Unknown Phantom Blade Zero trainer control." => "未知的影之刃零修改器功能。",
            "Invalid Phantom Blade Zero command identity." => "影之刃零指令标识无效。",
            "This Phantom Blade Zero control is unavailable in the current game session." or "This Phantom Blade Zero control is unavailable." => "当前游戏会话无法使用此影之刃零功能。",
            "This Phantom Blade Zero control does not accept an amount." => "此影之刃零功能不接受数值。",
            "Phantom Blade Zero rejected this control." => "影之刃零拒绝了此操作。",
            "The Phantom Blade Zero trainer command directory is unavailable." => "影之刃零修改器指令文件夹不可用。",
            "Invalid Phantom Blade Zero package or ownership record." => "影之刃零安装包或文件归属记录无效。",
            "Invalid Phantom Blade Zero package manifest." => "影之刃零安装包清单无效。",
            "Invalid Phantom Blade Zero ownership receipt." => "影之刃零文件归属记录无效。",
            "Phantom Blade Zero installation failed; some files need recovery." => "影之刃零安装失败；部分文件需要恢复。",
            "Phantom Blade Zero trainer removed. The shared UE4SS loader was preserved for other mods." => "影之刃零修改器已移除；其他模组使用的共享 UE4SS 加载器已保留。",
            "Phantom Blade Zero trainer removed. Unowned files were preserved." => "影之刃零修改器已移除；不属于本修改器的文件已保留。",
            "Choose a whole-number damage multiplier from 1 to 20." => "请选择 1 至 20 之间的整数伤害倍率。",
            "Phantom Blade Zero trainer starting; waiting for the game thread." => "影之刃零修改器正在启动，等待游戏线程就绪。",
            "All Phantom Blade Zero controls reset." => "所有影之刃零功能均已重置。",
            "Attack restoration is pending; keep HappyCheats connected." => "正在等待恢复攻击力，请保持 HappyCheats 连接。",
            "Load a single-player save to connect Phantom Blade Zero." => "请载入单人存档以连接影之刃零修改器。",
            "Phantom Blade Zero reflected attribute APIs are unavailable." => "影之刃零属性接口不可用。",
            "Phantom Blade Zero owned attribute set is unavailable." => "无法确认影之刃零玩家的属性归属，当前不可用。",
            "The player's owned attribute set is unavailable." => "无法确认玩家的属性归属，当前不可用。",
            "Waiting for a living Phantom Blade Zero player." => "正在等待存活的影之刃零玩家角色。",
            "Experimental Phantom Blade Zero trainer connected. Gameplay effects are unverified." => "影之刃零实验性修改器已连接；游戏内效果尚未验证。",
            "Phantom Blade Zero control disabled." => "影之刃零功能已关闭。",
            "Phantom Blade Zero control enabled." => "影之刃零功能已开启。",
            "Attack multiplier reset." => "攻击力倍率已重置。",
            "Attack power multiplier is already applied." => "此攻击力倍率已应用。",
            "Phantom Blade Zero session changed; reconnect before trying again." => "影之刃零游戏会话已改变，请重新连接后再试。",
            "This Phantom Blade Zero control is unavailable in the current session." => "当前会话无法使用此影之刃零功能。",
            "Resource refill could not be verified." => "无法确认资源恢复是否生效。",
            "Unknown Phantom Blade Zero control." => "未知的影之刃零功能。",
            "Choose a whole-number attack multiplier from 1 to 20." => "请选择 1 至 20 之间的整数攻击力倍率。",
            "Previous attack restoration is pending." => "上一次攻击力修改仍在等待恢复。",
            "Attack modifiers are active or unavailable; multiplier was not applied." => "攻击力受到其他效果影响或不可用，未应用倍率。",
            "The requested attack value exceeds the supported range." => "请求的攻击力超出支持范围。",
            "Attack multiplier could not be verified; restoration will retry while connected." => "无法确认攻击力倍率是否生效；保持连接时将重试恢复原值。",
            "Attack power multiplier applied. Final damage still follows the game's combat rules." => "攻击力倍率已应用，最终伤害仍遵循游戏战斗规则。",
            _ => null
        };
    }
}
