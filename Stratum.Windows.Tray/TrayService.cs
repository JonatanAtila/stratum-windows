// Stratum.Windows.Tray - system tray icon via raw Win32 (Shell_NotifyIcon)
// on a dedicated STA thread with its own message loop. Deterministic delivery
// of clicks/menus, no dependency on a hosted message pump.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Stratum.Windows.Tray
{
    public static class TrayService
    {
        public static event Action OpenRequested;
        public static event Action ExitRequested;

        private const int WM_APP_TRAY = 0x8001;
        private const int WM_APP_TRAY_QUIT = 0x8002;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_LBUTTONDBLCLK = 0x0203;

        private const int NIM_ADD = 0x0;
        private const int NIM_MODIFY = 0x1;
        private const int NIM_DELETE = 0x2;

        private const int NIF_MESSAGE = 0x1;
        private const int NIF_ICON = 0x2;
        private const int NIF_TIP = 0x4;

        private const int TPM_RETURNCMD = 0x100;
        private const int TPM_RIGHTBUTTON = 0x2;
        private const int TPM_NONOTIFY = 0x80;
        private const int MF_STRING = 0x0;
        private const int MF_SEPARATOR = 0x800;

        private const int SM_CXSMICON = 49;
        private const int IMAGE_ICON = 1;
        private const int LR_LOADFROMFILE = 0x10;

        private static IntPtr _hwnd = IntPtr.Zero;
        private static IntPtr _icon = IntPtr.Zero;
        private static string _tip = "Stratum";
        private static bool _visible;        private static Thread _thread;
        private static readonly ManualResetEventSlim _ready = new(false);
        private static WndProcDelegate _wndProcRef;

        public static void EnsureCreated(string iconPath)
        {
            if (_hwnd != IntPtr.Zero)
                return;

            _wndProcRef = WndProc;

            _thread = new Thread(() => MessageLoop(iconPath))
            {
                IsBackground = true,
                Name = "StratumTray"
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();

            _ready.Wait(TimeSpan.FromSeconds(5));
        }

        public static void SetVisible(bool visible)
        {
            if (_hwnd == IntPtr.Zero || visible == _visible)
                return;

            _visible = visible;

            if (visible)
                PostThreadMessage(NIM_ADD);
            else
                SendDelete();
        }

        public static void Dispose()
        {
            try
            {
                if (_hwnd != IntPtr.Zero)
                    PostMessage(_hwnd, WM_APP_TRAY_QUIT, IntPtr.Zero, IntPtr.Zero);
            }
            catch
            {
                // best-effort
            }
            finally
            {
                _hwnd = IntPtr.Zero;
                _visible = false;
            }
        }

        private static void PostThreadMessage(int action)
        {
            try
            {
                if (_hwnd != IntPtr.Zero)
                    SendNotifyIcon(action);
            }
            catch
            {
                // best-effort
            }
        }

        private static void SendDelete()
        {
            try
            {
                if (_hwnd == IntPtr.Zero)
                    return;

                var nid = new NOTIFYICONDATAW();
                nid.cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>();
                nid.hWnd = _hwnd;
                nid.uID = 1;
                Shell_NotifyIconW(NIM_DELETE, ref nid);
            }
            catch
            {
                // best-effort
            }
        }

        private static void SendNotifyIcon(int action)
        {
            var nid = new NOTIFYICONDATAW();
            nid.cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>();
            nid.hWnd = _hwnd;
            nid.uID = 1;
            nid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
            nid.uCallbackMessage = WM_APP_TRAY;
            nid.hIcon = _icon;
            nid.szTip = _tip;

            Shell_NotifyIconW(action, ref nid);
        }

        private static void MessageLoop(object state)
        {
            try
            {
                var iconPath = state as string;
                var size = GetSystemMetrics(SM_CXSMICON);

                if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                    _icon = LoadImageW(IntPtr.Zero, iconPath, IMAGE_ICON, size, size, LR_LOADFROMFILE);

                var module = GetModuleHandleW(null);
                var wc = new WNDCLASSW
                {
                    lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcRef),
                    hInstance = module,
                    lpszClassName = "StratumTrayWnd"
                };
                RegisterClassW(ref wc);

                // HWND_MESSAGE = -3
                _hwnd = CreateWindowExW(0, "StratumTrayWnd", "", 0, 0, 0, 0, 0,
                    new IntPtr(-3), IntPtr.Zero, module, IntPtr.Zero);

                _ready.Set();

                if (_hwnd == IntPtr.Zero)
                    return;

                MSG msg;
                while (GetMessageW(out msg, IntPtr.Zero, 0, 0))
                {
                    TranslateMessage(ref msg);
                    DispatchMessageW(ref msg);
                }

                SendDelete();

                if (_icon != IntPtr.Zero)
                {
                    DestroyIcon(_icon);
                    _icon = IntPtr.Zero;
                }
            }
            catch
            {
                _ready.Set();
            }
        }

        private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (msg == WM_APP_TRAY_QUIT)
                {
                    PostQuitMessage(0);
                    return IntPtr.Zero;
                }

                if (msg == WM_APP_TRAY)
                {
                    var evt = lParam.ToInt32();

                    if (evt == WM_LBUTTONDBLCLK)
                    {
                        OpenRequested?.Invoke();
                    }
                    else if (evt == WM_RBUTTONUP)
                    {
                        ShowMenu();
                    }

                    return IntPtr.Zero;
                }
            }
            catch
            {
                // nunca derruba o loop de mensagens
            }

            return DefWindowProcW(hWnd, msg, wParam, lParam);
        }

        private static void ShowMenu()
        {
            var menu = IntPtr.Zero;

            try
            {
                menu = CreatePopupMenu();
                AppendMenuW(menu, MF_STRING, new IntPtr(1), "Abrir Stratum");
                AppendMenuW(menu, MF_SEPARATOR, IntPtr.Zero, null);
                AppendMenuW(menu, MF_STRING, new IntPtr(2), "Sair");

                GetCursorPos(out var pt);
                SetForegroundWindow(_hwnd);

                var cmd = TrackPopupMenuEx(menu,
                    TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_NONOTIFY,
                    pt.X, pt.Y, _hwnd, IntPtr.Zero);

                PostMessage(_hwnd, 0x0000 /* WM_NULL */, IntPtr.Zero, IntPtr.Zero);

                if (cmd == 1)
                    OpenRequested?.Invoke();
                else if (cmd == 2)
                    ExitRequested?.Invoke();
            }
            catch
            {
                // best-effort
            }
            finally
            {
                if (menu != IntPtr.Zero)
                    DestroyMenu(menu);
            }
        }

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSW
        {
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATAW
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public uint uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hWnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX;
            public int ptY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClassW(ref WNDCLASSW lpWndClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowExW(int exStyle, string className, string windowName,
            int style, int x, int y, int width, int height, IntPtr parent,
            IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll")]
        private static extern bool GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessageW(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Shell_NotifyIconW(int dwMessage, ref NOTIFYICONDATAW lpData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadImageW(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool AppendMenuW(IntPtr hMenu, int uFlags, IntPtr uIDNewItem, string lpNewItem);

        [DllImport("user32.dll")]
        private static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hWnd, IntPtr lptpm);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern void PostQuitMessage(int nExitCode);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandleW(string lpModuleName);
    }
}

