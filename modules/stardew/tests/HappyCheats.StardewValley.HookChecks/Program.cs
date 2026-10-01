using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Runtime.Serialization;
using HarmonyLib;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Inventories;

internal static class Program
{
    private static int assertions;
    private static bool simulatedLocal, simulatedReady;
    private static int simulatedStack, simulatedReductions;

    private static int Main(string[] args)
    {
        // The primary references are copied into this ignored test output. Resolve any
        // optional transitive dependencies from local installs without copying game assets.
        string game = args.ElementAtOrDefault(0) ?? @"D:\Steam\steamapps\common\Stardew Valley";
        string? smapi = args.ElementAtOrDefault(1);
        var directories = new[] { AppContext.BaseDirectory, game, smapi, smapi is null ? null : Path.Combine(smapi, "smapi-internal") }
            .Where(p => !string.IsNullOrEmpty(p)).Cast<string>().ToArray();
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            foreach (string directory in directories)
            {
                string path = Path.Combine(directory, name.Name + ".dll");
                if (File.Exists(path)) return context.LoadFromAssemblyPath(Path.GetFullPath(path));
            }
            return null;
        };
        try { Run(); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run()
    {
        Assembly mod = Assembly.Load("HappyCheats.StardewValley");
        Type hooks = mod.GetType("HappyCheats.StardewValley.GamePatches", throwOnError: true)!;
        MethodInfo Hook(string name) => hooks.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(hooks.FullName, name);
        MethodInfo Target(Type type, string name, params Type[] arguments) => AccessTools.DeclaredMethod(type, name, arguments)
            ?? throw new MissingMethodException(type.FullName, name);
        var consumeTargets = new[] {
            Target(typeof(Farmer), "eatHeldObject"),
            Target(typeof(Utility), "tryToPlaceItem", typeof(GameLocation), typeof(StardewValley.Object), typeof(int), typeof(int)),
            Target(typeof(Game1), "pressActionButton", typeof(KeyboardState), typeof(MouseState), typeof(GamePadState))
        };
        var speed = AccessTools.PropertyGetter(typeof(Farmer), "addedSpeed")!;
        var giftTarget = Target(typeof(NPC), "tryToReceiveActiveObject", typeof(Farmer), typeof(bool));
        var expected = new[] {
            Target(typeof(Farmer), "takeDamage", typeof(int), typeof(bool), typeof(StardewValley.Monsters.Monster)),
            Target(typeof(CraftingRecipe), "doesFarmerHaveIngredientsInInventory", typeof(IList<Item>)),
            Target(typeof(CraftingRecipe), "consumeIngredients", typeof(List<IInventory>)),
            Target(typeof(CraftingRecipe), "getCraftableCount", typeof(IList<Item>)),
            Target(typeof(CraftingRecipe), "getCraftableCount", typeof(IList<StardewValley.Objects.Chest>)),
            speed
        }.Concat(consumeTargets).Append(giftTarget).ToArray();
        Console.WriteLine($"Game: {typeof(Game1).Assembly.GetName().Version}; mod: {mod.GetName().Version}; Harmony: {typeof(Harmony).Assembly.GetName().Version}");

        // Catch dangerous Harmony result-type mismatches before native detouring.
        Check(speed.ReturnType == typeof(float), "Unexpected Farmer.addedSpeed return type");
        Check(Hook("AddSpeed").GetParameters().Single(p => p.Name == "__result").ParameterType == speed.ReturnType.MakeByRefType(),
            "AddSpeed __result must have exactly the real getter's by-reference type");
        var reducer = Target(typeof(Farmer), "reduceActiveItemByOne");
        var replacement = Hook("ReduceUsedItem");
        Check(replacement.ReturnType == typeof(void) && replacement.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(Farmer) }),
            "Consumption helper must retain the original evaluation-stack shape");

        var transpiler = Hook("ReplaceConsumption");
        foreach (var target in consumeTargets)
        {
            var original = PatchProcessor.GetOriginalInstructions(target);
            Check(original.Count(instruction => instruction.Calls(reducer)) == 1,
                $"Expected one genuine consumption call in {target.DeclaringType!.Name}.{target.Name}");
            var rewritten = ((IEnumerable<CodeInstruction>)Invoke(transpiler, original, target)!).ToList();
            Check(rewritten.All(instruction => !instruction.Calls(reducer)), "Original consumption call was left behind");
            Check(rewritten.Count(instruction => instruction.Calls(replacement)) == 1, "Replacement call count changed");
            Console.WriteLine($"PASS one consumption call redirected in {target.DeclaringType!.Name}.{target.Name}");
        }
        try
        {
            Invoke(transpiler, new[] { new CodeInstruction(OpCodes.Ret) }, consumeTargets[0]);
            throw new Exception("Transpiler accepted a target with no consumption call");
        }
        catch (InvalidOperationException) { assertions++; }
        var synthetic = new CodeInstruction(OpCodes.Callvirt, reducer);
        var generator = new DynamicMethod("Labels", typeof(void), Type.EmptyTypes).GetILGenerator();
        var label = generator.DefineLabel();
        synthetic.labels.Add(label);
        synthetic.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
        var retained = ((IEnumerable<CodeInstruction>)Invoke(transpiler, new[] { synthetic }, consumeTargets[0])!).Single();
        Check(retained.labels.Contains(label) && retained.blocks.Count == 1, "Transpiler discarded branch labels or exception blocks");

