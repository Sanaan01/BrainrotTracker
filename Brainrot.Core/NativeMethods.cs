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
        private static extern uint GetWindowThreadProcessId(
            IntPtr hWnd,
            out uint processId);

        /// <summary>
        /// Returns the active foreground process name (without .exe),
        /// e.g. "chrome", "devenv", "explorer".
        /// </summary>
        public static string? GetActiveProcessName()
        {
            try
            {
                IntPtr handle = GetForegroundWindow();
                if (handle == IntPtr.Zero)
                    return null;

                _ = GetWindowThreadProcessId(handle, out uint pid);
                if (pid == 0)
                    return null;

                using var proc = Process.GetProcessById((int)pid);
                return proc.ProcessName;
            }
            catch
            {
                // We don't care about occasional failures.
                return null;
            }
        }
    }
}
