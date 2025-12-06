using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Brainrot.UI.Interop;

namespace Brainrot.UI
{
    internal sealed class TrayIconHost : IDisposable
    {
        private const int WM_APP = 0x8000;
        private const int WM_TRAY = WM_APP + 1;
        private const int WM_CONTEXTMENU = 0x007B;
        private const int NIN_SELECT = 0x0400;
        private const int NIN_KEYSELECT = 0x0401;
        private const int NIF_MESSAGE = 0x0001;
        private const int NIF_ICON = 0x0002;
        private const int NIF_TIP = 0x0004;
        private const int NIF_GUID = 0x0020;
        private const int NIM_ADD = 0x0000;
        private const int NIM_DELETE = 0x0002;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_DESTROY = 0x0002;
        private const int WS_OVERLAPPED = 0x00000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int NIM_SETVERSION = 0x00000004;
        private const int NOTIFYICON_VERSION_4 = 4;
        private const int IDI_APPLICATION = 32512;
        private const int IDI_INFORMATION = 32516;

        private readonly WndProc _wndProcDelegate;
        private IntPtr _hwnd;
        private bool _iconAdded;
        private readonly string _tip;
        private readonly ushort _classAtom;
        private readonly Guid _iconGuid = new("c0fdf9e4-d59a-4d33-a4c4-0c11a37f2292");

        public bool IsAdded => _iconAdded;

        public event Action? Click;
        public event Action? RightClick;
        public event Action<TrayMenuCommand>? MenuCommand;

        public TrayIconHost(string tooltip)
        {
            _tip = tooltip;
            _wndProcDelegate = WindowProc;

            var wc = new WNDCLASS
            {
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
                lpszClassName = "BrainrotTrayHost_" + Guid.NewGuid().ToString("N")
            };
            _classAtom = RegisterClass(ref wc);
            _hwnd = CreateWindowEx(
                WS_EX_TOOLWINDOW,
                _classAtom,
                "BrainrotTrayHost",
                WS_OVERLAPPED,
                0, 0, 0, 0,
                IntPtr.Zero, // top-level hidden window to ensure shell callbacks work from overflow
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);
            if (_hwnd == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create tray message window.");
            }
        }

        public bool AddIcon(IntPtr hIcon)
        {
            if (_iconAdded)
                return true;

            bool TryAddIcon()
            {
                NOTIFYICONDATA data = new()
                {
                    cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                    hWnd = _hwnd,
                    uID = 0, // GUID identifies icon
                    uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_GUID,
                    uCallbackMessage = WM_TRAY,
                    hIcon = hIcon,
                    szTip = _tip,
                    uTimeoutOrVersion = NOTIFYICON_VERSION_4,
                    guidItem = _iconGuid
                };

                if (!Shell_NotifyIcon(NIM_ADD, ref data))
                    return false;

                // Set modern version for better behavior on new Windows
                Shell_NotifyIcon(NIM_SETVERSION, ref data);
                return true;
            }

            _iconAdded = TryAddIcon();
            if (!_iconAdded)
            {
                // retry once with fallback icon
                IntPtr fallback = LoadIcon(IntPtr.Zero, new IntPtr(IDI_INFORMATION));
                if (fallback == IntPtr.Zero)
                {
                    fallback = LoadIcon(IntPtr.Zero, new IntPtr(IDI_APPLICATION));
                }
                hIcon = fallback != IntPtr.Zero ? fallback : hIcon;
                _iconAdded = TryAddIcon();
            }
            return _iconAdded;
        }

        private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_TRAY || msg == WM_CONTEXTMENU || msg == NIN_SELECT || msg == NIN_KEYSELECT)
            {
                int code = (int)lParam;
                if (msg == NIN_SELECT || msg == NIN_KEYSELECT || code == WM_LBUTTONUP)
                {
                    Click?.Invoke();
                }
                else if (code == WM_RBUTTONUP || msg == WM_CONTEXTMENU)
                {
                    RightClick?.Invoke();
                    var cmd = ShowContextMenu();
                    if (cmd != TrayMenuCommand.None)
                    {
                        MenuCommand?.Invoke(cmd);
                    }
                }
            }
            else if (msg == WM_DESTROY)
            {
                RemoveIcon();
            }

            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        private void RemoveIcon()
        {
            if (!_iconAdded) return;
            NOTIFYICONDATA data = new()
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 0,
                guidItem = _iconGuid,
                uFlags = NIF_GUID
            };
            Shell_NotifyIcon(NIM_DELETE, ref data);
            _iconAdded = false;
        }

        public void Dispose()
        {
            RemoveIcon();
            if (_hwnd != IntPtr.Zero)
            {
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
            if (_classAtom != 0)
            {
                UnregisterClass((IntPtr)_classAtom, IntPtr.Zero);
            }
        }

        #region Win32
        private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClass(ref WNDCLASS lpWndClass);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowEx(int dwExStyle, ushort lpClassName, string lpWindowName, int dwStyle,
            int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool UnregisterClass(IntPtr lpClassName, IntPtr hInstance);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll")]
        private static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool InsertMenu(IntPtr hMenu, int position, int flags, int itemId, string text);

        [DllImport("user32.dll")]
        private static extern int TrackPopupMenu(IntPtr hMenu, int flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASS
        {
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpszClassName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public int dwState;
            public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public uint uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public int dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        public static IntPtr GetAppIconHandle()
        {
            var exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            var hIcon = ExtractIcon(IntPtr.Zero, exe, 0);
            if (hIcon == IntPtr.Zero)
            {
                hIcon = LoadIcon(IntPtr.Zero, new IntPtr(IDI_INFORMATION));
                if (hIcon == IntPtr.Zero)
                {
                    hIcon = LoadIcon(IntPtr.Zero, new IntPtr(IDI_APPLICATION)); // final fallback
                }
            }
            return hIcon;
        }

        private TrayMenuCommand ShowContextMenu()
        {
            const int MF_STRING = 0x0000;
            const int TPM_RETURNCMD = 0x0100;
            const int TPM_RIGHTBUTTON = 0x0002;

            IntPtr hMenu = CreatePopupMenu();
            if (hMenu == IntPtr.Zero)
                return TrayMenuCommand.None;

            InsertMenu(hMenu, 0, MF_STRING, (int)TrayMenuCommand.Show, "Show");
            InsertMenu(hMenu, 1, MF_STRING, (int)TrayMenuCommand.Hide, "Hide");
            InsertMenu(hMenu, 2, MF_STRING, (int)TrayMenuCommand.Reposition, "Reposition");
            InsertMenu(hMenu, 3, MF_STRING, (int)TrayMenuCommand.ManualPosition, "Manual position");
            InsertMenu(hMenu, 4, MF_STRING, (int)TrayMenuCommand.Quit, "Quit");

            GetCursorPos(out POINT pt);
            User32.SetForegroundWindow(_hwnd);
            int cmd = TrackPopupMenu(hMenu, TPM_RETURNCMD | TPM_RIGHTBUTTON, pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
            DestroyMenu(hMenu);
            if (Enum.IsDefined(typeof(TrayMenuCommand), cmd))
            {
                return (TrayMenuCommand)cmd;
            }
            return TrayMenuCommand.None;
        }
        #endregion
    }

    internal enum TrayMenuCommand
    {
        None = 0,
        Show = 1,
        Hide = 2,
        Reposition = 3,
        ManualPosition = 4,
        Quit = 5
    }
}
