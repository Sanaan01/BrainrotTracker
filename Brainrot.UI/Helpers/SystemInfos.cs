using System;
using System.Runtime.InteropServices;
using Brainrot.UI.Interop;

namespace Brainrot.UI.Helpers
{
    internal static class SystemInfos
    {
        private const uint ABM_GETTASKBARPOS = 0x00000005;

        public static RECT GetTaskBarBounds()
        {
            APPBARDATA data = new APPBARDATA();
            data.cbSize = Marshal.SizeOf(typeof(APPBARDATA));
            Shell32.SHAppBarMessage(ABM_GETTASKBARPOS, ref data);

            // Fallback if the call fails or returns an empty rect
            if (data.rc.right == 0 && data.rc.bottom == 0)
            {
                var taskbar = User32.FindWindow("Shell_TrayWnd", null);
                if (taskbar != IntPtr.Zero)
                {
                    User32.GetWindowRect(taskbar, out var rect);
                    return rect;
                }
            }

            return data.rc;
        }
    }
}
