using System;

namespace OsuEnlightenOverlay.Overlay
{
    /// <summary>
    /// 클릭 투과 토글 — WS_EX_TRANSPARENT 동적 제거/복원.
    /// 게임플레이 중: 클릭 투과 ON (osu! 입력 가능)
    /// HUD Edit Mode: 클릭 투과 OFF (UI 조작 가능)
    /// </summary>
    internal static class ClickThrough
    {
        /// <summary>
        /// 클릭 투과 활성화 (WS_EX_TRANSPARENT 추가).
        /// </summary>
        public static void Enable(IntPtr hwnd)
        {
            int exStyle = WindowInterop.GetWindowLong(hwnd, WindowInterop.GWL_EXSTYLE);
            WindowInterop.SetWindowLong(hwnd, WindowInterop.GWL_EXSTYLE,
                exStyle | WindowInterop.WS_EX_TRANSPARENT | WindowInterop.WS_EX_LAYERED);
            ApplyStyleChange(hwnd);
        }

        /// <summary>
        /// 클릭 투과 해제 (WS_EX_TRANSPARENT 제거).
        /// </summary>
        public static void Disable(IntPtr hwnd)
        {
            int exStyle = WindowInterop.GetWindowLong(hwnd, WindowInterop.GWL_EXSTYLE);
            WindowInterop.SetWindowLong(hwnd, WindowInterop.GWL_EXSTYLE,
                exStyle & ~WindowInterop.WS_EX_TRANSPARENT);
            ApplyStyleChange(hwnd);
        }

        /// <summary>
        /// 스타일 변경 즉시 적용.
        /// </summary>
        static void ApplyStyleChange(IntPtr hwnd)
        {
            WindowInterop.SetWindowPos(hwnd, WindowInterop.HWND_TOPMOST, 0, 0, 0, 0,
                WindowInterop.SWP_NOMOVE | WindowInterop.SWP_NOSIZE |
                WindowInterop.SWP_NOACTIVATE | WindowInterop.SWP_NOOWNERZORDER |
                WindowInterop.SWP_FRAMECHANGED);
        }

        /// <summary>
        /// 현재 클릭 투과 상태 확인.
        /// </summary>
        public static bool IsEnabled(IntPtr hwnd)
        {
            int exStyle = WindowInterop.GetWindowLong(hwnd, WindowInterop.GWL_EXSTYLE);
            return (exStyle & WindowInterop.WS_EX_TRANSPARENT) != 0;
        }
    }
}