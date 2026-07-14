using System;

namespace OsuEnlightenOverlay.ControlPanel
{
    /// <summary>
    /// 오버레이 설정 데이터 모델 — NEWNEWOVERLAY OverlaySettings 포팅.
    /// </summary>
    public class OverlaySettings
    {
        // Overlay
        public bool Enabled = true;
        public bool CaptureBlocked = true;
        public int FpsCap = 300; // 0 = unlimited
        public bool HiddenOverride = false;

        // Difficulty Changer
        public float ArValue = 9.2f;     public bool ArAuto = true;
        public float CsValue = 4.0f;     public bool CsAuto = true;
        public float ArDtValue = 10.3f;  public bool ArDtAuto = true;
        public float ArHtValue = 8.0f;   public bool ArHtAuto = true;

        // Cursor
        public bool CursorAutoSize = false;
        public float CursorSize = 1.0f;
        // Cursor Pack — overlay-cursors\<PackName>\ 폴더에서 커서 텍스처 오버라이드
        public bool CursorPackEnabled = false;
        public string CursorPackName = "";

        // HUD (FPS, Accuracy, Combo, Hit Error Bar)
        public bool[] HudEnabled = new bool[] { true, true, true, true };
        public int[] HudFontSizes = new int[] { 32, 32, 32, 40 };
        public bool HudEditMode = false;
        public bool[] HudUseCustomPos = new bool[4];
        public float[] HudPositionX = new float[4];
        public float[] HudPositionY = new float[4];
        public float HudHitErrorScale = 1.0f;
        public int HudEditSelected = -1;   // edit 모드에서 선택된 HUD 인덱스 (-1=미선택)
        public bool HudEditSnap = true;     // 중앙 스냅 활성화

        // Skin
        public string SkinName = "Default";
        public bool InstaFade = false;  // hit animation 없이 즉시 사라짐

        // Paths (캐시)
        public string OsuRoot = "";

        /// <summary>
        /// 기본값 복사본 생성.
        /// </summary>
        public OverlaySettings Clone()
        {
            OverlaySettings c = new OverlaySettings();
            c.Enabled = Enabled;
            c.CaptureBlocked = CaptureBlocked;
            c.FpsCap = FpsCap;
            c.HiddenOverride = HiddenOverride;
            c.ArValue = ArValue; c.ArAuto = ArAuto;
            c.CsValue = CsValue; c.CsAuto = CsAuto;
            c.ArDtValue = ArDtValue; c.ArDtAuto = ArDtAuto;
            c.ArHtValue = ArHtValue; c.ArHtAuto = ArHtAuto;
            c.CursorAutoSize = CursorAutoSize;
            c.CursorSize = CursorSize;
            c.HudEnabled = (bool[])HudEnabled.Clone();
            c.HudFontSizes = (int[])HudFontSizes.Clone();
            c.HudEditMode = HudEditMode;
            c.HudUseCustomPos = (bool[])HudUseCustomPos.Clone();
            c.HudPositionX = (float[])HudPositionX.Clone();
            c.HudPositionY = (float[])HudPositionY.Clone();
            c.HudHitErrorScale = HudHitErrorScale;
            c.HudEditSelected = HudEditSelected;
            c.HudEditSnap = HudEditSnap;
            c.SkinName = SkinName;
            c.InstaFade = InstaFade;
            c.OsuRoot = OsuRoot;
            return c;
        }
    }
}