using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Content;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Hosting;
using Windows.Graphics;
using System.Diagnostics;
using Brainrot.UI.Helpers;
using Brainrot.UI.Interop;
using Windows.Foundation;
using System.Timers;

namespace Brainrot.UI
{
    /// <summary>
    /// Lightweight taskbar widget host injected into the Shell_TrayWnd, inspired by AwqatSalaat.
    /// Uses a DesktopWindowXamlSource hosted inside a tiny native window parented to the taskbar.
    /// </summary>
    internal sealed class TaskbarWidget : IDisposable
    {
        private const string HostClassName = "BrainrotTaskbarWidgetHost";
        private const string PopupClassName = "BrainrotTaskbarPopupHost";
        
        private readonly DesktopWindowXamlSource _xamlSource;
        private readonly TaskbarWidgetCompact _compactView;
        private readonly IntPtr _hInstance;
        private readonly WndProcDelegate _wndProc;

        // Popup
        private DesktopWindowXamlSource? _popupXamlSource;
        private TaskbarPopupPanel? _popupView;
        private IntPtr _hwndPopup;
        private AppWindow? _popupAppWindow;
        private bool _popupVisible;
        private ushort _popupClassAtom;

        private IntPtr _hwndShell;
        private IntPtr _hwndHost;
        private AppWindow? _appWindow;
        private bool _initialized;
        private ushort _classAtom;
        private bool _isAttached;
        private SizeInt32 _size = new SizeInt32(70, 44); // Match Awqat-Salaat size
        private SizeInt32 _popupSize = new SizeInt32(260, 400);
        private const int WM_NCHITTEST = 0x0084;
        private const int HTTRANSPARENT = -1;
        
        // Dragging mode
        private bool _isDraggingMode;
        private int _savedOffsetX = -1;
        private MouseHook? _mouseHook;
        private bool _hookDragging;
        private int _hookDragStartX;
        
        // Z-order timer to keep widget on top
        private Timer? _zOrderTimer;
        
        public event Action? OpenMainWindowRequested;
        public event Action<int>? PositionChanged;

        public TaskbarWidget()
        {
            _xamlSource = new DesktopWindowXamlSource();
            _compactView = new TaskbarWidgetCompact();
            _hInstance = Kernel32.GetModuleHandle(null)!;
            _wndProc = HostWndProc;
            
            // Wire up compact view events
            _compactView.Clicked += OnCompactViewClicked;
            _compactView.DragDelta += OnDragDelta;
            _compactView.DragCompleted += OnDragCompleted;
            _compactView.SetDragEnabled(false); // dragging disabled by default
            
            Initialize();
        }
        
        private void OnCompactViewClicked()
        {
            if (_isDraggingMode)
                return;

            TogglePopup();
        }
        
        private void OnDragDelta(Point delta)
        {
            if (_hwndHost == IntPtr.Zero || _appWindow == null)
                return;
                
            var rect = SystemInfos.GetTaskBarBounds();
            User32.GetWindowRect(_hwndHost, out var currentRect);
            
            int newX = currentRect.left + (int)delta.X;
            int newY = currentRect.top + (int)delta.Y;
            
            // Constrain primarily to taskbar bounds; if invalid, use screen bounds
            bool rectValid = rect.right > rect.left && rect.bottom > rect.top;
            if (rectValid)
            {
                newX = Math.Max(rect.left, Math.Min(rect.right - _size.Width, newX));
                newY = rect.top + ((rect.bottom - rect.top - _size.Height) / 2);
            }
            else
            {
                int screenW = User32.GetSystemMetrics(User32.SM_CXSCREEN);
                int screenH = User32.GetSystemMetrics(User32.SM_CYSCREEN);
                newX = Math.Max(0, Math.Min(screenW - _size.Width, newX));
                newY = Math.Max(0, Math.Min(screenH - _size.Height, newY));
            }
            
            if (_isAttached)
            {
                int relX = newX - rect.left;
                int relY = (rect.bottom - rect.top - _size.Height) / 2;
                User32.SetWindowPos(_hwndHost, IntPtr.Zero, relX, relY, 0, 0, SWP.NOSIZE | SWP.NOACTIVATE | SWP.NOZORDER);
            }
            else
            {
                User32.SetWindowPos(_hwndHost, User32.HWND_TOPMOST, newX, newY, 0, 0, SWP.NOSIZE | SWP.NOACTIVATE);
            }
        }
        
