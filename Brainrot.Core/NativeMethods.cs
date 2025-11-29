using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Brainrot.Core
{
    internal static class NativeMethods
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        public static string? GetActiveProcessName()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
                return null;

            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            if (pid == 0)
                return null;

            try
            {
                var proc = Process.GetProcessById((int)pid);
                return proc.ProcessName;
            }
            catch
            {
                return null;
            }
        }
    }
}
