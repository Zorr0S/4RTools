using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace _4RTools.Utils
{
    internal class Interop
    {
        // PINVOKES
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PostMessage(IntPtr hWnd, int Msg, Keys wParam, int lParam);

        [DllImport("user32.dll")]
        public static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, int dwExtraInfo);

        // Real global key state, independent of message-queue/focus association -
        // unlike System.Windows.Input.Keyboard.IsKeyDown (which wraps the Win32
        // GetKeyState function, documented as reflecting only what the CALLING
        // THREAD has seen via its own message queue), this reads the actual
        // current hardware key state system-wide. Needed for polling from a plain
        // background Thread with no message pump of its own (see StatusCapture).
        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        public static bool IsKeyPhysicallyDown(Keys key)
        {
            return (GetAsyncKeyState((int)key) & 0x8000) != 0;
        }
    }
}
