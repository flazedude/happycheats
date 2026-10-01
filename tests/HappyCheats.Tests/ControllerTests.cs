#nullable disable
// Compile separately with ControllerInput.cs; this EXE is not distributed.
// All controller states and times are injected. No physical input is generated.
using System;
using System.Runtime.InteropServices;

namespace HappyCheats.App
{
    internal static class ControllerTests
    {
        private const ushort Menu = 0x0010;
        private const ushort View = 0x0020;
        private const ushort Y = 0x8000;
        private const ushort X = 0x4000;
        private static int passed;
        private static int failed;

        public static int Main()
        {
            Run("Menu is the default modifier", DefaultModifier);
            Run("all six Onimusha shortcuts map exactly for both modifiers", AllMappings);
            Run("Onimusha is the default profile and unknown profiles are rejected", DefaultProfile);
            Run("Dawnwalker shortcuts map exactly for both modifiers", DawnwalkerMappings);
            Run("profile change requires full neutral and fresh arming", ProfileChange);
            Run("unchanged profile does not disarm", SameProfile);
            Run("Dawnwalker simultaneous edges cannot fire later", DawnwalkerSimultaneousEdges);
            Run("Dawnwalker pages expose new controls without changing Onimusha", DawnwalkerPages);
            Run("Dawnwalker page chords and manual changes preserve input guards", DawnwalkerPageGuards);
            Run("Dawnwalker auto teleport obeys edge focus and chord guards", DawnwalkerAutoTeleportGuards);
            Run("Dawnwalker X shortcut leaves Onimusha action masks unchanged", DawnwalkerXDoesNotChangeOnimusha);
            Run("Onimusha Y remains unbound after holding and repressing", OnimushaYHeld);
            Run("Onimusha Y before arming cannot bypass the hold guard", OnimushaYBeforeArming);
            Run("Onimusha Y after focus denial cannot bypass neutral", OnimushaYFocusGate);
            Run("Onimusha Y chords consume simultaneous action edges", OnimushaYSimultaneousButtons);
            Run("350 ms hold and rising edge are required", HoldAndEdge);
            Run("held chord at startup is ignored", StartupHeld);
            Run("disconnect and reconnect require neutral", ReconnectHeld);
            Run("focus denial and held return require neutral", FocusGate);
            Run("modifier change disarms until neutral", ModifierChange);
            Run("unchanged modifier does not disarm", SameModifier);
            Run("View option uses only View", ViewModifier);
            Run("first connected slot wins and switching disarms", SlotSelection);
            Run("simultaneous action edges do not queue shortcuts", SimultaneousButtons);
            Run("action pressed before modifier never becomes a new edge", ActionBeforeModifier);
            Run("release modifier restarts the hold timer", ModifierRelease);
            Run("clock rollback cannot arm shortcuts early", ClockRollback);
            Run("XInput native structures have the documented ABI size", NativeLayout);
            Console.WriteLine("Controller tests: {0} passed, {1} failed.", passed, failed);
            return failed == 0 ? 0 : 1;
        }

        private sealed class Fixture
        {
            public readonly bool[] Connected = new bool[4];
            public readonly ushort[] Buttons = new ushort[4];
            public long Now;
            public readonly ControllerInput Input;

            public Fixture()
            {
                Input = new ControllerInput(Read, delegate { return Now; });
            }

            private bool Read(int slot, out ushort buttons)
            {
                Check(slot >= 0 && slot < 4, "Invalid controller index");
                buttons = Buttons[slot];
                return Connected[slot];
            }

            public string Poll(ushort buttons, long advance, bool allow)
            {
                Now += advance;
                Buttons[0] = buttons;
                return Input.Poll(allow);
            }

            public void Neutral()
            {
                Connected[0] = true;
                Equal(null, Poll(0, 0, true));
            }

            public void Arm(ushort modifier)
            {
                Equal(null, Poll(modifier, 1, true));
                Equal(null, Poll(modifier, 350, true));
            }
        }

