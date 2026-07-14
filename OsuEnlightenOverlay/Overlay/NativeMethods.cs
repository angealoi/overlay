using System;
using System.Runtime.InteropServices;

namespace OsuEnlightenOverlay.Overlay
{
    /// <summary>
    /// Win32 API — 메시지 큐 상태 확인 (Application.Idle 루프용).
    /// </summary>
    internal static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hWnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PeekMessage(out MSG msg, IntPtr hWnd, uint messageFilterMin, uint messageFilterMax, uint wRemoveMsg);

        public const uint PM_NOREMOVE = 0;

        /// <summary>
        /// 메시지 큐에 처리할 메시지가 없으면 true (Idle 상태).
        /// </summary>
        public static bool AppStillIdle()
        {
            MSG msg;
            return !PeekMessage(out msg, IntPtr.Zero, 0, 0, PM_NOREMOVE);
        }
    }
}