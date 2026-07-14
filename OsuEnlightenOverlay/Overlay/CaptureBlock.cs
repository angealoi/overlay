using System;
using System.Runtime.InteropServices;

namespace OsuEnlightenOverlay.Overlay
{
    /// <summary>
    /// 캡처 차단 — SetWindowDisplayAffinity.
    /// WDA_EXCLUDEFROMCAPTURE 전용, WDA_MONITOR 폴백 절대 금지.
    /// </summary>
    internal static class CaptureBlock
    {
        /// <summary>
        /// 오버레이 창을 캡처에서 완전 제외.
        /// WDA_EXCLUDEFROMCAPTURE (0x11) — Windows 10 1903+.
        /// 뒤에 osu! 화면이 무조건 보여야 함.
        /// WDA_MONITOR 폴밭 시 검은 사각형 표시되어 치트 노출 → 절대 사용 금지.
        /// </summary>
        public static bool Enable(IntPtr hwnd)
        {
            bool success = WindowInterop.SetWindowDisplayAffinity(hwnd, WindowInterop.WDA_EXCLUDEFROMCAPTURE);
            if (!success)
            {
                // WDA_MONITOR 폴백 없이 재시도
                // 실패 원인: Windows 10 1903 미만 또는 권한 문제
                int err = Marshal.GetLastWin32Error();
                System.Diagnostics.Debug.WriteLine(
                    string.Format("[CaptureBlock] WDA_EXCLUDEFROMCAPTURE 실패 (err={0}). WDA_MONITOR 폴백 없음 — 재시도만 수행.", err));
            }
            return success;
        }

        /// <summary>
        /// 캡처 차단 해제.
        /// </summary>
        public static bool Disable(IntPtr hwnd)
        {
            return WindowInterop.SetWindowDisplayAffinity(hwnd, WindowInterop.WDA_NONE);
        }

        /// <summary>
        /// 현재 캡처 차단 상태 확인.
        /// </summary>
        public static uint GetAffinity(IntPtr hwnd)
        {
            uint affinity;
            WindowInterop.GetWindowDisplayAffinity(hwnd, out affinity);
            return affinity;
        }

        /// <summary>
        /// 캡처 차단이 활성화되어 있는지 확인.
        /// </summary>
        public static bool IsEnabled(IntPtr hwnd)
        {
            return GetAffinity(hwnd) == WindowInterop.WDA_EXCLUDEFROMCAPTURE;
        }
    }
}