        private static void Run(string name, Action test)
        {
            try { test(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error); }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Equal(string expected, string actual)
        {
            Check(expected == actual, "Expected " + (expected ?? "no action") + "; got " + (actual ?? "no action"));
        }

        private static void DefaultModifier()
        {
            Fixture f = new Fixture();
            Equal("Menu", f.Input.Modifier);
            Check(!f.Input.Connected && f.Input.Slot == -1, "Initial connection state is wrong");
            try { f.Input.Modifier = "Guide"; throw new InvalidOperationException("Invalid modifier accepted"); }
            catch (ArgumentException) { }
        }

        private static void AllMappings()
        {
            ushort[] buttons = { 0x0001, 0x0008, 0x0002, 0x0004, 0x0100, 0x0200 };
            string[] actions = { "unlimited_health", "auto_parry", "one_hit_stun", "stun_immunity", "disable_all", "hud" };
            foreach (ushort modifier in new[] { Menu, View })
            {
                for (int i = 0; i < buttons.Length; i++)
                {
                    Fixture f = new Fixture();
                    f.Input.Modifier = modifier == Menu ? "Menu" : "View";
                    f.Neutral(); f.Arm(modifier);
                    Equal(actions[i], f.Poll((ushort)(modifier | buttons[i]), 1, true));
                    Equal(null, f.Poll((ushort)(modifier | buttons[i]), 1000, true));
                    Equal(null, f.Poll(modifier, 1, true));
                    Equal(actions[i], f.Poll((ushort)(modifier | buttons[i]), 1, true));
                }
            }
        }

        private static void DefaultProfile()
        {
            Fixture f = new Fixture();
            Equal("onimusha", f.Input.Profile);
            foreach (string invalid in new[] { "", "unknown", " onimusha", null })
            {
                try { f.Input.Profile = invalid; throw new InvalidOperationException("Invalid profile accepted"); }
                catch (ArgumentException) { }
                Equal("onimusha", f.Input.Profile);
            }
        }

        private static void DawnwalkerMappings()
        {
            ushort[] buttons = { 0x0001, 0x0008, 0x0002, 0x0004, 0x0100, 0x0200, Y, X };
            string[] actions = { "dawnwalker_health", "dawnwalker_stamina", "dawnwalker_refill", "dawnwalker_blood",
                "disable_all", "hud", "dawnwalker_teleport", "dawnwalker_auto_teleport" };
            foreach (ushort modifier in new[] { Menu, View })
            {
                for (int i = 0; i < buttons.Length; i++)
                {
                    Fixture f = new Fixture();
                    f.Input.Profile = "Dawnwalker";
                    f.Input.Modifier = modifier == Menu ? "Menu" : "View";
                    Equal("dawnwalker", f.Input.Profile);
                    f.Neutral(); f.Arm(modifier);
                    Equal(actions[i], f.Poll((ushort)(modifier | buttons[i]), 1, true));
                    Equal(null, f.Poll((ushort)(modifier | buttons[i]), 1000, true));
                    Equal(null, f.Poll(modifier, 1, true));
                    Equal(actions[i], f.Poll((ushort)(modifier | buttons[i]), 1, true));
                }
            }
        }

        private static void ProfileChange()
        {
            Fixture f = new Fixture();
            f.Neutral(); f.Arm(Menu);
            Equal("unlimited_health", f.Poll(Menu | 0x0001, 1, true));
            f.Input.Profile = "dawnwalker";
            Equal(null, f.Poll(Menu | 0x0001, 1000, true));
            Equal(null, f.Poll(Menu, 1, true));
            Equal(null, f.Poll(Menu | 0x0008, 1000, true));
            f.Neutral();
            Equal(null, f.Poll(Menu, 1, true));
            Equal(null, f.Poll(Menu | 0x0008, 349, true));
            Equal(null, f.Poll(Menu | 0x0008, 1, true));
            Equal(null, f.Poll(Menu, 1, true));
            Equal("dawnwalker_stamina", f.Poll(Menu | 0x0008, 1, true));
            f.Input.Profile = "ONIMUSHA";
            Equal(null, f.Poll(Menu | Y, 1000, true));
            f.Neutral(); f.Arm(Menu);
            Equal(null, f.Poll(Menu | Y, 1, true));
            Equal("unlimited_health", f.Poll(Menu | 0x0001, 1, true));
        }

        private static void SameProfile()
        {
            Fixture f = new Fixture();
            f.Neutral(); f.Arm(Menu);
            f.Input.Profile = "ONIMUSHA";
            Equal("unlimited_health", f.Poll(Menu | 0x0001, 1, true));
            f.Input.Profile = "dawnwalker";
            f.Neutral(); f.Arm(Menu);
            f.Input.Profile = "DAWNWALKER";
            Equal("dawnwalker_refill", f.Poll(Menu | 0x0002, 1, true));
        }

        private static void DawnwalkerSimultaneousEdges()
        {
            Fixture f = new Fixture();
            f.Input.Profile = "dawnwalker";
            f.Neutral(); f.Arm(Menu);
            Equal("dawnwalker_blood", f.Poll(Menu | 0x0004, 1, true));
            Equal(null, f.Poll(Menu, 1, true));
            Equal(null, f.Poll(Menu | 0x0004 | 0x0001, 1, true));
            Equal(null, f.Poll(Menu | 0x0001, 1000, true));
            Equal(null, f.Poll(Menu, 1, true));
            Equal("dawnwalker_health", f.Poll(Menu | 0x0001, 1, true));
        }

        private static void DawnwalkerPages()
        {
            foreach (ushort modifier in new[] { Menu, View })
            {
                Fixture f = new Fixture();
                f.Input.Profile = "dawnwalker";
                f.Input.Modifier = modifier == Menu ? "Menu" : "View";
                ushort pageButton = modifier == Menu ? View : Menu;
                f.Neutral(); f.Arm(modifier);
                string[][] expected = {
                    new[] { "dawnwalker_invulnerable", "dawnwalker_damage", "dawnwalker_invisible", "dawnwalker_fly" },
                    new[] { "dawnwalker_carry", "dawnwalker_money", "dawnwalker_items", "dawnwalker_xp" },
                    new[] { "dawnwalker_health", "dawnwalker_stamina", "dawnwalker_refill", "dawnwalker_blood" }
                };
                ushort[] keys = { 1, 8, 2, 4 };
                for (int page = 0; page < expected.Length; page++)
                {
                    Equal("dawnwalker_page", f.Poll((ushort)(modifier | pageButton), 1, true));
                    Equal(null, f.Poll((ushort)(modifier | pageButton), 1000, true));
                    Check(f.Input.DawnwalkerPage == (page + 1) % 3, "Unexpected page number");
                    for (int item = 0; item < keys.Length; item++)
                    {
                        Equal(null, f.Poll(modifier, 1, true));
                        Equal(expected[page][item], f.Poll((ushort)(modifier | keys[item]), 1, true));
                    }
                    foreach (var shortcut in new[] { (Key: (ushort)0x100, Action: "disable_all"), (Key: (ushort)0x200, Action: "hud"), (Key: Y, Action: "dawnwalker_teleport"), (Key: X, Action: "dawnwalker_auto_teleport") })
                    {
                        Equal(null, f.Poll(modifier, 1, true));
                        Equal(shortcut.Action, f.Poll((ushort)(modifier | shortcut.Key), 1, true));
                    }
                    Equal(null, f.Poll(modifier, 1, true));
                }
                f.Input.Profile = "onimusha";
                Check(f.Input.DawnwalkerPage == 0, "Profile switch retained the Dawnwalker page");
                f.Neutral(); f.Arm(modifier);
                Equal(null, f.Poll((ushort)(modifier | pageButton), 1, true));
                Equal("unlimited_health", f.Poll((ushort)(modifier | 1), 1, true));
            }
        }

        private static void DawnwalkerPageGuards()
        {
            Fixture f = new Fixture(); f.Input.Profile = "dawnwalker";
            f.Neutral(); f.Arm(Menu);
            Equal(null, f.Poll(Menu | View | 1, 1, true));
            Equal(null, f.Poll(Menu | View, 1000, true));
            Check(f.Input.DawnwalkerPage == 0, "Simultaneous press changed page");
            f.Input.SelectDawnwalkerPage(2);
            Equal(null, f.Poll(Menu | 1, 1000, true));
            f.Neutral(); f.Arm(Menu);
            Equal("dawnwalker_carry", f.Poll(Menu | 1, 1, true));
            Equal(null, f.Poll(Menu | View, 1, false));
            Equal(null, f.Poll(Menu | View, 1000, true));
            Check(f.Input.DawnwalkerPage == 2, "Focus denial advanced page");
            try { f.Input.SelectDawnwalkerPage(3); throw new InvalidOperationException("Invalid page accepted"); }
            catch (ArgumentOutOfRangeException) { }
        }

        private static void DawnwalkerAutoTeleportGuards()
        {
            foreach (ushort modifier in new[] { Menu, View })
            {
                Fixture f = new Fixture();
                f.Input.Profile = "dawnwalker";
                f.Input.Modifier = modifier == Menu ? "Menu" : "View";
                f.Neutral();
                Equal(null, f.Poll(modifier, 1, true));
                Equal(null, f.Poll((ushort)(modifier | X), 349, true));
                Equal(null, f.Poll((ushort)(modifier | X), 1000, true));
                Equal(null, f.Poll(modifier, 1, true));
                Equal("dawnwalker_auto_teleport", f.Poll((ushort)(modifier | X), 1, true));
                Equal(null, f.Poll((ushort)(modifier | X), 1000, true));
                Equal(null, f.Poll(modifier, 1, true));
                Equal(null, f.Poll((ushort)(modifier | X | Y), 1, true));
                Equal(null, f.Poll((ushort)(modifier | X), 1000, true));
                Equal(null, f.Poll(modifier, 1, true));
                Equal("dawnwalker_auto_teleport", f.Poll((ushort)(modifier | X), 1, true));
                Equal(null, f.Poll((ushort)(modifier | X), 1, false));
                Equal(null, f.Poll((ushort)(modifier | X), 1000, true));
                Equal(null, f.Poll(modifier, 1, true));
                Equal(null, f.Poll((ushort)(modifier | X), 1000, true));
                f.Neutral(); f.Arm(modifier);
                Equal("dawnwalker_auto_teleport", f.Poll((ushort)(modifier | X), 1, true));
            }
        }

        private static void DawnwalkerXDoesNotChangeOnimusha()
        {
            foreach (ushort modifier in new[] { Menu, View })
            {
                Fixture f = new Fixture();
                f.Input.Modifier = modifier == Menu ? "Menu" : "View";
                f.Neutral(); f.Arm(modifier);
                Equal(null, f.Poll((ushort)(modifier | X), 1, true));
                Equal(null, f.Poll(modifier, 1, true));
                Equal(null, f.Poll((ushort)(modifier | X | Y), 1, true));
                Equal(null, f.Poll(modifier, 1, true));
                Equal("unlimited_health", f.Poll((ushort)(modifier | X | 0x0001), 1, true));
            }
        }

        private static void OnimushaYHeld()
        {
            foreach (ushort modifier in new[] { Menu, View })
            {
                Fixture f = new Fixture();
                f.Input.Modifier = modifier == Menu ? "Menu" : "View";
                f.Neutral(); f.Arm(modifier);
                Equal(null, f.Poll((ushort)(modifier | Y), 1, true));
                Equal(null, f.Poll((ushort)(modifier | Y), 1000, true));
                Equal(null, f.Poll((ushort)(modifier | Y), 10000, true));
                Equal(null, f.Poll(modifier, 1, true));
                Equal(null, f.Poll((ushort)(modifier | Y), 1, true));
                Equal("unlimited_health", f.Poll((ushort)(modifier | 0x0001), 1, true));
            }
        }

        private static void OnimushaYBeforeArming()
        {
            foreach (ushort modifier in new[] { Menu, View })
            {
                Fixture f = new Fixture();
                f.Input.Modifier = modifier == Menu ? "Menu" : "View";
                f.Neutral();
                Equal(null, f.Poll(Y, 1, true));
                Equal(null, f.Poll((ushort)(modifier | Y), 1, true));
                Equal(null, f.Poll((ushort)(modifier | 0x0001), 349, true));
                Equal(null, f.Poll((ushort)(modifier | 0x0001), 1, true));
                Equal(null, f.Poll((ushort)(modifier | 0x0001), 1000, true));
                Equal(null, f.Poll(modifier, 1, true));
                Equal(null, f.Poll((ushort)(modifier | Y), 1, true));
                Equal("unlimited_health", f.Poll((ushort)(modifier | 0x0001), 1, true));
            }
        }

        private static void OnimushaYFocusGate()
        {
            foreach (ushort modifier in new[] { Menu, View })
            {
                Fixture f = new Fixture();
                f.Input.Modifier = modifier == Menu ? "Menu" : "View";
                f.Neutral(); f.Arm(modifier);
                Equal(null, f.Poll((ushort)(modifier | Y), 1, false));
                Equal(null, f.Poll((ushort)(modifier | Y), 1000, true));
                Equal(null, f.Poll(modifier, 1, true));
                Equal(null, f.Poll((ushort)(modifier | 0x0001), 1000, true));
                f.Neutral(); f.Arm(modifier);
                Equal(null, f.Poll((ushort)(modifier | Y), 1, true));
                Equal("unlimited_health", f.Poll((ushort)(modifier | 0x0001), 1, true));
            }
        }

        private static void OnimushaYSimultaneousButtons()
        {
            ushort[] otherButtons = { 0x0001, 0x0008, 0x0002, 0x0004, 0x0100, 0x0200 };
            string[] actions = { "unlimited_health", "auto_parry", "one_hit_stun", "stun_immunity", "disable_all", "hud" };
            foreach (ushort modifier in new[] { Menu, View })
            {
                for (int i = 0; i < otherButtons.Length; i++)
                {
                    ushort other = otherButtons[i];
                    Fixture f = new Fixture();
                    f.Input.Modifier = modifier == Menu ? "Menu" : "View";
                    f.Neutral(); f.Arm(modifier);
                    Equal(null, f.Poll((ushort)(modifier | Y | other), 1, true));
                    Equal(null, f.Poll((ushort)(modifier | Y | other), 1000, true));
                    Equal(null, f.Poll((ushort)(modifier | other), 1, true));
                    Equal(null, f.Poll((ushort)(modifier | other), 1000, true));
                    Equal(null, f.Poll(modifier, 1, true));
                    Equal(null, f.Poll((ushort)(modifier | Y), 1, true));
                    Equal(actions[i], f.Poll((ushort)(modifier | other), 1, true));
                }
            }
        }

        private static void HoldAndEdge()
        {
            Fixture f = new Fixture();
            f.Neutral();
            Equal(null, f.Poll(Menu, 0, true));
            Equal(null, f.Poll(Menu | 0x0001, 349, true));
            Equal(null, f.Poll(Menu | 0x0001, 1, true)); // Holding cannot turn an early press into a new edge.
            Equal(null, f.Poll(Menu, 0, true));
            Equal("unlimited_health", f.Poll(Menu | 0x0001, 0, true));
        }

        private static void StartupHeld()
        {
            Fixture f = new Fixture();
            f.Connected[0] = true;
            Equal(null, f.Poll(Menu | 0x0001, 0, true));
            Equal(null, f.Poll(Menu | 0x0001, 1000, true));
            Equal(null, f.Poll(Menu, 1000, true));
            Equal(null, f.Poll(Menu | 0x0008, 1, true));
            f.Neutral();
            f.Arm(Menu);
            Equal("auto_parry", f.Poll(Menu | 0x0008, 1, true));
        }

        private static void ReconnectHeld()
        {
            Fixture f = new Fixture();
            f.Neutral(); f.Arm(Menu);
            f.Connected[0] = false;
            Equal(null, f.Input.Poll(true));
            Check(!f.Input.Connected && f.Input.Slot == -1, "Disconnect was not reported");
            f.Connected[0] = true;
            Equal(null, f.Poll(Menu | 0x0200, 1000, true));
            f.Neutral(); f.Arm(Menu);
            Equal("hud", f.Poll(Menu | 0x0200, 1, true));
        }

        private static void FocusGate()
        {
            Fixture f = new Fixture();
            f.Neutral(); f.Arm(Menu);
            Equal(null, f.Poll(Menu | 0x0100, 1, false));
            Check(f.Input.Connected, "Focus gate should still report controller connection");
            Equal(null, f.Poll(Menu | 0x0100, 1000, true));
            Equal(null, f.Poll(Menu, 1, true));
            Equal(null, f.Poll(Menu | 0x0100, 1000, true));
            f.Neutral(); f.Arm(Menu);
            Equal("disable_all", f.Poll(Menu | 0x0100, 1, true));
        }

        private static void ModifierChange()
        {
            Fixture f = new Fixture();
            f.Neutral(); f.Arm(Menu);
            f.Input.Modifier = "View";
            Equal(null, f.Poll(View | 0x0004, 1000, true));
            f.Neutral(); f.Arm(View);
            Equal("stun_immunity", f.Poll(View | 0x0004, 1, true));
        }

        private static void SameModifier()
        {
            Fixture f = new Fixture();
            f.Neutral(); f.Arm(Menu);
            f.Input.Modifier = "menu";
            Equal("unlimited_health", f.Poll(Menu | 0x0001, 1, true));
        }

        private static void ViewModifier()
        {
            Fixture f = new Fixture();
            f.Input.Modifier = "View";
            f.Neutral(); f.Arm(Menu);
            Equal(null, f.Poll(Menu | 0x0001, 1, true));
            f.Poll(0, 0, true); f.Arm(View);
            Equal("unlimited_health", f.Poll(View | 0x0001, 1, true));
        }

        private static void SlotSelection()
        {
            Fixture f = new Fixture();
            f.Connected[2] = true;
            Equal(null, f.Input.Poll(true));
            Check(f.Input.Connected && f.Input.Slot == 2, "First connected slot not found");
            f.Buttons[2] = Menu; f.Now++; f.Input.Poll(true); f.Now += 350; f.Input.Poll(true);
            f.Connected[1] = true; f.Buttons[1] = Menu | 0x0001;
            Equal(null, f.Input.Poll(true));
            Check(f.Input.Slot == 1, "Lower connected slot did not take priority");
            f.Now += 1000; Equal(null, f.Input.Poll(true));
            f.Buttons[1] = 0; f.Input.Poll(true);
            f.Buttons[1] = Menu; f.Now++; f.Input.Poll(true);
            f.Now += 350; f.Input.Poll(true);
            f.Buttons[1] = Menu | 0x0001;
            Equal("unlimited_health", f.Input.Poll(true));
        }

        private static void SimultaneousButtons()
        {
            Fixture f = new Fixture();
            f.Neutral(); f.Arm(Menu);
            Equal(null, f.Poll(Menu | 0x0001 | 0x0008, 1, true));
            Equal(null, f.Poll(Menu | 0x0001 | 0x0008, 1000, true));
            Equal(null, f.Poll(Menu | 0x0001, 1, true));
            Equal(null, f.Poll(Menu, 1, true));
            Equal("auto_parry", f.Poll(Menu | 0x0008, 1, true));
        }

        private static void ActionBeforeModifier()
        {
            Fixture f = new Fixture();
            f.Neutral();
            Equal(null, f.Poll(0x0002, 1, true));
            Equal(null, f.Poll(Menu | 0x0002, 1, true));
            Equal(null, f.Poll(Menu | 0x0002, 1000, true));
            Equal(null, f.Poll(Menu, 1, true));
            Equal("one_hit_stun", f.Poll(Menu | 0x0002, 1, true));
        }

        private static void ModifierRelease()
        {
            Fixture f = new Fixture();
            f.Neutral(); f.Arm(Menu);
            f.Poll(0, 1, true);
            Equal(null, f.Poll(Menu | 0x0001, 1, true));
            Equal(null, f.Poll(Menu | 0x0001, 349, true));
            f.Poll(Menu, 1, true);
            Equal("unlimited_health", f.Poll(Menu | 0x0001, 1, true));
        }

        private static void ClockRollback()
        {
            Fixture f = new Fixture();
            f.Now = 1000; f.Neutral();
            f.Poll(Menu, 0, true);
            Equal(null, f.Poll(Menu | 0x0001, -500, true));
            f.Poll(Menu, 349, true);
            Equal("unlimited_health", f.Poll(Menu | 0x0001, 1, true));
        }

        private static void NativeLayout()
        {
            Type gamepad = typeof(ControllerInput).GetNestedType("NativeGamepad",
                System.Reflection.BindingFlags.NonPublic);
            Type state = typeof(ControllerInput).GetNestedType("NativeState",
                System.Reflection.BindingFlags.NonPublic);
            Check(Marshal.SizeOf(gamepad) == 12 && Marshal.SizeOf(state) == 16, "XInput layout mismatch");
        }
    }
}