        var giftTranspiler = Hook("ReplaceGiftConsumption");
        var receiveGift = Target(typeof(NPC), "receiveGift", typeof(StardewValley.Object), typeof(Farmer), typeof(bool), typeof(float), typeof(bool));
        var giftOriginal = PatchProcessor.GetOriginalInstructions(giftTarget);
        int giftIndex = giftOriginal.FindIndex(instruction => instruction.Calls(receiveGift));
        Check(giftOriginal.Count(instruction => instruction.Calls(receiveGift)) == 1, "Expected one regular NPC gift acceptance call");
        Check(giftOriginal.Count(instruction => instruction.Calls(reducer)) == 7, "Expected seven NPC item consumption branches");
        Check(giftOriginal[giftIndex + 3].Calls(reducer), "Regular gift must consume its item immediately after acceptance and loading the farmer");
        var giftBefore = giftOriginal.Select(instruction => new CodeInstruction(instruction)).ToList();
        var giftRewritten = ((IEnumerable<CodeInstruction>)Invoke(giftTranspiler, giftOriginal, giftTarget)!).ToList();
        Check(giftRewritten.Count == giftBefore.Count, "Gift patch inserted or removed unrelated game instructions");
        Check(giftRewritten.Count(instruction => instruction.Calls(reducer)) == 6, "Gift patch changed a special offer or quest item consumption branch");
        Check(giftRewritten.Count(instruction => instruction.Calls(replacement)) == 1 && giftRewritten[giftIndex + 3].Calls(replacement),
            "Gift patch did not redirect only the accepted regular gift");
        Check(giftRewritten.Count(instruction => instruction.Calls(receiveGift)) == 1, "Gift acceptance and friendship processing must remain in the original flow");
        Check(giftRewritten.Select((instruction, index) => index == giftIndex + 3
            || giftBefore[index].opcode == instruction.opcode && Equals(giftBefore[index].operand, instruction.operand)).All(unchanged => unchanged),
            "Gift patch changed unrelated instructions, including acceptance, gift limits or special offers");

        void ExpectGiftBindingFailure(IEnumerable<CodeInstruction> instructions, string message)
        {
            try { Invoke(giftTranspiler, instructions, giftTarget); throw new Exception(message); }
            catch (InvalidOperationException) { assertions++; }
        }
        ExpectGiftBindingFailure(new[] { new CodeInstruction(OpCodes.Ret) }, "Gift patch accepted a missing gift call");
        ExpectGiftBindingFailure(giftBefore.Where((_, index) => index != giftIndex + 3).Select(instruction => new CodeInstruction(instruction)),
            "Gift patch accepted a missing accepted-gift reducer");
        ExpectGiftBindingFailure(giftBefore.Concat(giftBefore).Select(instruction => new CodeInstruction(instruction)),
            "Gift patch accepted ambiguous gift calls");
        var giftWithMetadata = giftBefore.Select(instruction => new CodeInstruction(instruction)).ToList();
        giftWithMetadata[giftIndex + 3].labels.Add(label);
        giftWithMetadata[giftIndex + 3].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
        var metadataRewritten = ((IEnumerable<CodeInstruction>)Invoke(giftTranspiler, giftWithMetadata, giftTarget)!).ToList();
        Check(metadataRewritten[giftIndex + 3].labels.Contains(label) && metadataRewritten[giftIndex + 3].blocks.Count == 1,
            "Gift patch discarded branch labels or exception blocks");
        Console.WriteLine("PASS regular NPC gifts redirect exactly one consumption call; six special/quest offers and all other game instructions retained");

        CheckConsumptionDispatch(mod, replacement, reducer);

