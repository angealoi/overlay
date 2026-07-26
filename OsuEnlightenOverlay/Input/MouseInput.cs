using System;
using System.Runtime.InteropServices;

namespace OsuEnlightenOverlay.Input
{
    /// <summary>
    /// SendInput absolute mouse — lame input::move_absolute_virtual_desktop 포팅.
    /// inject 태그는 MouseHook이 재귀하지 않도록 구분한다.
    /// </summary>
    internal static class MouseInput
    {
        public const uint InjectExtraInfo = 0xDEADC0DE;

        const int INPUT_MOUSE = 0;
        const uint MOUSEEVENTF_MOVE = 0x0001;
        const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
        const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;

        const int SM_XVIRTUALSCREEN = 76;
        const int SM_YVIRTUALSCREEN = 77;
        const int SM_CXVIRTUALSCREEN = 78;
        const int SM_CYVIRTUALSCREEN = 79;

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public int type;
            public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        static extern bool SetCursorPos(int x, int y);

        static readonly int InputSize = Marshal.SizeOf(typeof(INPUT));

        static int _vdOriginX, _vdOriginY, _vdWidth, _vdHeight;
        static bool _vdValid;

        public static void InvalidateVirtualDesktop()
        {
            _vdValid = false;
        }

        static void RefreshVirtualDesktop()
        {
            _vdOriginX = GetSystemMetrics(SM_XVIRTUALSCREEN);
            _vdOriginY = GetSystemMetrics(SM_YVIRTUALSCREEN);
            _vdWidth = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            _vdHeight = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            _vdValid = _vdWidth > 0 && _vdHeight > 0;
        }

        public static bool MoveAbsoluteVirtualDesktop(int screenX, int screenY)
        {
            if (!_vdValid)
                RefreshVirtualDesktop();
            if (!_vdValid)
                return SetCursorPos(screenX, screenY);

            if (SendAbsolute(screenX, screenY))
                return true;

            InvalidateVirtualDesktop();
            RefreshVirtualDesktop();
            if (_vdValid && SendAbsolute(screenX, screenY))
                return true;

            return SetCursorPos(screenX, screenY);
        }

        static bool SendAbsolute(int sx, int sy)
        {
            double denomX = _vdWidth > 1 ? (_vdWidth - 1) : 1.0;
            double denomY = _vdHeight > 1 ? (_vdHeight - 1) : 1.0;
            int normX = (int)(((sx - _vdOriginX) * 65535.0) / denomX + 0.5);
            int normY = (int)(((sy - _vdOriginY) * 65535.0) / denomY + 0.5);

            var input = new INPUT
            {
                type = INPUT_MOUSE,
                mi = new MOUSEINPUT
                {
                    dx = normX,
                    dy = normY,
                    mouseData = 0,
                    dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK,
                    time = 0,
                    dwExtraInfo = new UIntPtr(InjectExtraInfo)
                }
            };
            return SendInput(1, new[] { input }, InputSize) != 0;
        }
    }
}
