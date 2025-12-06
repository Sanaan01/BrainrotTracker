using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Brainrot.UI.Interop
{
    internal sealed class MouseHook : IDisposable
    {
        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_MOUSEMOVE = 0x0200;

        private IntPtr _hookId = IntPtr.Zero;
        private readonly LowLevelMouseProc _proc;
        private bool _isHooked;

        public event Action<int, int>? MouseDown;
        public event Action<int, int>? MouseMove;
        public event Action<int, int>? MouseUp;

        public MouseHook()
        {
            _proc = HookCallback;
        }

        public void Install()
        {
            if (_isHooked) return;
            
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            _hookId = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(curModule?.ModuleName), 0);
            _isHooked = _hookId != IntPtr.Zero;
            
            Debug.WriteLine($"[MouseHook] Installed: {_isHooked}, hookId={_hookId}");
        }

        public void Uninstall()
        {
            if (!_isHooked || _hookId == IntPtr.Zero) return;
            
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
            _isHooked = false;
            
            Debug.WriteLine("[MouseHook] Uninstalled");
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                int msg = wParam.ToInt32();

                if (msg == WM_LBUTTONDOWN)
                {
                    MouseDown?.Invoke(hookStruct.pt.x, hookStruct.pt.y);
                }
                else if (msg == WM_MOUSEMOVE)
                {
                    MouseMove?.Invoke(hookStruct.pt.x, hookStruct.pt.y);
                }
                else if (msg == WM_LBUTTONUP)
                {
                    MouseUp?.Invoke(hookStruct.pt.x, hookStruct.pt.y);
                }
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            Uninstall();
        }

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);
    }
}
