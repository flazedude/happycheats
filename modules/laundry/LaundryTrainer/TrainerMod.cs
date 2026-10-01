using System.Reflection;
using MelonLoader;

[assembly: MelonInfo(typeof(LaundryTrainer.TrainerMod), "Laundry Trainer", "0.3.0", "Happy Cheats")]
// Game identity is also checked by executable name and both build fingerprints.
[assembly: MelonGame(null, null)]

namespace LaundryTrainer;

public sealed class TrainerMod : MelonMod
{
    private readonly Hotkeys _hotkeys = new();
    private GameBindings? _game;
    private WashSpeed? _wash;
    private DryerSpeed? _dryer;
    private ShopLevel? _shop;
    private StatusOverlay? _overlay;
    private LauncherBridge? _bridge;
    private MelonPreferences_Entry<float>? _moneyStep;
    private MelonPreferences_Entry<float>? _xpStep;
    private MelonPreferences_Entry<float>? _washMultiplier;
    private MelonPreferences_Entry<float>? _dryerMultiplier;
    private MelonPreferences_Category? _preferences;
    private bool _inGame;
    private bool _showOverlay = true;
    private bool _ready;
    private long _nextReadinessCheck;
    private string _status = "请先载入存档，再使用修改器。";

    public override void OnInitializeMelon()
    {
        _inGame = string.Equals(Path.GetFileName(Environment.ProcessPath),
            "Laundry Store Simulator.exe", StringComparison.OrdinalIgnoreCase);
        if (!_inGame)
        {
            LoggerInstance.Warning("洗衣店模拟器修改器仅支持 Laundry Store Simulator.exe。");
            return;
        }

        var preferences = _preferences = MelonPreferences.CreateCategory("LaundryTrainer");
        _moneyStep = preferences.CreateEntry("MoneyPerPress", 10_000f);
        _xpStep = preferences.CreateEntry("XpPerPress", 100f);
        _washMultiplier = preferences.CreateEntry("WashSpeedMultiplier", 5f);
        _dryerMultiplier = preferences.CreateEntry("DryerSpeedMultiplier", 5f);
        try { _bridge = new LauncherBridge(Path.GetDirectoryName(Environment.ProcessPath)!); }
        catch (Exception error) { LoggerInstance.Warning("Launcher bridge unavailable: " + Describe(error)); }
        try
        {
            SupportedBuild.Verify(Path.GetDirectoryName(Environment.ProcessPath)!);
            var gameAssembly = Assembly.Load("Assembly-CSharp");
            _game = new GameBindings(gameAssembly);
            try { _wash = new WashSpeed(gameAssembly, Report) { Multiplier = ValidMultiplier(_washMultiplier.Value) }; }
            catch (Exception error) { LoggerInstance.Error($"快速洗衣不可用：{Describe(error)}"); }
            try { _dryer = new DryerSpeed(gameAssembly, Report) { Multiplier = ValidMultiplier(_dryerMultiplier.Value) }; }
            catch (Exception error) { LoggerInstance.Error($"烘干调速不可用：{Describe(error)}"); }
            try { _shop = new ShopLevel(gameAssembly); }
            catch (Exception error) { LoggerInstance.Error($"洗衣店升级不可用：{Describe(error)}"); }
            LoggerInstance.Msg("已就绪：F5 面板 | F6 金钱 | F7 玩家经验 | F8 快速洗衣 | F9 烘干调速 | F10 洗衣店升一级。洗衣和烘干加速默认关闭。");
        }
        catch (Exception error)
        {
            Report("修改器不可用：" + Describe(error));
        }
    }

    public override void OnLateInitializeMelon()
    {
        if (!_inGame) return;
        try { _overlay = new StatusOverlay(); }
        catch (Exception error) { LoggerInstance.Warning($"状态面板不可用，快捷键仍可使用。{Describe(error)}"); }
    }

    public override void OnUpdate()
    {
        if (!_inGame) return;
        if (_bridge is not null)
        {
            try { _bridge.Tick(GetBridgeState, ExecuteBridgeCommand); }
            catch (Exception error)
            {
                LoggerInstance.Warning("Launcher bridge disabled: " + Describe(error));
                _bridge = null;
            }
        }
        var panel = _hotkeys.Pressed(0x74); // F5
        var money = _hotkeys.Pressed(0x75); // F6
        var xp = _hotkeys.Pressed(0x76); // F7
        var washing = _hotkeys.Pressed(0x77); // F8
        var drying = _hotkeys.Pressed(0x78); // F9
        var shopLevel = _hotkeys.Pressed(0x79); // F10
        if (panel) _showOverlay = !_showOverlay;
        if (_game is null) return;

        if (Environment.TickCount64 >= _nextReadinessCheck)
        {
            _nextReadinessCheck = Environment.TickCount64 + 500;
            try { _ready = _game.Ready; }
            catch { _ready = false; }
        }

        if (money) Execute(() => Report($"当前金钱：{_game.AddMoney(_moneyStep!.Value):N0}"));
        if (xp) Execute(() => Report($"已增加玩家经验：{_game.AddXp(_xpStep!.Value):N1}"));
        if (washing) Execute(ToggleWashing);
        if (drying) Execute(() =>
        {
            if (_hotkeys.Down(0x11)) AdjustDrying(-1); // Ctrl + F9
            else if (_hotkeys.Down(0x10)) AdjustDrying(1); // Shift + F9
            else ToggleDrying();
        });
        if (shopLevel) Execute(IncreaseShopLevel);
    }