        private void OnDragCompleted()
        {
            // Save the new position offset
            var rect = SystemInfos.GetTaskBarBounds();
            User32.GetWindowRect(_hwndHost, out var currentRect);
            bool rectValid = rect.right > rect.left;
            int screenW = User32.GetSystemMetrics(User32.SM_CXSCREEN);
            int rightEdge = rectValid ? rect.right : screenW;
            _savedOffsetX = rightEdge - currentRect.right;
            Debug.WriteLine($"[TaskbarWidget] Drag completed, offset from right: {_savedOffsetX}");
            
            // Notify position changed so it can be saved
            PositionChanged?.Invoke(_savedOffsetX);
            
            // Close popup if open
            if (_popupVisible)
            {
                HidePopup();
            }
            
            // End dragging mode if active
            if (_isDraggingMode)
            {
                EndDraggingMode();
            }
        }
        
        /// <summary>
        /// Start manual repositioning mode - widget becomes draggable via global mouse hook
        /// </summary>
        public void StartDraggingMode()
        {
            if (_isDraggingMode)
                return;
                
            _isDraggingMode = true;
            _hookDragging = false;
            _compactView.SetDragModeVisual(true); // Show visual feedback
            
            // Install global mouse hook
            _mouseHook = new MouseHook();
            _mouseHook.MouseDown += OnHookMouseDown;
            _mouseHook.MouseMove += OnHookMouseMove;
            _mouseHook.MouseUp += OnHookMouseUp;
            _mouseHook.Install();
            
            Debug.WriteLine("[TaskbarWidget] Started dragging mode with global mouse hook");
            
            // Bring widget to foreground
            BringToFront();
        }
        
        private void OnHookMouseDown(int x, int y)
        {
            if (!_isDraggingMode || _hwndHost == IntPtr.Zero)
                return;
                
            // Check if click is inside widget bounds
            User32.GetWindowRect(_hwndHost, out var rect);
            if (x >= rect.left && x <= rect.right && y >= rect.top && y <= rect.bottom)
            {
                _hookDragging = true;
                _hookDragStartX = x;
                Debug.WriteLine($"[TaskbarWidget] Hook drag started at {x}");
            }
        }
        
        private void OnHookMouseMove(int x, int y)
        {
            if (!_hookDragging || _hwndHost == IntPtr.Zero)
                return;
                
            int deltaX = x - _hookDragStartX;
            if (Math.Abs(deltaX) > 1)
            {
                OnDragDelta(new Point(deltaX, 0));
                _hookDragStartX = x;
            }
        }
        
        private void OnHookMouseUp(int x, int y)
        {
            if (!_hookDragging)
                return;
                
            _hookDragging = false;
            Debug.WriteLine("[TaskbarWidget] Hook drag completed");
            OnDragCompleted();
        }
        
        /// <summary>
        /// End manual repositioning mode
        /// </summary>
        public void EndDraggingMode()
        {
            if (!_isDraggingMode)
                return;
                
            _isDraggingMode = false;
            _hookDragging = false;
            _nativeDragging = false;
            
            // Uninstall mouse hook
            if (_mouseHook != null)
            {
                _mouseHook.MouseDown -= OnHookMouseDown;
                _mouseHook.MouseMove -= OnHookMouseMove;
                _mouseHook.MouseUp -= OnHookMouseUp;
                _mouseHook.Uninstall();
                _mouseHook.Dispose();
                _mouseHook = null;
            }
            
            _compactView.SetDragModeVisual(false); // Remove visual feedback
            Debug.WriteLine("[TaskbarWidget] Ended dragging mode");
        }
        
