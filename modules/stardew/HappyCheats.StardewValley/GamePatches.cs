using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using StardewValley;
using StardewValley.Inventories;
using Microsoft.Xna.Framework.Input;

namespace HappyCheats.StardewValley;

internal static class GamePatches
{
    private static readonly MethodInfo ReduceMethod = AccessTools.Method(typeof(Farmer), nameof(Farmer.reduceActiveItemByOne), Type.EmptyTypes)
        ?? throw new MissingMethodException("Farmer.reduceActiveItemByOne");
    private static readonly MethodInfo ReceiveGiftMethod = AccessTools.DeclaredMethod(typeof(NPC), nameof(NPC.receiveGift),
        new[] { typeof(global::StardewValley.Object), typeof(Farmer), typeof(bool), typeof(float), typeof(bool) })
        ?? throw new MissingMethodException("NPC.receiveGift");

    internal static void Install(Harmony harmony)
    {
        // If any required method changed, remove every patch so the module never claims partial support.
        try {
            Patch(harmony, typeof(Farmer), "takeDamage", new[] { typeof(int), typeof(bool), typeof(global::StardewValley.Monsters.Monster) }, prefix: nameof(PreventDamage));
            Patch(harmony, typeof(CraftingRecipe), "doesFarmerHaveIngredientsInInventory", new[] { typeof(IList<Item>) }, postfix: nameof(HaveIngredients));
            Patch(harmony, typeof(CraftingRecipe), "consumeIngredients", new[] { typeof(List<IInventory>) }, prefix: nameof(ConsumeIngredients));
            Patch(harmony, typeof(CraftingRecipe), "getCraftableCount", new[] { typeof(IList<Item>) }, postfix: nameof(CraftableCount));
            Patch(harmony, typeof(CraftingRecipe), "getCraftableCount", new[] { typeof(IList<global::StardewValley.Objects.Chest>) }, postfix: nameof(CraftableCount));
            harmony.Patch(AccessTools.PropertyGetter(typeof(Farmer), nameof(Farmer.addedSpeed)) ?? throw new MissingMethodException("Farmer.addedSpeed"),
                postfix: new HarmonyMethod(typeof(GamePatches), nameof(AddSpeed)));
            Patch(harmony, typeof(Farmer), "eatHeldObject", Type.EmptyTypes, transpiler: nameof(ReplaceConsumption));
            Patch(harmony, typeof(Utility), "tryToPlaceItem", new[] { typeof(GameLocation), typeof(global::StardewValley.Object), typeof(int), typeof(int) }, transpiler: nameof(ReplaceConsumption));
            Patch(harmony, typeof(Game1), "pressActionButton", new[] { typeof(KeyboardState), typeof(MouseState), typeof(GamePadState) }, transpiler: nameof(ReplaceConsumption));
            Patch(harmony, typeof(NPC), "tryToReceiveActiveObject", new[] { typeof(Farmer), typeof(bool) }, transpiler: nameof(ReplaceGiftConsumption));
        } catch { harmony.UnpatchAll(harmony.Id); throw; }
    }

    private static void Patch(Harmony harmony, Type type, string name, Type[] args, string? prefix = null, string? postfix = null, string? transpiler = null)
    {
        var original = AccessTools.DeclaredMethod(type, name, args) ?? throw new MissingMethodException(type.FullName, name);
        harmony.Patch(original,
            prefix is null ? null : new HarmonyMethod(typeof(GamePatches), prefix),
            postfix is null ? null : new HarmonyMethod(typeof(GamePatches), postfix),
            transpiler is null ? null : new HarmonyMethod(typeof(GamePatches), transpiler));
    }

    private static bool PreventDamage(Farmer __instance) => !(__instance.IsLocalPlayer && ModEntry.Instance?.Enabled("stardew_unlimited_health") == true);
    private static bool FreeCraft(CraftingRecipe recipe) => !recipe.isCookingRecipe && ModEntry.Instance?.Enabled("stardew_easy_crafting") == true;
    private static void HaveIngredients(CraftingRecipe __instance, ref bool __result) { if (FreeCraft(__instance)) __result = true; }
    private static void CraftableCount(CraftingRecipe __instance, ref int __result) { if (FreeCraft(__instance)) __result = 999; }
    private static bool ConsumeIngredients(CraftingRecipe __instance) => !FreeCraft(__instance);
    private static void AddSpeed(Farmer __instance, ref float __result) { if (__instance.IsLocalPlayer) __result += ModEntry.Instance?.SpeedBonus ?? 0; }

    // Change only use/plant/eat call sites; transfers into furniture, containers,
    // machines, shipping bins, shops and quests retain their original behavior.
    private static IEnumerable<CodeInstruction> ReplaceConsumption(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var code = instructions.ToList();
        int replaced = 0;
        var replacement = AccessTools.Method(typeof(GamePatches), nameof(ReduceUsedItem));
        foreach (var instruction in code) {
            if (!instruction.Calls(ReduceMethod)) continue;
            instruction.opcode = OpCodes.Call;
            instruction.operand = replacement;
            replaced++;
        }
        if (replaced != 1) throw new InvalidOperationException("Item consumption binding changed: " + __originalMethod.Name);
        return code;
    }

    private static IEnumerable<CodeInstruction> ReplaceGiftConsumption(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var code = instructions.ToList();
        // This method also consumes quest items, movie tickets and relationship items.
        // Only the reducer immediately after the normal receiveGift call is a gift.
        var gifts = code.Select((instruction, index) => (instruction, index))
            .Where(pair => pair.instruction.Calls(ReceiveGiftMethod)).Select(pair => pair.index).ToArray();
        if (gifts.Length != 1) throw new InvalidOperationException("Gift consumption binding changed: " + __originalMethod.Name);
        int gift = gifts[0];
        if (gift + 3 >= code.Count || code[gift + 1].opcode != OpCodes.Ldloc_0
            || code[gift + 2].opcode != OpCodes.Ldfld || code[gift + 2].operand is not FieldInfo farmer
            || farmer.FieldType != typeof(Farmer) || farmer.Name != "who" || !code[gift + 3].Calls(ReduceMethod))
            throw new InvalidOperationException("Gift consumption binding changed: " + __originalMethod.Name);
        // Keep the instruction's branch labels and exception blocks. The existing helper
        // applies the toggle, local-player and ready/single-player gates before consuming.
        code[gift + 3].opcode = OpCodes.Call;
        code[gift + 3].operand = AccessTools.Method(typeof(GamePatches), nameof(ReduceUsedItem));
        return code;
    }

    private static void ReduceUsedItem(Farmer who)
    {
        if (who.IsLocalPlayer && ModEntry.Instance?.Enabled("stardew_unlimited_items") == true) return;
        who.reduceActiveItemByOne();
    }
}
