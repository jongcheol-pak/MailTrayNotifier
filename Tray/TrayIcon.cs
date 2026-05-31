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
    /// 컨텍스트 메뉴는 owner-draw(MF_OWNERDRAW)로 직접 그려 다크/라이트 테마를 적용한다.
    /// </summary>
    internal sealed class TrayIcon : IDisposable
    {
        // 트레이 콜백 메시지 (WM_APP + 1)
        private const uint WM_TRAYICON = 0x0400 + 1;
        private const uint WM_LBUTTONUP = 0x0202;
        private const uint WM_RBUTTONUP = 0x0205;
        private const uint WM_COMMAND = 0x0111;
        private const uint WM_NULL = 0x0000;
        private const uint WM_MEASUREITEM = 0x002C;
        private const uint WM_DRAWITEM = 0x002B;

        private const uint NIM_ADD = 0x0;
        private const uint NIM_MODIFY = 0x1;
        private const uint NIM_DELETE = 0x2;
        private const uint NIF_MESSAGE = 0x1;
        private const uint NIF_ICON = 0x2;
        private const uint NIF_TIP = 0x4;

        private const uint IMAGE_ICON = 1;
        private const uint LR_LOADFROMFILE = 0x10;
        private const uint LR_DEFAULTSIZE = 0x40;

        // 메뉴 플래그: 항목을 owner-draw로 추가(텍스트/배경을 직접 그림)
        private const uint MF_OWNERDRAW = 0x100;
        private const uint MF_ENABLED = 0x0;
        private const uint MF_GRAYED = 0x1;
        private const uint MF_DISABLED = 0x2;
        private const uint TPM_RIGHTBUTTON = 0x2;

        // DRAWITEMSTRUCT.itemState 비트
        private const uint ODS_SELECTED = 0x0001;
        private const uint ODS_GRAYED = 0x0002;
        private const uint ODS_DISABLED = 0x0004;

        // 시스템 메뉴 폰트 취득
        private const uint SPI_GETNONCLIENTMETRICS = 0x0029;
        private const int DEFAULT_GUI_FONT = 17;

        // GDI 텍스트 그리기
        private const int TRANSPARENT = 1;
        private const uint DT_LEFT = 0x0;
        private const uint DT_VCENTER = 0x4;
        private const uint DT_SINGLELINE = 0x20;

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

        // 메뉴 폰트 캐시 (DPI가 바뀌면 재생성). 스톡 폰트 폴백 시 DeleteObject 금지
        private nint _menuFont;
        private uint _menuFontDpi;
        private bool _menuFontIsStock;

        /// <summary>좌클릭 시 발생</summary>
        public event Action? LeftClicked;

        /// <summary>메뉴 항목 클릭 시 발생 (인자: 항목 Id)</summary>
        public event Action<int>? MenuItemClicked;

        /// <summary>
        /// 메뉴를 다크 색으로 그릴지 판정하는 콜백 (true=다크). null이면 라이트로 그린다.
        /// 앱의 실제 적용 테마(ActualTheme)를 우클릭 시점마다 조회하도록 App이 주입한다.
        /// </summary>
        public Func<bool>? IsDarkTheme { get; set; }

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

            // owner-draw 메뉴 측정/그리기 (메뉴 소유 창인 이 창으로 전송됨)
            if (msg == WM_MEASUREITEM)
            {
                return OnMeasureItem(lParam);
            }

            if (msg == WM_DRAWITEM)
            {
                return OnDrawItem(lParam);
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

                    // owner-draw: 클릭 command ID(uIDNewItem)와 그리기 식별자(itemData=lpNewItem) 모두 항목 Id를 전달.
                    // 구분선은 MF_DISABLED로 클릭/hover 불가 처리(그리기는 itemData로 식별).
                    var flags = item.IsSeparator
                        ? MF_OWNERDRAW | MF_DISABLED
                        : MF_OWNERDRAW | (item.IsEnabled ? MF_ENABLED : MF_GRAYED);
                    AppendMenu(menu, flags, new nint(item.Id), new nint(item.Id));
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

        /// <summary>
        /// WM_MEASUREITEM: 항목 크기 산출 (텍스트 폭/높이 + DPI 스케일 패딩, 구분선은 낮은 높이)
        /// </summary>
        private nint OnMeasureItem(nint lParam)
        {
            var mis = Marshal.PtrToStructure<MEASUREITEMSTRUCT>(lParam);
            var item = _menuItems.Find(m => m.Id == (int)mis.itemData);
            var scale = GetDpiForWindow(_hwnd) / 96.0;

            if (item is null)
            {
                return (nint)1;
            }

            if (item.IsSeparator)
            {
                mis.itemWidth = 0;
                mis.itemHeight = (uint)Math.Max(1, (int)(7 * scale));
            }
            else
            {
                var hdc = GetDC(_hwnd);
                if (hdc == nint.Zero)
                {
                    // DC 획득 실패 시(극히 드묾) 텍스트 측정 불가 → 안전한 기본 크기
                    mis.itemWidth = (uint)(120 * scale);
                    mis.itemHeight = (uint)(24 * scale);
                }
                else
                {
                    var font = GetMenuFont();
                    var old = SelectObject(hdc, font);
                    GetTextExtentPoint32(hdc, item.Text, item.Text.Length, out var sz);
                    SelectObject(hdc, old);
                    ReleaseDC(_hwnd, hdc);

                    // 좌우 패딩(텍스트 좌측 16 + 우측 여백), 상하 패딩
                    mis.itemWidth = (uint)(sz.cx + (int)(40 * scale));
                    mis.itemHeight = (uint)(sz.cy + (int)(10 * scale));
                }
            }

            Marshal.StructureToPtr(mis, lParam, false);
            return (nint)1;
        }

        /// <summary>
        /// WM_DRAWITEM: 배경/텍스트/구분선을 테마 색으로 직접 그린다.
        /// </summary>
        private nint OnDrawItem(nint lParam)
        {
            var dis = Marshal.PtrToStructure<DRAWITEMSTRUCT>(lParam);
            var item = _menuItems.Find(m => m.Id == (int)dis.itemData);
            if (item is null)
            {
                return (nint)1;
            }

            var dark = IsDarkTheme?.Invoke() ?? false;
            var c = GetColors(dark);
            var scale = GetDpiForWindow(_hwnd) / 96.0;

            var selected = (dis.itemState & ODS_SELECTED) != 0;
            var disabled = (dis.itemState & (ODS_GRAYED | ODS_DISABLED)) != 0;

            // 배경: 활성 상태에서 선택(hover)이면 hover색, 아니면 기본 배경색
            var bg = (selected && !disabled) ? c.Hover : c.Background;
            var bgBrush = CreateSolidBrush(bg);
            FillRect(dis.hDC, ref dis.rcItem, bgBrush);
            DeleteObject(bgBrush);

            if (item.IsSeparator)
            {
                // 중앙에 가는 구분선
                var midY = (dis.rcItem.top + dis.rcItem.bottom) / 2;
                var pad = (int)(8 * scale);
                var lineRect = new RECT
                {
                    left = dis.rcItem.left + pad,
                    top = midY,
                    right = dis.rcItem.right - pad,
                    bottom = midY + 1
                };
                var lineBrush = CreateSolidBrush(c.Separator);
                FillRect(dis.hDC, ref lineRect, lineBrush);
                DeleteObject(lineBrush);
            }
            else
            {
                var font = GetMenuFont();
                var old = SelectObject(dis.hDC, font);
                SetBkMode(dis.hDC, TRANSPARENT);
                SetTextColor(dis.hDC, disabled ? c.Grayed : c.Text);

                var textRect = dis.rcItem;
                textRect.left += (int)(16 * scale); // 좌측 패딩
                DrawText(dis.hDC, item.Text, item.Text.Length, ref textRect, DT_LEFT | DT_VCENTER | DT_SINGLELINE);

                SelectObject(dis.hDC, old);
            }

            return (nint)1;
        }

        /// <summary>
        /// 시스템 메뉴 폰트(lfMenuFont)를 취득해 캐시한다. 취득 실패 시 기본 GUI 폰트로 폴백.
        /// </summary>
        private nint GetMenuFont()
        {
            var dpi = GetDpiForWindow(_hwnd);
            if (_menuFont != nint.Zero && _menuFontDpi == dpi)
            {
                return _menuFont;
            }

            if (_menuFont != nint.Zero && !_menuFontIsStock)
            {
                DeleteObject(_menuFont);
            }
            _menuFont = nint.Zero;
            _menuFontIsStock = false;

            var ncm = new NONCLIENTMETRICS { cbSize = (uint)Marshal.SizeOf<NONCLIENTMETRICS>() };
            if (SystemParametersInfo(SPI_GETNONCLIENTMETRICS, ncm.cbSize, ref ncm, 0))
            {
                var lf = ncm.lfMenuFont;
                _menuFont = CreateFontIndirect(ref lf);
            }

            if (_menuFont == nint.Zero)
            {
                _menuFont = GetStockObject(DEFAULT_GUI_FONT);
                _menuFontIsStock = true;
            }

            _menuFontDpi = dpi;
            return _menuFont;
        }

        /// <summary>
        /// 테마별 메뉴 색 (COLORREF 0x00BBGGRR — 회색조라 RRGGBB와 동일)
        /// </summary>
        private static MenuColors GetColors(bool dark)
        {
            return dark
                ? new MenuColors(0x2B2B2B, 0xFFFFFF, 0x3D3D3D, 0x777777, 0x404040)
                : new MenuColors(0xFFFFFF, 0x1A1A1A, 0xE9E9E9, 0x999999, 0xE0E0E0);
        }

        private readonly struct MenuColors
        {
            public readonly uint Background;
            public readonly uint Text;
            public readonly uint Hover;
            public readonly uint Grayed;
            public readonly uint Separator;

            public MenuColors(uint background, uint text, uint hover, uint grayed, uint separator)
            {
                Background = background;
                Text = text;
                Hover = hover;
                Grayed = grayed;
                Separator = separator;
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

            // 메뉴 폰트 해제 (스톡 폰트는 해제 금지)
            if (_menuFont != nint.Zero && !_menuFontIsStock)
            {
                DeleteObject(_menuFont);
            }
            _menuFont = nint.Zero;

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

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {
            public int cx;
            public int cy;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEASUREITEMSTRUCT
        {
            public uint CtlType;
            public uint CtlID;
            public uint itemID;
            public uint itemWidth;
            public uint itemHeight;
            public nint itemData;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DRAWITEMSTRUCT
        {
            public uint CtlType;
            public uint CtlID;
            public uint itemID;
            public uint itemAction;
            public uint itemState;
            public nint hwndItem;
            public nint hDC;
            public RECT rcItem;
            public nint itemData;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct LOGFONT
        {
            public int lfHeight;
            public int lfWidth;
            public int lfEscapement;
            public int lfOrientation;
            public int lfWeight;
            public byte lfItalic;
            public byte lfUnderline;
            public byte lfStrikeOut;
            public byte lfCharSet;
            public byte lfOutPrecision;
            public byte lfClipPrecision;
            public byte lfQuality;
            public byte lfPitchAndFamily;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string lfFaceName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NONCLIENTMETRICS
        {
            public uint cbSize;
            public int iBorderWidth;
            public int iScrollWidth;
            public int iScrollHeight;
            public int iCaptionWidth;
            public int iCaptionHeight;
            public LOGFONT lfCaptionFont;
            public int iSmCaptionWidth;
            public int iSmCaptionHeight;
            public LOGFONT lfSmCaptionFont;
            public int iMenuWidth;
            public int iMenuHeight;
            public LOGFONT lfMenuFont;
            public LOGFONT lfStatusFont;
            public LOGFONT lfMessageFont;
            public int iPaddedBorderWidth;
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

        // MF_OWNERDRAW 항목용: lpNewItem이 문자열이 아니라 itemData 포인터로 해석됨
        [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)]
        private static extern bool AppendMenu(nint hMenu, uint uFlags, nint uIDNewItem, nint lpNewItem);

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

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(nint hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref NONCLIENTMETRICS pvParam, uint fWinIni);

        [DllImport("user32.dll")]
        private static extern nint GetDC(nint hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(nint hWnd, nint hDC);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int DrawText(nint hDC, string lpchText, int cchText, ref RECT lprc, uint format);

        [DllImport("user32.dll")]
        private static extern int FillRect(nint hDC, ref RECT lprc, nint hbr);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern nint CreateFontIndirect(ref LOGFONT lplf);

        [DllImport("gdi32.dll")]
        private static extern nint GetStockObject(int fnObject);

        [DllImport("gdi32.dll")]
        private static extern nint SelectObject(nint hDC, nint hgdiobj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(nint hObject);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetTextExtentPoint32(nint hDC, string lpString, int c, out SIZE psizl);

        [DllImport("gdi32.dll")]
        private static extern nint CreateSolidBrush(uint crColor);

        [DllImport("gdi32.dll")]
        private static extern uint SetTextColor(nint hDC, uint crColor);

        [DllImport("gdi32.dll")]
        private static extern int SetBkMode(nint hDC, int mode);

        #endregion
    }
}