        /// <summary>
        /// Set the widget position from saved offset
        /// </summary>
        public void SetPositionOffset(int offsetFromRight)
        {
            _savedOffsetX = offsetFromRight;
        }
        
        /// <summary>
        /// Bring the widget to front (above taskbar)
        /// </summary>
        public void BringToFront()
        {
            if (_hwndHost == IntPtr.Zero)
                return;
                
            User32.SetWindowPos(_hwndHost, User32.HWND_TOPMOST, 0, 0, 0, 0, 
                SWP.NOMOVE | SWP.NOSIZE | SWP.NOACTIVATE | SWP.SHOWWINDOW);
        }
        
        private void StartZOrderTimer()
        {
            if (_zOrderTimer != null)
                return;
                
            _zOrderTimer = new Timer(50); // Check every 50ms for responsive z-order
            _zOrderTimer.Elapsed += (s, e) =>
            {
                if (_hwndHost == IntPtr.Zero)
                    return;
                    
                // Check if widget is still visible and on top
                if (!_popupVisible)
                {
                    // Get the window directly above the taskbar
                    var foreground = User32.GetForegroundWindow();
                    
                    // Always ensure widget stays on top, especially when taskbar is active
                    User32.SetWindowPos(_hwndHost, User32.HWND_TOPMOST, 0, 0, 0, 0,
                        SWP.NOMOVE | SWP.NOSIZE | SWP.NOACTIVATE | SWP.SHOWWINDOW);
                }
            };
            _zOrderTimer.Start();
        }
        
        private void StopZOrderTimer()
        {
            _zOrderTimer?.Stop();
            _zOrderTimer?.Dispose();
            _zOrderTimer = null;
        }

        public void Initialize()
        {
            if (_initialized)
                return;

            _hwndShell = User32.FindWindow("Shell_TrayWnd", null);
            RegisterHostWindowClass();

            // Create unattached host window first; XamlSource must initialize on an owned window
            _hwndHost = CreateHostWindow(_hwndShell);
            if (_hwndHost == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create taskbar widget host window.");
            }

            var id = global::Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwndHost);
            _appWindow = AppWindow.GetFromWindowId(id);
            if (_appWindow != null)
            {
                _appWindow.IsShownInSwitchers = false;
            }

            _xamlSource.Initialize(id);
            _xamlSource.SiteBridge.ResizePolicy = ContentSizePolicy.ResizeContentToParentWindow;
            // Wrap in Grid with transparent background (like Awqat-Salaat)
            var container = new Microsoft.UI.Xaml.Controls.Grid
            {
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent)
            };
            container.Children.Add(_compactView);
            _xamlSource.Content = container;

            // Inject widget as child of taskbar (like Awqat-Salaat does)
            InjectIntoTaskbar();
            
            _isAttached = true;
            MoveToTaskbarSlot(-1, log: true);
            ShowWidget();
            
            InitializePopup();

