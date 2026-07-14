using System;
using System.Runtime.InteropServices;

namespace OsuEnlightenOverlay.Overlay
{
    /// <summary>
    /// Win32 API P/Invoke — 창 관리, 캡처 차단, 클릭 투과.
    /// </summary>
    internal static class WindowInterop
    {
        // ── 창 스타일 ──
        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int GWL_EXSTYLE = -20;

        public const int SWP_NOMOVE = 0x0002;
        public const int SWP_NOSIZE = 0x0001;
        public const int SWP_NOACTIVATE = 0x0010;
        public const int SWP_NOOWNERZORDER = 0x0200;
        public const int SWP_FRAMECHANGED = 0x0020;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        // 마우스 버튼 가상 키 코드 — CursorExpand용
        public const int VK_LBUTTON = 0x01;
        public const int VK_RBUTTON = 0x02;

        // ── edit mode 단축키 가상키 ──
        public const int VK_SHIFT   = 0x10;
        public const int VK_TAB     = 0x09;
        public const int VK_UP      = 0x26;
        public const int VK_DOWN    = 0x28;
        public const int VK_ESCAPE  = 0x1B;

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        // ── edit mode 마우스 입력 ──
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetCursorPos(out POINT lpPoint);

        // POINT 배열을 한 창의 좌표공간에서 다른 창으로 변환 (화면↔클라이언트 포함).
        [DllImport("user32.dll", SetLastError = true)]
        public static extern long MapWindowPoints(IntPtr hWndFrom, IntPtr hWndTo, ref POINT lpPoints, int cPoints);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetCapture(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetCursor(IntPtr hCursor);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

        // 시스템 커서 ID — IDC_SIZEALL (크기 조정 화살표)
        public const int IDC_SIZEALL = 32649;
        public static readonly IntPtr IDC_SIZEALL_Handle = LoadCursor(IntPtr.Zero, IDC_SIZEALL);

        /// <summary>LParam에서 signed x 좌표 추출 (GET_X_LPARAM 대용).</summary>
        public static int GetXLParam(IntPtr lParam)
        {
            return (short)(lParam.ToInt64() & 0xFFFF);
        }

        /// <summary>LParam에서 signed y 좌표 추출 (GET_Y_LPARAM 대용).</summary>
        public static int GetYLParam(IntPtr lParam)
        {
            return (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        }

        public const int LWA_ALPHA = 0x2;
        public const int LWA_COLORKEY = 0x1;

        // ── SetWindowDisplayAffinity ──
        public const uint WDA_NONE = 0x00000000;
        public const uint WDA_MONITOR = 0x00000001;
        public const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint pdwAffinity);

        // ── 창 스타일 ──
        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // ── 타이머 해상도 — winmm ──
        [DllImport("winmm.dll", SetLastError = true)]
        public static extern uint timeBeginPeriod(uint uPeriod);

        [DllImport("winmm.dll", SetLastError = true)]
        public static extern uint timeEndPeriod(uint uPeriod);

        // ── PID → HWND ──
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        // ── DWM ──
        [DllImport("dwmapi.dll", SetLastError = true)]
        public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

        [DllImport("dwmapi.dll", SetLastError = true)]
        public static extern int DwmIsCompositionEnabled(out bool pfEnabled);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
            public int Width { get { return Right - Left; } }
            public int Height { get { return Bottom - Top; } }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MARGINS
        {
            public int Left, Right, Top, Bottom;
        }

        // ── 가상키 ──
        public const uint VK_F9 = 0x78;
        public const uint VK_F10 = 0x79;
        public const uint MOD_NONE = 0x0000;

        // ── ShowWindow 명령 ──
        public const int SW_HIDE = 0;
        public const int SW_SHOWNOACTIVATE = 4;
        public const int SW_SHOWNA = 8;

        // ── WM_HOTKEY ──
        public const int WM_HOTKEY = 0x0312;
        public const int WM_NCHITTEST = 0x0084;
        public const int HTTRANSPARENT = -1;
        public const int HTCLIENT = 1;

        // ── edit mode 마우스 메시지 ──
        public const int WM_SETCURSOR = 0x0020;
        public const int WM_MOUSEACTIVATE = 0x0021;
        public const int WM_MOUSEMOVE = 0x0200;
        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_LBUTTONUP = 0x0202;
        public const int MA_NOACTIVATE = 3;
        public const int HTCAPTION = 2;

        /// <summary>
        /// 오버레이 창의 확장 스타일 설정.
        /// TOPMOST | NOACTIVATE | TOOLWINDOW | TRANSPARENT | LAYERED
        /// </summary>
        public static int OverlayExStyle
        {
            get { return WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_LAYERED; }
        }

        /// <summary>
        /// PID로 osu! 창 HWND 찾기.
        /// 창 제목은 곡에 따라 바뀌므로 신뢰할 수 없음.
        /// PID는 실행 파일(osu!.exe)에서 추출하므로 안정적.
        /// </summary>
        public static IntPtr FindOsuWindow(int processId)
        {
            IntPtr foundHwnd = IntPtr.Zero;
            uint targetPid = (uint)processId;

            EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                uint pid;
                GetWindowThreadProcessId(hWnd, out pid);
                if (pid == targetPid && IsWindowVisible(hWnd))
                {
                    // 가장 먼저 발견된 가시 창 사용
                    foundHwnd = hWnd;
                    return false; // 열거 중단
                }
                return true; // 계속
            }, IntPtr.Zero);

            return foundHwnd;
        }
    }
}