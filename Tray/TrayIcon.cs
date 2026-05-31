using System.Runtime.InteropServices;

namespace MailTrayNotifier.WinUI.Tray
{
    /// <summary>
    /// 트레이 컨텍스트 메뉴 항목
    /// </summary>
    internal sealed class TrayMenuItem
    {
        public int Id { get; init; }
        public string Text { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
        public bool IsVisible { get; set; } = true;
        public bool IsSeparator { get; init; }
    }

    /// <summary>
    /// Win32 Shell_NotifyIcon 기반 트레이 아이콘 (외부 라이브러리 미사용).
    /// 숨은 메시지 전용 창을 만들어 트레이 콜백/메뉴 명령을 처리한다.
    /// </summary>
    internal sealed class TrayIcon : IDisposable
    {
        // 트레이 콜백 메시지 (WM_APP + 1)
        private const uint WM_TRAYICON = 0x0400 + 1;
        private const uint WM_LBUTTONUP = 0x0202;
        private const uint WM_RBUTTONUP = 0x0205;
        private const uint WM_COMMAND = 0x0111;
        private const uint WM_NULL = 0x0000;

        private const uint NIM_ADD = 0x0;
        private const uint NIM_MODIFY = 0x1;
        private const uint NIM_DELETE = 0x2;
        private const uint NIF_MESSAGE = 0x1;
        private const uint NIF_ICON = 0x2;
        private const uint NIF_TIP = 0x4;

        private const uint IMAGE_ICON = 1;
        private const uint LR_LOADFROMFILE = 0x10;
        private const uint LR_DEFAULTSIZE = 0x40;

        private const uint MF_STRING = 0x0;
        private const uint MF_SEPARATOR = 0x800;
        private const uint MF_ENABLED = 0x0;
        private const uint MF_GRAYED = 0x1;
        private const uint TPM_RIGHTBUTTON = 0x2;

        private static readonly nint HWND_MESSAGE = new(-3);
        private const uint TrayUid = 1;

        private readonly WndProcDelegate _wndProcDelegate;
        private readonly List<TrayMenuItem> _menuItems = new();
        // 경로별 HICON 캐시 (아이콘은 3종 고정 — 상태 변경마다 디스크 재로딩 방지)
        private readonly Dictionary<string, nint> _iconCache = new();
        private readonly string _className;
        private nint _hwnd;
        private bool _added;
        private bool _disposed;

        /// <summary>좌클릭 시 발생</summary>
        public event Action? LeftClicked;

        /// <summary>메뉴 항목 클릭 시 발생 (인자: 항목 Id)</summary>
        public event Action<int>? MenuItemClicked;

        public TrayIcon()
        {
            _wndProcDelegate = WndProc;
            _className = "MailTrayNotifierTrayWindow";
        }

        /// <summary>
        /// 메시지 창 생성 및 트레이 아이콘 등록
        /// </summary>
        public void Create(string toolTip)
        {
            var hInstance = GetModuleHandle(null);

            var wc = new WNDCLASS
            {
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
                hInstance = hInstance,
                lpszClassName = _className
            };
            RegisterClass(ref wc);

            _hwnd = CreateWindowEx(0, _className, string.Empty, 0, 0, 0, 0, 0,
                HWND_MESSAGE, nint.Zero, hInstance, nint.Zero);

            var data = CreateData(NIF_MESSAGE | NIF_TIP, toolTip);
            Shell_NotifyIcon(NIM_ADD, ref data);
            _added = true;
        }