            _initialized = true;
            Debug.WriteLine("[TaskbarWidget] Initialization complete");
        }
        
        private void InjectIntoTaskbar()
        {
            Debug.WriteLine("[TaskbarWidget] Attempting to inject widget into taskbar");
            int attempts = 0;

            while (attempts++ <= 3)
            {
                Debug.WriteLine($"[TaskbarWidget] Attempt #{attempts} to inject the widget");
                var result = User32.SetParent(_hwndHost, _hwndShell);

                if (result != IntPtr.Zero)
                {
                    Debug.WriteLine("[TaskbarWidget] Widget injected successfully into taskbar");
                    return;
                }

                System.Threading.Thread.Sleep(500);
            }

            Debug.WriteLine("[TaskbarWidget] Failed to inject widget into taskbar, falling back to overlay mode");
            _isAttached = false;
        }
        
        private void InitializePopup()
        {
            RegisterPopupWindowClass();
            
            _hwndPopup = CreatePopupWindow();
            if (_hwndPopup == IntPtr.Zero)
            {
                Debug.WriteLine("[TaskbarWidget] Failed to create popup window");
                return;
            }
            
            var popupId = global::Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwndPopup);
            _popupAppWindow = AppWindow.GetFromWindowId(popupId);
            if (_popupAppWindow != null)
            {
                _popupAppWindow.IsShownInSwitchers = false;
            }
            
            _popupXamlSource = new DesktopWindowXamlSource();
            _popupXamlSource.Initialize(popupId);
            _popupXamlSource.SiteBridge.ResizePolicy = ContentSizePolicy.ResizeContentToParentWindow;
            
            _popupView = new TaskbarPopupPanel();
            _popupView.CloseRequested += () => HidePopup();
            _popupView.OpenMainRequested += () => OpenMainWindowRequested?.Invoke();
            _popupXamlSource.Content = _popupView;
            
            User32.ShowWindow(_hwndPopup, User32.SW_HIDE);
        }
        
        private void RegisterPopupWindowClass()
        {
            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                hInstance = _hInstance,
                lpszClassName = PopupClassName,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hCursor = User32.LoadCursor(IntPtr.Zero, User32.IDC_ARROW),
                hIcon = IntPtr.Zero,
                hbrBackground = IntPtr.Zero
            };

            _popupClassAtom = User32.RegisterClassEx(ref wc);
            if (_popupClassAtom == 0)
            {
                int err = Marshal.GetLastWin32Error();
                if (err != User32.ERROR_CLASS_ALREADY_EXISTS)
                {
                    Debug.WriteLine($"[TaskbarWidget] Failed to register popup class. Win32 error {err}.");
                }
            }
        }
        
        private IntPtr CreatePopupWindow()
        {
            int style = User32.WS_POPUP | User32.WS_CLIPSIBLINGS | User32.WS_CLIPCHILDREN;
            int exStyle = User32.WS_EX_NOACTIVATE | User32.WS_EX_TOOLWINDOW | User32.WS_EX_TOPMOST;

            IntPtr hwnd;
            if (_popupClassAtom != 0)
            {
                hwnd = User32.CreateWindowEx(
                    exStyle,
                    _popupClassAtom,
                    "BrainrotPopup",
                    style,
                    0, 0,
                    _popupSize.Width,
                    _popupSize.Height,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    _hInstance,
                    IntPtr.Zero);
            }
            else
            {
                hwnd = User32.CreateWindowEx(
                    exStyle,
                    PopupClassName,
                    "BrainrotPopup",
                    style,
                    0, 0,
                    _popupSize.Width,
                    _popupSize.Height,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    _hInstance,
                    IntPtr.Zero);
            }

            return hwnd;
        }
        
        private void TogglePopup()
        {
            if (_popupVisible)
            {
                HidePopup();
            }
            else
            {
                ShowPopup();
            }
        }
        
        private MouseHook? _popupCloseHook;
        
        private void ShowPopup()
        {
            if (_hwndPopup == IntPtr.Zero)
                return;
            
            // Position popup above/below the widget based on taskbar position
            var rect = SystemInfos.GetTaskBarBounds();
            User32.GetWindowRect(_hwndHost, out var widgetRect);
            
            int x = widgetRect.left + (_size.Width - _popupSize.Width) / 2;
            int y;
            
            // Determine if taskbar is at top or bottom
            int screenH = User32.GetSystemMetrics(User32.SM_CYSCREEN);
            if (rect.top < screenH / 2)
            {
                // Taskbar at top - show popup below
                y = widgetRect.bottom + 8;
            }
            else
            {
                // Taskbar at bottom - show popup above
                y = widgetRect.top - _popupSize.Height - 8;
            }
            
            // Keep on screen
            int screenW = User32.GetSystemMetrics(User32.SM_CXSCREEN);
            x = Math.Max(8, Math.Min(screenW - _popupSize.Width - 8, x));
            y = Math.Max(8, Math.Min(screenH - _popupSize.Height - 8, y));
            
            _popupAppWindow?.MoveAndResize(new RectInt32(x, y, _popupSize.Width, _popupSize.Height));
            User32.ShowWindow(_hwndPopup, User32.SW_SHOWNOACTIVATE);
            User32.SetWindowPos(_hwndPopup, User32.HWND_TOPMOST, x, y, _popupSize.Width, _popupSize.Height, SWP.NOACTIVATE | SWP.SHOWWINDOW);
            
            _popupVisible = true;
            _compactView.SetExpanded(true);
            
            // Install mouse hook to detect clicks outside popup
            InstallPopupCloseHook();
        }
        
        private void InstallPopupCloseHook()
        {
            if (_popupCloseHook != null)
                return;
                
            _popupCloseHook = new MouseHook();
            _popupCloseHook.MouseDown += OnPopupCloseMouseDown;
            _popupCloseHook.Install();
        }
        
        private void UninstallPopupCloseHook()
        {
            if (_popupCloseHook == null)
                return;
                
            _popupCloseHook.MouseDown -= OnPopupCloseMouseDown;
            _popupCloseHook.Uninstall();
            _popupCloseHook.Dispose();
            _popupCloseHook = null;
        }
        
        private void OnPopupCloseMouseDown(int x, int y)
        {
            if (!_popupVisible)
                return;
                
            // Check if click is inside widget
            User32.GetWindowRect(_hwndHost, out var widgetRect);
            if (x >= widgetRect.left && x <= widgetRect.right && y >= widgetRect.top && y <= widgetRect.bottom)
                return; // Click on widget - let it handle toggle
                
            // Check if click is inside popup
            User32.GetWindowRect(_hwndPopup, out var popupRect);
            if (x >= popupRect.left && x <= popupRect.right && y >= popupRect.top && y <= popupRect.bottom)
                return; // Click inside popup
                
            // Click outside - close popup
            HidePopup();
        }
        
        private void HidePopup()
        {
            if (_hwndPopup == IntPtr.Zero)
                return;
            
            UninstallPopupCloseHook();
            User32.ShowWindow(_hwndPopup, User32.SW_HIDE);
            _popupVisible = false;
            _compactView.SetExpanded(false);
        }

        public void UpdateSnapshot(string label, string emoji, string focus, string neutral, string rot, double focusPct)
        {
            _compactView.Update(label, emoji, focusPct);
            _popupView?.Update(label, emoji, focus, neutral, rot, focusPct);
        }

        public void MoveToTaskbarSlot(int offsetFromRight = -1, bool log = false)
        {
            if (_appWindow == null || _hwndHost == IntPtr.Zero)
                return;

            var rect = SystemInfos.GetTaskBarBounds();
            int width = _size.Width;
            int taskbarHeight = Math.Max(0, rect.bottom - rect.top);
            int height = _size.Height;
            if (taskbarHeight > 0)
            {
                // Clamp to fit inside the bar so it isn't clipped
                height = Math.Max(24, Math.Min(taskbarHeight - 4, height));
            }
            int screenW = User32.GetSystemMetrics(User32.SM_CXSCREEN);
            int screenH = User32.GetSystemMetrics(User32.SM_CYSCREEN);
            int x = screenW - width - 12;
            int y = rect.top;

            // Handle left/right/top taskbars; if rect invalid, fall back
            bool rectValid = rect.right > rect.left && rect.bottom > rect.top;
            if (rectValid)
            {
                bool isVertical = (rect.bottom - rect.top) > (rect.right - rect.left);
                if (!isVertical)
                {
                    // bottom or top taskbar
                    x = rect.right - width - (offsetFromRight >= 0 ? offsetFromRight : 12);
                    y = rect.top + ((taskbarHeight - height) / 2);
                }
                else
                {
                    // left or right taskbar
                    y = rect.top + ((taskbarHeight - height) / 2);
                    x = rect.left + 8;
                }
            }
            else
            {
                // Fallback: place near bottom-right of primary display
                x = Math.Max(0, screenW - width - 20);
                y = Math.Max(0, screenH - height - 80);
            }

            // When parented to taskbar, use relative coordinates
            if (_isAttached)
            {
                int relX = x - rect.left;
                int relY = (taskbarHeight - height) / 2;
                _appWindow?.MoveAndResize(new RectInt32(relX, relY, width, height));
                User32.SetWindowPos(_hwndHost, IntPtr.Zero, relX, relY, width, height, SWP.NOACTIVATE | SWP.NOZORDER | SWP.SHOWWINDOW);
            }
            else
            {
                _appWindow?.MoveAndResize(new RectInt32(x, y, width, height));
                User32.SetWindowPos(_hwndHost, User32.HWND_TOPMOST, x, y, width, height, SWP.NOACTIVATE | SWP.SHOWWINDOW);
            }

            if (log)
            {
                System.Diagnostics.Debug.WriteLine($"[TaskbarWidget] MoveToTaskbarSlot -> x:{x}, y:{y}, w:{width}, h:{height}, attached:{_isAttached}, rect:({rect.left},{rect.top},{rect.right},{rect.bottom})");
            }
        }

        public void ShowWidget()
        {
            if (_hwndHost == IntPtr.Zero)
                return;

            _appWindow?.Show(false);
            
            // Use saved position if available
            int offset = _savedOffsetX > 0 ? _savedOffsetX : -1;
            MoveToTaskbarSlot(offset, log: true);
            
            User32.ShowWindow(_hwndHost, User32.SW_SHOWNOACTIVATE);
            
            // When parented to taskbar, don't need z-order management
            if (_isAttached)
            {
                Debug.WriteLine($"[TaskbarWidget] ShowWidget called (attached mode), hwnd={_hwndHost}, offset={offset}");
            }
            else
            {
                User32.SetWindowPos(_hwndHost, User32.HWND_TOPMOST, 0, 0, 0, 0, SWP.NOMOVE | SWP.NOSIZE | SWP.NOACTIVATE | SWP.SHOWWINDOW);
                // Start timer to keep widget on top of taskbar (only needed in overlay mode)
                StartZOrderTimer();
                Debug.WriteLine($"[TaskbarWidget] ShowWidget called (overlay mode), hwnd={_hwndHost}, offset={offset}");
            }
        }

        public void HideWidget()
        {
            if (_hwndHost == IntPtr.Zero)
                return;
            User32.ShowWindow(_hwndHost, User32.SW_HIDE);
        }

        // Attachment to taskbar removed; overlay mode only

        private void RegisterHostWindowClass()
        {
            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                hInstance = _hInstance,
                lpszClassName = HostClassName,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hCursor = User32.LoadCursor(IntPtr.Zero, User32.IDC_ARROW),
                hIcon = IntPtr.Zero,
                hbrBackground = IntPtr.Zero
            };

            _classAtom = User32.RegisterClassEx(ref wc);
            if (_classAtom == 0)
            {
                int err = Marshal.GetLastWin32Error();
                if (err != User32.ERROR_CLASS_ALREADY_EXISTS)
                {
                    throw new InvalidOperationException($"Failed to register widget window class. Win32 error {err}.");
                }
                // Use the class name without atom on already-exists
            }
        }

        private IntPtr CreateHostWindow(IntPtr parent)
        {
            // Create as popup with layered style - will be parented to taskbar
            int style = User32.WS_POPUP | User32.WS_CLIPSIBLINGS | User32.WS_CLIPCHILDREN | User32.WS_VISIBLE;
            int exStyle = User32.WS_EX_LAYERED | User32.WS_EX_NOACTIVATE | User32.WS_EX_TOOLWINDOW;
            IntPtr parentHwnd = IntPtr.Zero;

            IntPtr hwnd;
            if (_classAtom != 0)
            {
                hwnd = User32.CreateWindowEx(
                    exStyle,
                    _classAtom,
                    "BrainrotWidgetHost",
                    style,
                    0, 0,
                    _size.Width,
                    _size.Height,
                    parentHwnd,
                    IntPtr.Zero,
                    _hInstance,
                    IntPtr.Zero);
            }
            else
            {
                hwnd = User32.CreateWindowEx(
                    exStyle,
                    HostClassName,
                    "BrainrotWidgetHost",
                    style,
                    0, 0,
                    _size.Width,
                    _size.Height,
                    parentHwnd,
                    IntPtr.Zero,
                    _hInstance,
                    IntPtr.Zero);
            }

            if (hwnd == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                Debug.WriteLine($"[TaskbarWidget] CreateWindowEx failed. Win32 error={err}, parent={parentHwnd}");
            }
            else if (parentHwnd != IntPtr.Zero)
            {
                User32.SetParent(hwnd, parentHwnd);
            }

            return hwnd;
        }

        private bool _nativeDragging = false;
        private int _dragStartX = 0;
        private int _dragStartY = 0;
        
        private IntPtr HostWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            const int WM_LBUTTONDOWN = 0x0201;
            const int WM_MOUSEMOVE = 0x0200;
            const int WM_LBUTTONUP = 0x0202;
            
            // Handle dragging at native window level when in drag mode
            if (_isDraggingMode)
            {
                if (msg == WM_LBUTTONDOWN)
                {
                    _nativeDragging = true;
                    _dragStartX = (short)(lParam.ToInt32() & 0xFFFF);
                    _dragStartY = (short)((lParam.ToInt32() >> 16) & 0xFFFF);
                    
                    // Convert to screen coordinates
                    User32.GetWindowRect(_hwndHost, out var rect);
                    _dragStartX += rect.left;
                    _dragStartY += rect.top;
                    
                    User32.SetCapture(_hwndHost);
                    Debug.WriteLine($"[TaskbarWidget] Native drag started at {_dragStartX}, {_dragStartY}");
                    return IntPtr.Zero;
                }
                else if (msg == WM_MOUSEMOVE && _nativeDragging)
                {
                    int x = (short)(lParam.ToInt32() & 0xFFFF);
                    int y = (short)((lParam.ToInt32() >> 16) & 0xFFFF);
                    
                    // Convert to screen coordinates
                    User32.GetWindowRect(_hwndHost, out var rect);
                    x += rect.left;
                    y += rect.top;
                    
                    int deltaX = x - _dragStartX;
                    int deltaY = 0; // Restrict to horizontal only
                    
                    if (Math.Abs(deltaX) > 2)
                    {
                        OnDragDelta(new Point(deltaX, deltaY));
                        _dragStartX = x;
                        _dragStartY = y;
                    }
                    
                    return IntPtr.Zero;
                }
                else if (msg == WM_LBUTTONUP)
                {
                    if (_nativeDragging)
                    {
                        _nativeDragging = false;
                        User32.ReleaseCapture();
                        OnDragCompleted();
                        Debug.WriteLine("[TaskbarWidget] Native drag completed");
                    }
                    return IntPtr.Zero;
                }
            }
            
            return User32.DefWindowProc(hWnd, msg, wParam, lParam);
        }

        public void Dispose()
        {
            // Clean up mouse hooks
            try
            {
                _mouseHook?.Dispose();
                _mouseHook = null;
                _popupCloseHook?.Dispose();
                _popupCloseHook = null;
            }
            catch { }
            
            // Clean up z-order timer
            try
            {
                StopZOrderTimer();
            }
            catch { }
            
            try
            {
                _popupXamlSource?.Dispose();
            }
            catch { }

            try
            {
                _xamlSource.Dispose();
            }
            catch { }

            try
            {
                _appWindow?.Hide();
                _popupAppWindow?.Hide();
            }
            catch { }

            if (_hwndPopup != IntPtr.Zero)
            {
                User32.DestroyWindow(_hwndPopup);
                _hwndPopup = IntPtr.Zero;
            }

            if (_hwndHost != IntPtr.Zero)
            {
                User32.DestroyWindow(_hwndHost);
                _hwndHost = IntPtr.Zero;
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    }

    internal static class Kernel32
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string? lpModuleName);
    }
}
