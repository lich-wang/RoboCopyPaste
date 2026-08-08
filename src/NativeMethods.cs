using System;
using System.Runtime.InteropServices;
using System.Text;

namespace RoboCopyPaste
{
    internal static class NativeMethods
    {
        internal const int WM_CLIPBOARDUPDATE = 0x031D;
        internal const int WM_HOTKEY = 0x0312;
        internal const int HOTKEY_ID = 0x5243;
        internal const int VK_CONTROL = 0x11;
        internal const int VK_V = 0x56;
        internal const uint KEYEVENTF_KEYUP = 0x0002;
        internal const uint MOD_CONTROL = 0x0002;
        internal const uint MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, int virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);

        internal static void SendCtrlV()
        {
            keybd_event((byte)VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event((byte)VK_V, 0, 0, UIntPtr.Zero);
            keybd_event((byte)VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event((byte)VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        internal static string ClassName(IntPtr hwnd)
        {
            var text = new StringBuilder(256);
            return GetClassName(hwnd, text, text.Capacity) > 0 ? text.ToString() : string.Empty;
        }

        internal static bool IsExplorerOrDesktop(IntPtr hwnd)
        {
            string name = ClassName(hwnd);
            return name == "CabinetWClass" || name == "ExploreWClass" || name == "Progman" || name == "WorkerW";
        }
    }
}
