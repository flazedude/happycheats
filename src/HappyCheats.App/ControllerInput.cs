#nullable disable
// Read-only Xbox controller shortcuts. The launcher supplies the foreground gate.
// XInputGetState/XINPUT_GAMEPAD:
// https://learn.microsoft.com/en-us/windows/win32/api/xinput/nf-xinput-xinputgetstate
// https://learn.microsoft.com/en-us/windows/win32/api/xinput/ns-xinput-xinput_gamepad
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HappyCheats.App
{
    internal delegate bool ControllerStateReader(int slot, out ushort buttons);

    public sealed class ControllerInput
    {
        private const ushort MenuButton = 0x0010;
        private const ushort ViewButton = 0x0020;
        private const ushort RightBumper = 0x0200;
        // Y is unbound in Onimusha, but still consumes simultaneous action edges.
        private const ushort ActionButtons = 0x830F;
        // Ignore reserved bits, whose states are undefined in XINPUT_GAMEPAD.
        private const ushort DefinedButtons = 0xF3FF;
        private const long ArmDelayMilliseconds = 350;
        private readonly ControllerStateReader reader;
        private readonly Func<long> clock;
        private string modifier = "Menu";
        private string profile = "onimusha";
        private bool needsNeutral = true;
        private bool modifierPressed;
        private long modifierSince;
        private ushort previousButtons;

        public ControllerInput()
            : this(new NativeReader().Read, MonotonicMilliseconds)
        {
        }

        // Pure input/time boundaries used by the separate regression-test EXE.
        internal ControllerInput(ControllerStateReader stateReader, Func<long> milliseconds)
        {
            if (stateReader == null) throw new ArgumentNullException("stateReader");
            if (milliseconds == null) throw new ArgumentNullException("milliseconds");
            reader = stateReader;
            clock = milliseconds;
            Slot = -1;
        }

        public string Modifier
        {
            get { return modifier; }
            set
            {
                string normalized;
                if (String.Equals(value, "Menu", StringComparison.OrdinalIgnoreCase)) normalized = "Menu";
                else if (String.Equals(value, "View", StringComparison.OrdinalIgnoreCase)) normalized = "View";
                else throw new ArgumentException("Controller modifier must be Menu or View.", "value");
                if (modifier == normalized) return;
                modifier = normalized;
                ResetInput();
            }
        }

        // The launcher selects a game's profile and still gates each returned
        // action on that game's current feature availability.
        public string Profile
        {
            get { return profile; }
            set
            {
                string normalized;
                if (String.Equals(value, "onimusha", StringComparison.OrdinalIgnoreCase)) normalized = "onimusha";
                else if (String.Equals(value, "dawnwalker", StringComparison.OrdinalIgnoreCase)) normalized = "dawnwalker";
                else if (String.Equals(value, "shared", StringComparison.OrdinalIgnoreCase)) normalized = "shared";
                else throw new ArgumentException("Controller profile must be shared, onimusha or dawnwalker.", "value");
                if (profile == normalized) return;
                profile = normalized;
                DawnwalkerPage = 0;
                ResetInput();
            }
        }

        public bool Connected { get; private set; }
        public int Slot { get; private set; }
        public int DawnwalkerPage { get; private set; }

        public void SelectDawnwalkerPage(int page)
        {
            if (page < 0 || page > 2) throw new ArgumentOutOfRangeException("page");
            if (DawnwalkerPage == page) return;
            DawnwalkerPage = page;
            ResetInput();
        }

        // Returns at most one action per new button press. Simultaneous action
        // edges (for example a diagonal D-pad press) are consumed without firing.
        public string Poll(bool allowActions)
        {
            return Poll(allowActions, allowActions);
        }

        // The companion menu remains usable when the trainer is busy or not yet
        // ready. Its foreground/enable gate is separate from cheat availability.
        // Denied edges are still consumed, so readiness cannot replay held input.
        public string Poll(bool allowMenu, bool allowActions)
        {
            ushort buttons = 0;
            int selected = -1;
            for (int slot = 0; slot < 4; slot++)
            {
                if (!reader(slot, out buttons)) continue;
                selected = slot;
                break;
            }
            if (selected < 0)
            {
                Connected = false;
                Slot = -1;
                ResetInput();
                return null;
            }

            bool changedController = !Connected || Slot != selected;
            Connected = true;
            Slot = selected;
            if (changedController) ResetInput();
            if (!allowMenu && !allowActions)
            {
                ResetInput();
                return null;
            }
            if (needsNeutral)
            {
                previousButtons = buttons;
                if ((buttons & DefinedButtons) == 0) needsNeutral = false;
                return null;
            }

            ushort previous = previousButtons;
            previousButtons = buttons; // Consume edges even before the modifier arms.
            ushort modifierButton = modifier == "Menu" ? MenuButton : ViewButton;
            if ((buttons & modifierButton) == 0)
            {
                modifierPressed = false;
                return null;
            }

            long now = clock();
            if (!modifierPressed || now < modifierSince)
            {
                modifierPressed = true;
                modifierSince = now;
                return null;
            }
            if (now - modifierSince < ArmDelayMilliseconds) return null;

            // Every profile shares the same companion menu chord. Include all
            // defined buttons in ambiguity checks for this chord, even buttons
            // that have no cheat mapping in the selected legacy profile.
            int menuEdges = buttons & ~previous & DefinedButtons & ~modifierButton;
            if ((menuEdges & RightBumper) != 0)
                return menuEdges == RightBumper && allowMenu ? "game_menu" : null;
            if (!allowActions || profile == "shared") return null;

            ushort pageButton = modifierButton == MenuButton ? ViewButton : MenuButton;
            int actions = profile == "dawnwalker" ? ActionButtons | 0x4000 | pageButton : ActionButtons;
            int rising = buttons & ~previous & actions;
            if (rising == 0 || (rising & (rising - 1)) != 0) return null;
            if (profile == "dawnwalker")
            {
                if (rising == pageButton)
                {
                    DawnwalkerPage = (DawnwalkerPage + 1) % 3;
                    return "dawnwalker_page";
                }
                if (DawnwalkerPage == 1)
                {
                    switch (rising)
                    {
                        case 0x0001: return "dawnwalker_invulnerable";
                        case 0x0008: return "dawnwalker_damage";
                        case 0x0002: return "dawnwalker_invisible";
                        case 0x0004: return "dawnwalker_fly";
                    }
                }
                if (DawnwalkerPage == 2)
                {
                    switch (rising)
                    {
                        case 0x0001: return "dawnwalker_carry";
                        case 0x0008: return "dawnwalker_money";
                        case 0x0002: return "dawnwalker_items";
                        case 0x0004: return "dawnwalker_xp";
                    }
                }
                switch (rising)
                {
                    case 0x0001: return "dawnwalker_health";  // D-pad Up.
                    case 0x0008: return "dawnwalker_stamina"; // D-pad Right.
                    case 0x0002: return "dawnwalker_refill";  // D-pad Down.
                    case 0x0004: return "dawnwalker_blood";   // D-pad Left.
                    case 0x0100: return "disable_all";        // Left bumper.
                    case 0x8000: return "dawnwalker_teleport"; // Y; launcher availability-gated.
                    case 0x4000: return "dawnwalker_auto_teleport"; // X, on every page.
                    default: return null;
                }
            }
            switch (rising)
            {
                case 0x0001: return "unlimited_health"; // D-pad Up.
                case 0x0008: return "auto_parry";       // D-pad Right.
                case 0x0002: return "one_hit_stun";     // D-pad Down.
                case 0x0004: return "stun_immunity";    // D-pad Left.
                case 0x0100: return "disable_all";      // Left bumper.
                default: return null;
            }
        }

        private void ResetInput()
        {
            needsNeutral = true;
            modifierPressed = false;
            modifierSince = 0;
            previousButtons = 0;
        }

        private static long MonotonicMilliseconds()
        {
            return (long)(Stopwatch.GetTimestamp() * (1000.0 / Stopwatch.Frequency));
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeGamepad
        {
            public ushort Buttons;
            public byte LeftTrigger;
            public byte RightTrigger;
            public short ThumbLX;
            public short ThumbLY;
            public short ThumbRX;
            public short ThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeState
        {
            public uint PacketNumber;
            public NativeGamepad Gamepad;
        }

        private sealed class NativeReader
        {
            // 0/1 = try modern, 2 = legacy, 3 = neither DLL exports XInputGetState.
            private int backend;

            [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState", ExactSpelling = true)]
            private static extern uint GetStateModern(uint slot, out NativeState state);

            [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState", ExactSpelling = true)]
            private static extern uint GetStateLegacy(uint slot, out NativeState state);

            public bool Read(int slot, out ushort buttons)
            {
                buttons = 0;
                NativeState state;
                if (backend < 2)
                {
                    try
                    {
                        uint result = GetStateModern((uint)slot, out state);
                        backend = 1;
                        if (result != 0) return false;
                        buttons = state.Gamepad.Buttons;
                        return true;
                    }
                    catch (DllNotFoundException) { backend = 2; }
                    catch (EntryPointNotFoundException) { backend = 2; }
                    catch (BadImageFormatException) { backend = 2; }
                }
                if (backend == 2)
                {
                    try
                    {
                        if (GetStateLegacy((uint)slot, out state) != 0) return false;
                        buttons = state.Gamepad.Buttons;
                        return true;
                    }
                    catch (DllNotFoundException) { backend = 3; }
                    catch (EntryPointNotFoundException) { backend = 3; }
                    catch (BadImageFormatException) { backend = 3; }
                }
                return false;
            }
        }
    }
}
