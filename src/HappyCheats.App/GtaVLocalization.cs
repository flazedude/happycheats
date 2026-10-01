using System.Text.RegularExpressions;

namespace HappyCheats.App;

internal static class GtaVLocalization
{
    private static readonly Dictionary<string, string> Messages = new(StringComparer.Ordinal)
    {
        ["GTA Online is unsupported. Story Mode controls are disabled."] = "不支持 GTA 在线模式，故事模式功能已禁用。",
        ["GTA Online is unsupported, including invite-only sessions."] = "不支持 GTA 在线模式，包括邀请战局。",
        ["GTA Online is locked out, including invite-only sessions."] = "已禁用 GTA 在线模式功能，包括邀请战局。",
        ["GTA Online detected. Trainer locked until the game restarts, including invite-only sessions."] = "检测到 GTA 在线模式，修改器已锁定至游戏重启；邀请战局也不受支持。",
        ["GTA V Enhanced Story Mode trainer installed. Gameplay validation is pending."] = "已安装 GTA V 增强版故事模式修改器。尚待游戏内验证。",
        ["GTA V Enhanced trainer update required. Close the game before installing."] = "GTA V 增强版修改器需要更新。安装前请先关闭游戏。",
        ["Ready to install the GTA V Enhanced Story Mode trainer. GTA Online is unsupported."] = "可以安装 GTA V 增强版故事模式修改器。不支持在线模式。",
        ["Install the official Script Hook V and its ASI loader (ScriptHookV.dll and dinput8.dll) for GTA V Enhanced first. Existing loaders are never replaced."] = "请先安装适用于 GTA V 增强版的官方 Script Hook V 及 ASI 加载器（ScriptHookV.dll、dinput8.dll）。现有加载器不会被替换。",
        ["Close GTA V Enhanced before changing trainer files."] = "修改文件前，请先关闭 GTA V 增强版。",
        ["The installed Script Hook V does not match the supported GTA V Enhanced 1.0.1158.13 runtime. Install the matching official Script Hook V release; existing files were preserved."] = "已安装的 Script Hook V 与受支持的 GTA V 增强版 1.0.1158.13 运行库不匹配。请安装对应的官方 Script Hook V 版本；已保留现有文件。",
        ["Choose an installed GTA V Enhanced game folder first."] = "请先选择已安装的 GTA V 增强版文件夹。",
        ["Install GTA V Enhanced through Steam first."] = "请先通过 Steam 安装 GTA V 增强版。",
        ["The installed GTA V Enhanced trainer is incompatible. Remove it before launching this game version."] = "已安装的 GTA V 增强版修改器不兼容。启动此游戏版本前，请先移除修改器。",
        ["GTA V Enhanced version or Script Hook V prerequisites are incompatible. Story Mode controls are disabled."] = "GTA V 增强版版本或 Script Hook V 依赖不兼容。故事模式功能已禁用。",
        ["Start GTA V Enhanced Story Mode to connect."] = "请启动 GTA V 增强版的故事模式以连接修改器。",
        ["Waiting for GTA V Enhanced Story Mode trainer status."] = "正在等待 GTA V 增强版故事模式修改器状态。",
        ["Invalid GTA V Enhanced trainer status."] = "GTA V 增强版修改器状态无效。",
        ["Waiting for a fresh GTA V Enhanced trainer heartbeat."] = "正在等待 GTA V 增强版修改器更新连接状态。",
        ["GTA V Enhanced trainer identity or heartbeat is invalid. Restart Story Mode."] = "GTA V 增强版修改器标识或连接状态无效。请重启故事模式。",
        ["GTA V Enhanced Story Mode trainer connected."] = "GTA V 增强版故事模式修改器已连接。",
        ["GTA V Enhanced session changed. Reconnect before trying again."] = "GTA V 增强版游戏连接已改变。请重新连接后再试。",
        ["This GTA V Enhanced control is unavailable in the current Story Mode session."] = "当前故事模式场景中无法使用此功能。",
        ["GTA V Enhanced session changed while waiting for the command."] = "等待指令确认时，GTA V 增强版游戏连接已改变。",
        ["GTA V Enhanced rejected this control."] = "GTA V 增强版未接受此操作。",
        ["The GTA V Enhanced trainer command directory is unavailable."] = "GTA V 增强版修改器指令目录不可用。",
        ["No GTA V Enhanced acknowledgement received. This action may have applied; it was not retried."] = "未收到 GTA V 增强版操作确认。操作可能已生效，未自动重试。",
        ["Load a living Story Mode character and close the pause menu."] = "请载入存活的故事模式角色并关闭暂停菜单。",
        ["Waiting for a loaded Story Mode character."] = "正在等待载入故事模式角色。",
        ["All HappyCheats toggles disabled."] = "所有 HappyCheats 持续生效功能均已关闭。",
        ["Enter a vehicle first."] = "请先进入载具。",
        ["This feature is unavailable."] = "此功能当前不可用。",
        ["Toggle enabled."] = "持续生效功能已开启。",
        ["Toggle disabled."] = "持续生效功能已关闭。",
        ["Story Mode action applied."] = "故事模式操作已应用。",
        ["The command belongs to another game session."] = "指令属于另一个游戏连接。",
        ["The command expired or has a future timestamp."] = "指令已过期或时间戳无效。",
        ["Duplicate request rejected."] = "已拒绝重复指令。",
        ["Malformed command rejected."] = "已拒绝格式无效的指令。",
        ["Actions do not accept an enabled value."] = "单次操作不接受开关状态。",
        ["Missing action."] = "缺少指令操作名称。",
        ["Missing command timestamp."] = "缺少指令时间戳。",
        ["No commands received."] = "尚未收到指令。",
        ["Toggle requires a boolean enabled value."] = "持续生效功能需要有效的布尔开关状态。",
        ["Toggles already disabled while Story Mode is unavailable."] = "故事模式不可用时，持续生效功能已关闭。",
        ["Too many recent requests."] = "近期指令请求过多。",
        ["Unexpected command fields."] = "指令包含非预期字段。",
        ["Unknown action."] = "未知操作。",
        ["Unsupported command schema."] = "不支持此指令格式版本。",
        ["Waiting to restore the previous player state."] = "正在等待恢复先前的玩家状态。",
        ["The GTA V Enhanced executable is missing."] = "未找到 GTA V 增强版游戏程序。",
        ["GTA V Enhanced changed during executable verification. Scan the game again."] = "校验期间 GTA V 增强版程序发生变化，请重新扫描游戏。",
        ["GTA V Enhanced 1.0.1158.13 detected. Install / update obtains the verified Script Hook V and ASI loader from the author. Native startup and gameplay are unverified."] = "检测到 GTA V 增强版 1.0.1158.13。“安装 / 更新”会从作者网站获取并校验 Script Hook V 和 ASI 加载器。尚未验证游戏内加载与功能。",
        ["Pinned GTA V Enhanced 1.0.1158.16 detected. Install / update obtains the verified ASI loader for the included experimental Story Mode backend. Native startup and gameplay are unverified."] = "检测到已核对的 GTA V 增强版 1.0.1158.16。“安装 / 更新”会为内置实验性故事模式运行库获取并校验 ASI 加载器。尚未验证游戏内加载与功能。",
        ["Experimental GTA V Enhanced Story Mode trainer installed. Native startup and gameplay are unverified."] = "已安装实验性 GTA V 增强版故事模式修改器。尚未验证游戏内加载与功能。",
        ["Ready to install the experimental GTA V Enhanced Story Mode trainer. Native startup and gameplay are unverified. GTA Online is unsupported."] = "可以安装实验性 GTA V 增强版故事模式修改器。尚未验证游戏内加载与功能，不支持在线模式。",
        ["This GTA V Enhanced 1.0.1158.16 executable does not match the reviewed Steam build. The experimental backend requires its pinned executable hash; game files were preserved."] = "此 GTA V 增强版 1.0.1158.16 程序与已核对的 Steam 版本不一致，未通过实验性运行库的程序校验。已保留游戏文件。",
        ["The verified GTA V runtime changed during setup; trainer installation was stopped."] = "配置期间已校验的 GTA V 运行库发生变化，修改器安装已停止。",
        ["GTA V Enhanced runtime setup is required. Close the game and choose Install / update."] = "需要配置 GTA V 增强版运行库。请关闭游戏，然后选择“安装 / 更新”。",
        ["GTA V Enhanced 1.0.1158.13 requires the verified Script Hook V and ASI loader. Choose Install / update to set them up."] = "GTA V 增强版 1.0.1158.13 需要经校验的 Script Hook V 和 ASI 加载器，请选择“安装 / 更新”进行配置。",
        ["The GTA V Enhanced Story Mode backend requires the verified ASI loader. Choose Install / update to set it up."] = "GTA V 增强版故事模式运行库需要经校验的 ASI 加载器，请选择“安装 / 更新”进行配置。",
        ["Invalid GTA V Enhanced package or ownership record."] = "GTA V 增强版安装包或文件归属记录无效。",
        ["Unsupported GTA V Enhanced package identity."] = "不支持此 GTA V 增强版安装包标识。",
        ["Missing GTA V Enhanced package files."] = "缺少 GTA V 增强版安装包文件列表。",
        ["The GTA V Enhanced package is missing a required runtime file."] = "GTA V 增强版安装包缺少必要的运行文件。",
        ["Invalid GTA V Enhanced ownership receipt."] = "GTA V 增强版文件归属记录无效。",
        ["The GTA V Enhanced ownership receipt path is a directory."] = "GTA V 增强版文件归属记录的位置被文件夹占用。",
        ["A GTA V Enhanced trainer directory path is a file."] = "GTA V 增强版修改器文件夹的位置被文件占用。",
        ["A GTA V Enhanced trainer file path is a directory."] = "GTA V 增强版修改器文件的位置被文件夹占用。",
        ["GTA V Enhanced payload changed during installation."] = "安装期间 GTA V 增强版安装包文件发生变化。",
        ["No GTA V Enhanced ownership receipt was found. Existing files were preserved."] = "未找到 GTA V 增强版文件归属记录，已保留现有文件。",
        ["Unexpected GTA V Enhanced mailbox size; preserved for inspection."] = "GTA V 增强版通信文件大小异常，已保留以供检查。",
        ["GTA V Enhanced trainer removed. Unowned files were preserved."] = "已移除 GTA V 增强版修改器，已保留不属于本修改器的文件。",
        ["A GTA V Enhanced file changed before installation commit; it was preserved."] = "完成安装前 GTA V 增强版文件发生变化，已保留该文件。",
        ["GTA V Enhanced operation failed; review recovery details."] = "GTA V 增强版操作失败，请查看恢复详情。",
        ["Invalid GTA V Enhanced command identity or time."] = "GTA V 增强版指令标识或时间无效。",
        ["Unknown GTA V Enhanced trainer control."] = "未知的 GTA V 增强版修改器功能。",
        ["Only toggle controls require an explicit enabled state."] = "只有持续生效功能需要明确指定开关状态。",
        ["The GTA V Enhanced game directory does not exist."] = "GTA V 增强版游戏文件夹不存在。",
        ["Obtaining the verified Script Hook V runtime from its author or the local cache."] = "正在从作者网站或本地缓存获取经校验的 Script Hook V 运行库。",
        ["Obtaining the verified ASI loader from its author or the local cache."] = "正在从作者网站或本地缓存获取经校验的 ASI 加载器。",
        ["Installing the verified runtime; existing unrecognized loaders and hooks are preserved."] = "正在安装经校验的运行库；会保留无法识别的现有加载器和 Hook 文件。",
        ["The old Script Hook V backup could not be verified."] = "无法通过旧版 Script Hook V 备份文件的校验。",
        ["Runtime staging file changed before commit."] = "完成配置前运行库临时文件发生变化。",
        ["Runtime setup failed and recovery needs attention. Existing backups were retained."] = "运行库配置失败，需要检查恢复情况。已保留现有备份。",
        ["The runtime download or file exceeds its size limit."] = "运行库下载内容或文件超出大小限制。",
        ["The author download returned an invalid redirect."] = "作者下载地址返回了无效跳转。",
        ["The author runtime download redirected away from its pinned archive."] = "作者运行库下载跳转到了指定安装包之外的地址。",
        ["The author runtime archive exceeds its size limit."] = "作者运行库安装包超出大小限制。",
        ["The author runtime download redirected too many times."] = "作者运行库下载跳转次数过多。",
        ["Script Hook V archive"] = "Script Hook V 安装包",
        ["Cached runtime archive"] = "缓存的运行库安装包",
        ["Unsupported host: only GTA5_Enhanced.exe is accepted."] = "不支持此程序，仅允许在 GTA5_Enhanced.exe 中运行。",
        ["Cannot verify the game executable version."] = "无法校验游戏程序版本。",
        ["GTA Online was detected. Restart the game to use Story Mode controls."] = "检测到 GTA 在线模式。请重启游戏后再使用故事模式功能。",
        ["Story Mode ready. F11 menu; F1/F2/F3 toggles; F4/F6/F7/F8 actions; F10 disable all."] = "故事模式已就绪。F11 菜单；F1/F2/F3 开关；F4/F6/F7/F8 操作；F10 全部关闭。",
        ["Paused, loading, dead, switching character, or player control unavailable. Toggles reset."] = "游戏暂停、载入中、角色死亡、切换角色或暂时无法操作。持续生效功能已重置。",
        ["Starting a new Story Mode command session."] = "正在建立新的故事模式指令连接。",
        ["Trainer stopped after a runtime error. Restart Story Mode before trying again."] = "修改器因运行错误停止，请重启故事模式后再试。",
        ["Validating the experimental Enhanced 1.0.1158.16 source runtime."] = "正在校验增强版 1.0.1158.16 的实验性开源运行库。",
        ["Matching ScriptHookV runtime is missing or lacks a required public export."] = "缺少匹配的 ScriptHookV 运行库，或其必要接口不完整。",
        ["Waiting for ScriptHookV and a loaded Story Mode character."] = "正在等待 ScriptHookV 就绪及故事模式角色载入。",
        ["GTA V runtime initialization failed. Restart Story Mode before trying again."] = "GTA V 运行库初始化失败，请重启故事模式后再试。",
        ["Session entropy unavailable"] = "无法生成安全的游戏连接标识",
        ["Invalid image header memory"] = "程序映像头内存无效",
        ["Invalid DOS header"] = "程序 DOS 头无效",
        ["Invalid x64 PE image"] = "x64 PE 程序映像无效",
        ["Invalid sections"] = "程序区段无效",
        ["Section outside image"] = "程序区段超出映像范围",
        ["Missing valid unwind table"] = "缺少有效的异常展开表",
        ["Runtime version string is not exactly 1158.16"] = "运行中的游戏版本不完全匹配 1158.16",
        ["Script/session globals are not validated writable image data"] = "脚本或游戏连接数据未通过可写映像校验",
        ["Hook/resolver signature does not identify an unwind function boundary"] = "Hook 或函数解析特征未匹配有效的函数边界",
        ["Runtime already initialized or missing callback"] = "运行库已初始化或缺少回调函数",
        ["Game header is unavailable"] = "无法读取游戏程序头",
        ["Invalid game module"] = "游戏模块无效",
        ["MinHook initialization failed"] = "MinHook 初始化失败",
        ["Unable to create scheduler hook"] = "无法创建脚本调度绑定",
        ["Unable to enable scheduler hook"] = "无法启用脚本调度绑定",
        ["Session guard unavailable; experimental runtime disabled"] = "游戏连接保护检查不可用，实验性运行库已禁用",
        ["Session guard has invalid data; experimental runtime disabled"] = "游戏连接保护数据无效，实验性运行库已禁用",
        ["Online session detected; source runtime disabled until the game restarts"] = "检测到在线战局，开源运行库已禁用至游戏重启",
        ["Native resolver returned an invalid handler; experimental runtime disabled"] = "游戏函数解析结果无效，实验性运行库已禁用",
        ["Script scheduler moved to a different operating-system thread"] = "游戏脚本调度切换至另一操作系统线程",
        ["Waiting for a valid Story Mode script array"] = "正在等待有效的故事模式脚本列表",
        ["Waiting for the Story Mode script thread"] = "正在等待故事模式脚本线程",
        ["Waiting for game script TLS"] = "正在等待游戏脚本线程数据",
        ["Waiting for valid game script TLS"] = "正在等待有效的游戏脚本线程数据",
        ["Invalid script TLS state"] = "游戏脚本线程状态无效",
        ["Experimental script callback stopped"] = "实验性脚本回调已停止",
        ["Unknown or oversized source native call"] = "开源运行库收到未知或超出限制的游戏函数调用",
        ["Unexpected vector native result"] = "游戏函数返回了非预期的向量结果"
    };