    private void ToggleDrying()
    {
        var dryer = RequireDryer();
        if (!dryer.Enabled)
        {
            if (_game?.Ready is not true) throw new InvalidOperationException("请先载入存档，再开启烘干调速。");
            dryer.Multiplier = ConfiguredDryerMultiplier();
        }
        dryer.Enabled = !dryer.Enabled;
        Report(dryer.Enabled ? $"烘干调速已开启：{dryer.Multiplier:0.#} 倍" : "烘干调速已关闭，恢复正常速度。");
    }

    private void AdjustDrying(int step)
    {
        var dryer = RequireDryer();
        var factor = Math.Clamp(ConfiguredDryerMultiplier() + step, 1, 20);
        dryer.Multiplier = factor;
        _dryerMultiplier!.Value = factor;
        var message = dryer.Enabled ? $"烘干速度已调整为 {factor:0.#} 倍。" : $"烘干倍率已设为 {factor:0.#} 倍，按 F9 开启。";
        try { _preferences!.SaveToFile(false); }
        catch (Exception error) { message += $"设置保存失败：{Describe(error)}"; }
        Report(message);
    }

    private DryerSpeed RequireDryer() => _dryer is { Faulted: false } dryer ? dryer :
        throw new InvalidOperationException("烘干调速不可用，请查看 MelonLoader 日志。");

    private float ConfiguredDryerMultiplier()
    {
        var factor = _dryerMultiplier!.Value;
        if (!float.IsFinite(factor) || factor < 1 || factor > 20)
            throw new InvalidOperationException("烘干速度倍率（DryerSpeedMultiplier）必须在 1 到 20 之间。");
        return factor;
    }

    private void IncreaseShopLevel()
    {
        if (_shop is null) throw new InvalidOperationException("洗衣店升级不可用，请查看 MelonLoader 日志。");
        if (_game?.Ready is not true) throw new InvalidOperationException("请先载入存档，再提升洗衣店等级。");
        Report($"洗衣店等级已提升，当前等级：{_shop.IncreaseLevel()}。");
    }

    private void ToggleWashing()
    {
        if (_wash is null || _wash.Faulted)
            throw new InvalidOperationException("快速洗衣不可用，请查看 MelonLoader 日志。");
        if (!_wash.Enabled)
        {
            if (_game?.Ready is not true) throw new InvalidOperationException("请先载入存档，再开启快速洗衣。");
            var factor = _washMultiplier!.Value;
            if (!float.IsFinite(factor) || factor < 1 || factor > 20)
                throw new InvalidOperationException("洗衣速度倍率（WashSpeedMultiplier）必须在 1 到 20 之间。");
            _wash.Multiplier = factor;
        }
        _wash.Enabled = !_wash.Enabled;
        Report(_wash.Enabled ? $"快速洗衣已开启：{_wash.Multiplier:0.#} 倍" : "快速洗衣已关闭。");
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        _bridge?.ChangeSession();
        _ready = false;
        _nextReadinessCheck = 0;
        var wasAccelerated = _wash?.Enabled is true || _dryer?.Enabled is true;
        if (_wash is not null) _wash.Enabled = false;
        if (_dryer is not null) _dryer.Enabled = false;
        if (wasAccelerated)
        {
            Report("场景已切换，洗衣和烘干加速已自动关闭。");
        }
    }

    public override void OnGUI()
    {
        if (!_showOverlay || _overlay is null) return;
        try
        {
            var washLabel = _wash is null || _wash.Faulted ? "不可用" :
                _wash.Enabled ? $"已开启（{_wash.Multiplier:0.#} 倍）" : "已关闭";
            var dryerLabel = _dryer is null || _dryer.Faulted ? "不可用" :
                _dryer.Enabled ? $"已开启（{_dryer.Multiplier:0.#} 倍）" : $"已关闭（设定 {_dryerMultiplier?.Value ?? 5:0.#} 倍）";
            _overlay.Draw(new[]
            {
                "F5  显示 / 隐藏面板",
                $"F6  增加 {_moneyStep?.Value ?? 10_000:N0} 金钱",
                $"F7  增加 {_xpStep?.Value ?? 100:N0} 玩家经验",
                $"F8  快速洗衣：{washLabel}",
                $"F9  烘干调速：{dryerLabel}",
                "Ctrl + F9 减 1 倍 / Shift + F9 加 1 倍（1–20）",
                _shop is null ? "F10 洗衣店升一级：不可用" : "F10 洗衣店升一级（补齐本级经验和挑战）",
                _game is null ? "游戏版本不受支持 / 游戏接口不可用" :
                    _ready ? "存档已载入" : "等待载入存档",
                _status
            });
        }
        catch (Exception error)
        {
            _overlay = null;
            LoggerInstance.Warning($"状态面板已停用：{Describe(error)}");
        }
    }

