namespace HappyCheats.App;

internal static class DawnwalkerControllerHints
{
    internal static string Directions(int page) => page switch {
        1 => Localization.Choose("Hold Menu: ↑ Invulnerability · → Damage · ↓ Invisibility · ← Fly", "按住 Menu：↑ 无敌 · → 超级伤害 · ↓ 隐身 · ← 飞行"),
        2 => Localization.Choose("Hold Menu: ↑ Carry weight · → Money · ↓ Item use · ← Add XP", "按住 Menu：↑ 负重 · → 金钱 · ↓ 物品使用 · ← 增加经验"),
        _ => Localization.Choose("Hold Menu: ↑ Health · → Stamina · ↓ Refill · ← Blood", "按住 Menu：↑ 生命 · → 耐力 · ↓ 恢复 · ← 血量")
    };
}