    public static string? Translate(string source)
    {
        if (Messages.TryGetValue(source, out var translated)) return translated;
        foreach (var prefix in new[] {
            ("Enhanced source runtime inactive: ", "增强版开源运行库未运行："),
            ("GTA V Enhanced payload checksum failed: ", "GTA V 增强版安装包校验失败："),
            ("Preserved an existing or modified GTA V Enhanced trainer file: ", "已保留现有或已修改的 GTA V 增强版修改器文件："),
            ("Modified GTA V Enhanced files were preserved: ", "已保留修改过的 GTA V 增强版文件："),
            ("A changed file was preserved. Its previous version was saved to ", "已保留被更改的文件，其先前版本已保存至 "),
            ("A runtime file path is a directory: ", "运行库文件的位置被文件夹占用："),
            ("Unexpected runtime file size: ", "运行库文件大小异常："),
            ("Runtime files changed during setup; the changed file was preserved: ", "配置期间运行库文件发生变化，已保留该文件："),
            ("Runtime target changed before commit: ", "完成配置前运行库目标文件发生变化："),
            ("A concurrently changed runtime file was preserved: ", "已保留被其他操作更改的运行库文件：") })
            if (source.StartsWith(prefix.Item1, StringComparison.Ordinal))
                return prefix.Item2 + (Translate(source[prefix.Item1.Length..]) ?? source[prefix.Item1.Length..]);
        var target = Regex.Match(source, @"^GTA V Enhanced (.+) is unsupported\. Experimental targets are the pinned 1\.0\.1158\.16 Steam build and 1\.0\.1158\.13; game files were preserved\.$");
        if (target.Success) return $"暂不支持 GTA V 增强版 {target.Groups[1].Value}。实验性目标版本为已核对的 Steam 1.0.1158.16 和 1.0.1158.13；已保留游戏文件。";
        var build = Regex.Match(source, @"^Unsupported GTA V Enhanced build (.+); targets are 1\.0\.1158\.16 and 1\.0\.1158\.13\.$");
        if (build.Success) return $"不支持 GTA V 增强版 {build.Groups[1].Value}；目标版本为 1.0.1158.16 和 1.0.1158.13。";
        var conflict = Regex.Match(source, @"^An unrecognized existing (.+) was preserved\. Resolve the runtime conflict before installing\.$");
        if (conflict.Success) return $"已保留无法识别的现有文件 {conflict.Groups[1].Value}，请先解决运行库冲突再安装。";
        var entry = Regex.Match(source, @"^The official runtime archive has missing or duplicate (.+) entries\.$");
        if (entry.Success) return $"官方运行库安装包中的 {entry.Groups[1].Value} 文件缺失或重复。";
        var checksum = Regex.Match(source, @"^(.+) failed the pinned size/SHA-256 verification\. No runtime was installed\.$");
        if (checksum.Success) return $"{Translate(checksum.Groups[1].Value) ?? checksum.Groups[1].Value}未通过指定大小及 SHA-256 校验，未安装运行库。";
        var recovery = Regex.Match(source, @"^Could not safely roll back (.+)\.( The newly installed runtime file remains for inspection\.| Original backup: (.+))$");
        if (recovery.Success) return $"无法安全恢复 {recovery.Groups[1].Value}。" + (recovery.Groups[3].Success ? "原始备份：" + recovery.Groups[3].Value : "新安装的运行库文件已保留以供检查。");
        var pattern = Regex.Match(source, @"^Expected one (version|script threads|native initializer|script scheduler|session guard) pattern; found ([0-9]+)$");
        if (pattern.Success) {
            var name = pattern.Groups[1].Value switch { "version" => "版本", "script threads" => "脚本线程", "native initializer" => "游戏函数初始化", "script scheduler" => "脚本调度", _ => "游戏连接保护" };
            return $"{name}特征需要唯一匹配，实际找到 {pattern.Groups[2].Value} 处";
        }
        var version = Regex.Match(source, @"^GTA V Enhanced ([0-9.]+) is unsupported\. This trainer requires ([0-9.]+); no game files were changed\.$");
        if (version.Success) return $"暂不支持 GTA V 增强版 {version.Groups[1].Value}。此修改器要求 {version.Groups[2].Value}；游戏文件未作更改。";
        var unsupported = Regex.Match(source, @"^Unsupported GTA V Enhanced executable\. This trainer requires ([0-9.]+); game files were preserved\.$");
        if (unsupported.Success) return $"此 GTA V 增强版程序暂不受支持。修改器要求 {unsupported.Groups[1].Value}；已保留游戏文件。";
        return null;
    }
}