    public override void OnDeinitializeMelon()
    {
        try { _bridge?.Dispose(); }
        catch (Exception error) { LoggerInstance.Warning("Launcher disconnect failed: " + Describe(error)); }
        _bridge = null;
        try { _wash?.Dispose(); }
        catch (Exception error) { LoggerInstance.Warning($"无法移除快速洗衣补丁：{Describe(error)}"); }
        _wash = null;
        try { _dryer?.Dispose(); }
        catch (Exception error) { LoggerInstance.Warning($"无法移除烘干调速补丁：{Describe(error)}"); }
        _dryer = null;
        _shop = null;
        _game = null;
    }

    private void Execute(Action action)
    {
        try { action(); }
        catch (Exception error) { Report(Describe(error)); }
    }

    private BridgeState GetBridgeState()
    {
        bool ready;
        try { ready = _game?.Ready is true; }
        catch { ready = false; }
        return new BridgeState(_game is not null, ready, _status,
            _wash?.Enabled is true, _wash?.Multiplier ?? 5,
            _dryer?.Enabled is true, _dryer?.Multiplier ?? 5,
            _wash is { Faulted: false }, _dryer is { Faulted: false }, _shop is not null);
    }

    private string ExecuteBridgeCommand(BridgeCommand command)
    {
        if (command.Action == "disable_all")
        {
            if (_wash is not null) _wash.Enabled = false;
            if (_dryer is not null) _dryer.Enabled = false;
            Report("洗衣和烘干加速已关闭。");
            return _status;
        }
        if (_game is null) throw new InvalidOperationException("Unsupported game build or unavailable game bindings.");
        var value = command.Value;
        switch (command.Action)
        {
            case "cash":
                Report($"当前金钱：{_game.AddMoney(RequireValue(value, 1, 1_000_000)):N0}");
                break;
            case "xp":
                Report($"已增加玩家经验：{_game.AddXp(RequireValue(value, 1, 10_000)):N1}");
                break;
            case "shop":
                IncreaseShopLevel();
                break;
            case "wash":
                if (_wash is null || _wash.Faulted) throw new InvalidOperationException("Washing acceleration unavailable.");
                var washFactor = RequireValue(value, 1, 20);
                var washEnabled = command.Enabled ?? throw new InvalidOperationException("Enabled state is required.");
                if (washEnabled && !_game.Ready) throw new InvalidOperationException("Load a save first.");
                _wash.Multiplier = washFactor;
                _wash.Enabled = washEnabled;
                _washMultiplier!.Value = washFactor;
                Report(washEnabled ? $"快速洗衣已开启：{washFactor:0.#} 倍" : "快速洗衣已关闭。");
                break;
            case "dry":
                var dryer = RequireDryer();
                var dryFactor = RequireValue(value, 1, 20);
                var dryEnabled = command.Enabled ?? throw new InvalidOperationException("Enabled state is required.");
                if (dryEnabled && !_game.Ready) throw new InvalidOperationException("Load a save first.");
                dryer.Multiplier = dryFactor;
                dryer.Enabled = dryEnabled;
                _dryerMultiplier!.Value = dryFactor;
                Report(dryEnabled ? $"烘干调速已开启：{dryFactor:0.#} 倍" : "烘干调速已关闭。");
                break;
            default: throw new InvalidOperationException("Unknown Laundry trainer action.");
        }
        if (command.Action is "wash" or "dry")
        {
            try { _preferences!.SaveToFile(false); }
            catch (Exception error) { Report(_status + " 设置保存失败：" + Describe(error)); }
        }
        return _status;
    }

    private static float RequireValue(double? value, double minimum, double maximum)
    {
        if (value is not double number || !double.IsFinite(number) || number < minimum || number > maximum)
            throw new ArgumentOutOfRangeException(nameof(value), $"Value must be between {minimum} and {maximum}.");
        return (float)number;
    }

    private static float ValidMultiplier(float value) => float.IsFinite(value) && value >= 1 && value <= 20 ? value : 5;

    private void Report(string message)
    {
        _status = message;
        LoggerInstance.Msg(message);
    }

    private static string Describe(Exception error) => error.GetBaseException().Message;
}
