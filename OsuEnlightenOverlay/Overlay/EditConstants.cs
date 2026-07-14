using System.Drawing;

namespace OsuEnlightenOverlay.Overlay
{
    /// <summary>
    /// HUD edit-layout 상수 — NEWNEWOVERLAY edit_constants.h 포팅.
    /// 색상/패딩/임계값/폰트 단계. C++ ARGB(0xAARRGGBB) → Color.FromArgb(a,r,g,b).
    /// </summary>
    internal static class EditConstants
    {
        // 각 HUD rect 주변 하이라이트 박스 패딩(px) + hit-test 확장.
        public const float HighlightPad = 6.0f;

        // 하이라이트 채움/외곽선. Selected = 포커스된 HUD; Unselected = edit 모드의 다른 가시 HUD.
        public static readonly Color FillSelected      = Color.FromArgb(0x33, 0xFF, 0xFF, 0x00); // 반투명 노랑
        public static readonly Color FillUnselected    = Color.FromArgb(0x22, 0x80, 0x80, 0x80); // 반투명 회색
        public static readonly Color OutlineSelected   = Color.FromArgb(0xFF, 0xFF, 0xFF, 0x00); // 노랑
        public static readonly Color OutlineUnselected = Color.FromArgb(0xFF, 0x80, 0x80, 0x80); // 회색

        // 중앙 스냅 가이드선: 드래그 중 + snap ON일 때 표시.
        public static readonly Color ColorSnapGuide = Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF); // 반투명 흰
        public const float SnapGuideThick = 1.0f;
        public const float SnapThreshold  = 6.0f;  // px — 이 범위 이내면 중앙으로 흡착

        // axis-lock 가이드선 색상.
        public static readonly Color ColorAxisLock = Color.FromArgb(0x80, 0x00, 0xFF, 0xFF); // 반투명 시안
        public const float AxisLockThick = 1.0f;

        // HUD 폰트 크기 조정 단계. Shift는 단계에 kShiftStepMult 곱.
        public const int FontSizeStep      = 1;
        public const int FontSizeShiftStep = 10;
        public const int FontSizeMin       = 8;
        public const int FontSizeMax       = 96;
    }
}
