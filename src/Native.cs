using System;
using System.Runtime.InteropServices;

namespace SteadyClick
{
    /// <summary>Thin wrappers over the Win32 calls that move and click the real mouse.</summary>
    static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        // MOUSEINPUT is the largest member of the native union, so a plain sequential
        // struct has the right size and alignment on both x86 and x64.
        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public uint type;
            public MOUSEINPUT mi;
        }

        const uint INPUT_MOUSE = 0;

        public const uint LEFTDOWN = 0x0002;
        public const uint LEFTUP = 0x0004;
        public const uint RIGHTDOWN = 0x0008;
        public const uint RIGHTUP = 0x0010;
        public const uint MIDDLEDOWN = 0x0020;
        public const uint MIDDLEUP = 0x0040;

        const uint MOVE = 0x0001;
        const uint VIRTUALDESK = 0x4000;
        const uint ABSOLUTE = 0x8000;

        const int SM_SWAPBUTTON = 23;
        const int SM_XVIRTUALSCREEN = 76;
        const int SM_YVIRTUALSCREEN = 77;
        const int SM_CXVIRTUALSCREEN = 78;
        const int SM_CYVIRTUALSCREEN = 79;

        public const int WM_HOTKEY = 0x0312;
        public const uint MOD_NOREPEAT = 0x4000;

        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll")]
        static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        /// <summary>True when the user has swapped the primary and secondary buttons in Windows.</summary>
        public static bool ButtonsSwapped
        {
            get { return GetSystemMetrics(SM_SWAPBUTTON) != 0; }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        // Padded to the size of the native INPUT union (whose largest member is MOUSEINPUT).
        [StructLayout(LayoutKind.Sequential)]
        struct KEYINPUT
        {
            public uint type;
            public KEYBDINPUT ki;
            public uint pad1;
            public uint pad2;
        }

        [DllImport("user32.dll", SetLastError = true, EntryPoint = "SendInput")]
        static extern uint SendKeyInput(uint nInputs, KEYINPUT[] pInputs, int cbSize);

        /// <summary>Presses and releases one key. Only the automated window test uses this.</summary>
        public static bool TapKey(int virtualKey)
        {
            KEYINPUT[] inputs = new KEYINPUT[2];
            inputs[0].type = 1;
            inputs[0].ki.wVk = (ushort)virtualKey;
            inputs[1].type = 1;
            inputs[1].ki.wVk = (ushort)virtualKey;
            inputs[1].ki.dwFlags = 0x0002;   // KEYEVENTF_KEYUP
            return SendKeyInput(2, inputs, Marshal.SizeOf(typeof(KEYINPUT))) == 2;
        }

        [DllImport("user32.dll")]
        public static extern IntPtr GetMessageExtraInfo();

        /// <summary>Tag carried by injected events. Zero in normal use; the self-test sets it to tell
        /// its own clicks apart from the user's.</summary>
        public static IntPtr ExtraInfo = IntPtr.Zero;

        /// <summary>Injects one mouse button event wherever the cursor is. Returns false if Windows refused it.</summary>
        public static bool SendMouse(uint flags)
        {
            INPUT[] inputs = new INPUT[1];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].mi.dwFlags = flags;
            inputs[0].mi.dwExtraInfo = ExtraInfo;
            return SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT))) == 1;
        }

        /// <summary>
        /// Injects one event that both places the cursor on (x, y) and presses or releases a button.
        /// Because it is a single event, a hand moving the mouse cannot slip in between the move and
        /// the press, so the click lands on the exact pixel.
        /// </summary>
        public static bool SendMouseAt(uint flags, int x, int y)
        {
            int left = GetSystemMetrics(SM_XVIRTUALSCREEN), top = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int width = GetSystemMetrics(SM_CXVIRTUALSCREEN), height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            if (width <= 0 || height <= 0) return SendMouse(flags);

            INPUT[] inputs = new INPUT[1];
            inputs[0].type = INPUT_MOUSE;
            // Absolute coordinates run 0..65535 across the whole desktop and Windows truncates when
            // converting back to pixels, so aim for the middle of the target pixel.
            inputs[0].mi.dx = (int)(((long)(x - left) * 65536 + 32768) / width);
            inputs[0].mi.dy = (int)(((long)(y - top) * 65536 + 32768) / height);
            inputs[0].mi.dwFlags = flags | MOVE | ABSOLUTE | VIRTUALDESK;
            inputs[0].mi.dwExtraInfo = ExtraInfo;
            return SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT))) == 1;
        }
    }
}