        /// <summary>
        /// 트레이 아이콘 변경 (.ico 파일 경로)
        /// </summary>
        public void SetIcon(string icoPath)
        {
            if (_disposed || string.IsNullOrEmpty(icoPath))
            {
                return;
            }

            // 캐시에 없으면 1회만 디스크에서 로드 (이후 상태 변경 시 재사용)
            if (!_iconCache.TryGetValue(icoPath, out var hIcon))
            {
                hIcon = LoadImage(nint.Zero, icoPath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
                if (hIcon == nint.Zero)
                {
                    return;
                }
                _iconCache[icoPath] = hIcon;
            }

            var data = CreateData(NIF_ICON, string.Empty);
            data.hIcon = hIcon;
            Shell_NotifyIcon(NIM_MODIFY, ref data);
        }

        /// <summary>
        /// 툴팁 텍스트 변경
        /// </summary>
        public void SetToolTip(string toolTip)
        {
            if (_disposed)
            {
                return;
            }

            var data = CreateData(NIF_TIP, toolTip);
            Shell_NotifyIcon(NIM_MODIFY, ref data);
        }

        /// <summary>
        /// 메뉴 항목 등록 (구성은 1회, 이후 갱신은 UpdateMenuItem)
        /// </summary>
        public void AddMenuItem(TrayMenuItem item) => _menuItems.Add(item);

        /// <summary>
        /// 메뉴 항목 갱신 (텍스트/활성/표시)
        /// </summary>
        public void UpdateMenuItem(int id, string? text = null, bool? isEnabled = null, bool? isVisible = null)
        {
            var item = _menuItems.Find(m => m.Id == id);
            if (item is null)
            {
                return;
            }

            if (text is not null) item.Text = text;
            if (isEnabled is not null) item.IsEnabled = isEnabled.Value;
            if (isVisible is not null) item.IsVisible = isVisible.Value;
        }

        private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
        {
            if (msg == WM_TRAYICON)
            {
                var mouseMsg = (uint)((long)lParam & 0xFFFF);
                if (mouseMsg == WM_LBUTTONUP)
                {
                    LeftClicked?.Invoke();
                }
                else if (mouseMsg == WM_RBUTTONUP)
                {
                    ShowContextMenu();
                }
                return nint.Zero;
            }

            if (msg == WM_COMMAND)
            {
                var id = (int)((long)wParam & 0xFFFF);
                MenuItemClicked?.Invoke(id);
                return nint.Zero;
            }

            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        private void ShowContextMenu()
        {
            var menu = CreatePopupMenu();
            if (menu == nint.Zero)
            {
                return;
            }

            try
            {
                foreach (var item in _menuItems)
                {
                    if (!item.IsVisible)
                    {
                        continue;
                    }

                    if (item.IsSeparator)
                    {
                        AppendMenu(menu, MF_SEPARATOR, nint.Zero, null);
                    }
                    else
                    {
                        var flags = MF_STRING | (item.IsEnabled ? MF_ENABLED : MF_GRAYED);
                        AppendMenu(menu, flags, new nint(item.Id), item.Text);
                    }
                }

                GetCursorPos(out var pt);

                // TrackPopupMenu 관용: 메뉴 밖 클릭 시 즉시 닫히도록 포그라운드 설정
                SetForegroundWindow(_hwnd);
                TrackPopupMenu(menu, TPM_RIGHTBUTTON, pt.X, pt.Y, 0, _hwnd, nint.Zero);
                PostMessage(_hwnd, WM_NULL, nint.Zero, nint.Zero);
            }
            finally
            {
                DestroyMenu(menu);
            }
        }

        private NOTIFYICONDATA CreateData(uint flags, string tip)
        {
            return new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = TrayUid,
                uFlags = flags,
                uCallbackMessage = WM_TRAYICON,
                szTip = tip ?? string.Empty,
                szInfo = string.Empty,
                szInfoTitle = string.Empty
            };
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            if (_added)
            {
                var data = CreateData(0, string.Empty);
                Shell_NotifyIcon(NIM_DELETE, ref data);
                _added = false;
            }

            // 캐시된 모든 아이콘 핸들 해제
            foreach (var hIcon in _iconCache.Values)
            {
                DestroyIcon(hIcon);
            }
            _iconCache.Clear();

            if (_hwnd != nint.Zero)
            {
                DestroyWindow(_hwnd);
                _hwnd = nint.Zero;
            }
        }

        #region Win32 Interop

        private delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASS
        {
            public uint style;
            public nint lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public nint hInstance;
            public nint hIcon;
            public nint hCursor;
            public nint hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public uint cbSize;
            public nint hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public nint hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public uint uVersionOrTimeout;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public nint hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClass(ref WNDCLASS lpWndClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName,
            uint dwStyle, int x, int y, int nWidth, int nHeight, nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(nint hWnd);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern nint GetModuleHandle(string? lpModuleName);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint LoadImage(nint hInst, string name, uint type, int cx, int cy, uint fuLoad);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(nint hIcon);

        [DllImport("user32.dll")]
        private static extern nint CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool AppendMenu(nint hMenu, uint uFlags, nint uIDNewItem, string? lpNewItem);

        [DllImport("user32.dll")]
        private static extern bool TrackPopupMenu(nint hMenu, uint uFlags, int x, int y, int nReserved, nint hWnd, nint prcRect);

        [DllImport("user32.dll")]
        private static extern bool DestroyMenu(nint hMenu);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(nint hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

        #endregion
    }
}