        var harmony = new Harmony("HappyCheats.StardewValley.LocalHookChecks." + Guid.NewGuid().ToString("N"));
        try
        {
            // This compiles actual Harmony wrappers against real game methods. It does
            // not create Game1, initialize a farm, call gameplay, or open saves.
            Invoke(Hook("Install"), harmony);
            var installed = harmony.GetPatchedMethods().ToArray();
            Check(installed.Length == expected.Length && expected.All(installed.Contains),
                "Installed patch targets differ from the ten intended methods");
            foreach (var target in expected)
            {
                var info = Harmony.GetPatchInfo(target);
                Check(info is not null && info.Owners.Contains(harmony.Id), "Missing patch owner for " + target.Name);
            }
            foreach (var target in consumeTargets)
                Check(Harmony.GetPatchInfo(target)!.Transpilers.Count(p => p.owner == harmony.Id) == 1, "Consumption target is not patched exactly once");
            Check(Harmony.GetPatchInfo(giftTarget)!.Transpilers.Count(p => p.owner == harmony.Id) == 1, "Gift target is not patched exactly once");
            Console.WriteLine("PASS Harmony installed all ten genuine method patches and compiled their wrappers");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
        }
        Check(!harmony.GetPatchedMethods().Any(), "Test-owned patches remained after unpatch");
        Console.WriteLine($"PASS hook compatibility and cleanup ({assertions} assertions); no game launch or save access");
    }

    private static void CheckConsumptionDispatch(Assembly mod, MethodInfo replacement, MethodInfo reducer)
    {
        Type modEntry = mod.GetType("HappyCheats.StardewValley.ModEntry", throwOnError: true)!;
        var instance = modEntry.GetProperty("Instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousInstance = instance.GetValue(null);
        // Empty placeholders avoid Farmer/Mod constructors, Game1 setup and content/save access.
        // The genuine helper and Enabled method run, while external game state and the
        // original inventory reducer are isolated behind test-owned Harmony stubs.
        var farmer = (Farmer)FormatterServices.GetUninitializedObject(typeof(Farmer));
        var entry = FormatterServices.GetUninitializedObject(modEntry);
        var toggles = new Dictionary<string, bool> { ["stardew_unlimited_items"] = true };
        modEntry.GetField("toggles", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(entry, toggles);
        var harmony = new Harmony("HappyCheats.StardewValley.ConsumptionDispatch." + Guid.NewGuid().ToString("N"));
        try
        {
            harmony.Patch(AccessTools.PropertyGetter(typeof(Farmer), nameof(Farmer.IsLocalPlayer)), prefix: new HarmonyMethod(typeof(Program), nameof(SimulateLocalPlayer)));
            harmony.Patch(AccessTools.PropertyGetter(modEntry, "Ready"), prefix: new HarmonyMethod(typeof(Program), nameof(SimulateReady)));
            harmony.Patch(reducer, prefix: new HarmonyMethod(typeof(Program), nameof(SimulateReduction)));
            instance.SetValue(null, entry);
            foreach (int stack in new[] { 1, 3 })
            {
                simulatedStack = stack;
                simulatedReductions = 0;
                simulatedLocal = simulatedReady = true;
                toggles["stardew_unlimited_items"] = true;
                Invoke(replacement, farmer);
                Check(simulatedStack == stack && simulatedReductions == 0, $"Enabled gift retention consumed stack {stack}");
                toggles["stardew_unlimited_items"] = false;
                Invoke(replacement, farmer);
                Check(simulatedStack == stack - 1 && simulatedReductions == 1, $"Disabled gift retention changed ordinary consumption of stack {stack}");
            }
            toggles["stardew_unlimited_items"] = true;
            simulatedStack = 3;
            simulatedReductions = 0;
            simulatedLocal = false;
            Invoke(replacement, farmer);
            Check(simulatedStack == 2 && simulatedReductions == 1, "Gift retention affected a non-local farmer");
            simulatedLocal = true;
            simulatedReady = false;
            Invoke(replacement, farmer);
            Check(simulatedStack == 1 && simulatedReductions == 2, "Gift retention ignored the trainer's Ready gate");
            instance.SetValue(null, null);
            Invoke(replacement, farmer);
            Check(simulatedStack == 0 && simulatedReductions == 3, "Gift retention changed behavior before trainer initialization");
            Console.WriteLine("PASS isolated real-helper dispatch: stacks 1/3 retained while enabled; off, non-local, not-ready and uninitialized states consume normally");
        }
        finally
        {
            instance.SetValue(null, previousInstance);
            harmony.UnpatchAll(harmony.Id);
        }
        Check(!harmony.GetPatchedMethods().Any(), "Helper-dispatch stubs remained after cleanup");
    }

    private static bool SimulateLocalPlayer(ref bool __result) { __result = simulatedLocal; return false; }
    private static bool SimulateReady(ref bool __result) { __result = simulatedReady; return false; }
    private static bool SimulateReduction() { simulatedReductions++; if (simulatedStack > 0) simulatedStack--; return false; }

    private static object? Invoke(MethodInfo method, params object[] arguments)
    {
        try { return method.Invoke(null, arguments); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        assertions++;
    }
}
