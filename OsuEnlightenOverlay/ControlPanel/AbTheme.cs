using System.Drawing;
using System.Drawing.Drawing2D;

namespace OsuEnlightenOverlay.ControlPanel
{
    /// <summary>
    /// AByteCheat 원본 색상 팔레트 — Controls.cpp / GUI.cpp 상단의 #define 매크로를 1:1로 이식.
    /// 모든 색은 AByteCheat에서 직접 그린 VGUI 메뉴와 동일한 RGB값을 가진다.
    ///
    /// MenuColor(액센트)는 AByteCheat에서 사용자 지정 컬러셀렉터로 변경 가능했으나,
    /// 본 프로젝트에서는 원본 시그니처 초록 Color(27, 220, 117)로 고정한다.
    /// </summary>
    internal static class AbTheme
    {
        // ── 배경/외곽선 (Controls.cpp:11-16) ──
        public static readonly Color Gray         = Color.FromArgb(28, 28, 28);  // 폼/메인 배경
        public static readonly Color DarkGray     = Color.FromArgb(21, 21, 19);  // 사이드/어두운 영역
        public static readonly Color DarkerGray   = Color.FromArgb(19, 19, 19);
        public static readonly Color LightGray    = Color.FromArgb(40, 40, 40);  // 외곽선 안쪽
        public static readonly Color LighterGray  = Color.FromArgb(48, 48, 48);  // 외곽선 바깥쪽
        public static readonly Color Black        = Color.FromArgb(0, 0, 0);

        // GroupBox (Controls.cpp:364, 417-436)
        public static readonly Color GroupBg      = Color.FromArgb(25, 25, 25);  // CGroupBox 내부
        public static readonly Color GroupDark    = Color.FromArgb(10, 10, 10);  // 외곽 2중선 바깥
        public static readonly Color GroupLight   = Color.FromArgb(48, 48, 48);  // 외곽 2중선 안

        // ── 아웃라인/텍스트 (Controls.cpp:22-25) ──
        public static readonly Color Outline      = Color.FromArgb(10, 10, 10);
        public static readonly Color TextRegular  = Color.FromArgb(200, 200, 200);
        public static readonly Color TextOff      = Color.FromArgb(150, 150, 150); // 콤보/보조 텍스트
        public static readonly Color TextBright   = Color.FromArgb(210, 210, 210); // 선택 탭/활성

        // ── 그라디언트 (Controls.cpp:18-21) — hover/non-hover 컨트롤 배경 ──
        public static readonly Color GradHoverFirst    = Color.FromArgb(50, 50, 50);
        public static readonly Color GradHoverSecond   = Color.FromArgb(55, 55, 55);
        public static readonly Color GradNotHoverFirst = Color.FromArgb(35, 35, 35);
        public static readonly Color GradNotHoverSecond= Color.FromArgb(40, 40, 40);

        // ── MenuColor (액센트) — GUI.cpp:48 UI_COL_MAIN Color(27, 220, 117) ──
        // AByteCheat은 MenuColor()에서 R/G/B 각각 -15 한 값을 그라디언트 끝으로 쓴다 (Controls.cpp:279-283).
        public static readonly Color Accent      = Color.FromArgb(27, 220, 117);
        public static readonly Color AccentDark  = Color.FromArgb(12, 205, 102); // 각 채널 -15

        // 상태 색
        public static readonly Color Green  = Color.FromArgb(120, 224, 143); // ● Ready
        public static readonly Color Muted  = TextOff;
        public static readonly Color Hint   = Color.FromArgb(180, 200, 120); // 단축키 힌트 (다크 배지 대응)

        /// <summary>
        /// 수직 그라디언트 브러시 — AByteCheat Render::gradient_verticle() 재현.
        /// 위쪽이 top, 아래쪽이 bottom.
        /// </summary>
        public static LinearGradientBrush VerticalGradient(Rectangle rect, Color top, Color bottom)
        {
            // 1px 높이여도 수직 방향 보장. rect가 0높이면 brace로 1 확보.
            if (rect.Height <= 0) rect = new Rectangle(rect.X, rect.Y, rect.Width, 1);
            LinearGradientBrush b = new LinearGradientBrush(
                rect, top, bottom, LinearGradientMode.Vertical);
            return b;
        }

        /// <summary>
        /// 가로 그라디언트 브러시 — AByteCheat Render::GradientSideways() 재현.
        /// </summary>
        public static LinearGradientBrush HorizontalGradient(Rectangle rect, Color left, Color right)
        {
            if (rect.Width <= 0) rect = new Rectangle(rect.X, rect.Y, 1, rect.Height);
            LinearGradientBrush b = new LinearGradientBrush(
                rect, left, right, LinearGradientMode.Horizontal);
            return b;
        }
    }
}
