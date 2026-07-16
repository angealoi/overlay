using System;
using System.IO;

namespace OsuEnlightenOverlay.ControlPanel
{
    /// <summary>
    /// 오버레이 설정 데이터 모델 — NEWNEWOVERLAY OverlaySettings 포팅.
    /// </summary>
    public class OverlaySettings
    {
        // ── 유효 범위 상수 ──
        // 컨트롤 Min/Max와 Normalize() 클램프가 공유하는 단일 출처. 두 곳이 갈라지면
        // 컨트롤 범위 밖 값이 로드되어 A1 크래시(범위 밖 .Value 대입)가 재발한다.
        public const int FpsCapMin = 0, FpsCapMax = 10000;
        public const float CursorSizeMin = 0.1f, CursorSizeMax = 2.0f;
        public const float ArMin = 0f, ArMax = 10f;
        public const float CsMin = 0f, CsMax = 10f;
        public const float DtArMin = 0f, DtArMax = 12f;  // DT만 상한 12 (AR10 맵+DT 체감 AR)
        public const float HtArMin = 0f, HtArMax = 10f;
        // 컨트롤엔 안 걸리지만 렌더/폰트가 소비하므로 온전한 값 보장 (EditConstants와 동일 범위).
        public const int HudFontSizeMin = 8, HudFontSizeMax = 96;
        public const float HudHitErrorScaleMin = 0.1f, HudHitErrorScaleMax = 10f;

        // Overlay
        public bool Enabled = true;
        public bool CaptureBlocked = true;
        public int FpsCap = 300; // 0 = unlimited
        public bool HiddenOverride = false;

        // Difficulty Changer
        public float ArValue = 9.2f;
        public float CsValue = 4.0f;
        public float ArDtValue = 10.3f;
        public float ArHtValue = 8.0f;

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
            c.ArValue = ArValue;
            c.CsValue = CsValue;
            c.ArDtValue = ArDtValue;
            c.ArHtValue = ArHtValue;
            c.CursorAutoSize = CursorAutoSize;
            c.CursorSize = CursorSize;
            c.CursorPackEnabled = CursorPackEnabled;
            c.CursorPackName = CursorPackName;
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

        /// <summary>
        /// 로드된 값 정규화 — 손상/범위 밖/비정상(NaN·Infinity) 값을 안전 범위로 강제한다.
        /// SettingsSerializer.Load 끝에서 항상 호출한다.
        ///
        /// 이게 없으면 settings.ini의 값 하나로 기동이 막힌다 (A1): 범위 밖 값이 컨트롤
        /// .Value 대입에서 예외를 던지고, NaN/Infinity는 (decimal) 캐스트에서 OverflowException,
        /// 경로 문자가 섞인 스킨/커서팩 이름은 Path.Combine에서 ArgumentException을 낸다.
        /// 컨트롤 패널이 메인 폼이라 이 예외 하나면 앱이 아예 안 뜬다.
        /// </summary>
        public void Normalize()
        {
            FpsCap = Clamp(FpsCap, FpsCapMin, FpsCapMax);
            CursorSize = Clamp(CursorSize, CursorSizeMin, CursorSizeMax, 1.0f);
            ArValue = Clamp(ArValue, ArMin, ArMax, 9.2f);
            CsValue = Clamp(CsValue, CsMin, CsMax, 4.0f);
            ArDtValue = Clamp(ArDtValue, DtArMin, DtArMax, 10.3f);
            ArHtValue = Clamp(ArHtValue, HtArMin, HtArMax, 8.0f);
            HudHitErrorScale = Clamp(HudHitErrorScale, HudHitErrorScaleMin, HudHitErrorScaleMax, 1.0f);

            // HUD 배열은 항상 길이 4·non-null이어야 인덱싱이 안전하다.
            HudEnabled = EnsureLength(HudEnabled, 4, true);
            HudUseCustomPos = EnsureLength(HudUseCustomPos, 4, false);
            HudFontSizes = EnsureLength(HudFontSizes, 4, 32);
            HudPositionX = EnsureLength(HudPositionX, 4, 0f);
            HudPositionY = EnsureLength(HudPositionY, 4, 0f);
            for (int i = 0; i < 4; i++)
            {
                HudFontSizes[i] = Clamp(HudFontSizes[i], HudFontSizeMin, HudFontSizeMax);
                HudPositionX[i] = Clamp(HudPositionX[i], 0f, 1f, 0f);
                HudPositionY[i] = Clamp(HudPositionY[i], 0f, 1f, 0f);
            }

            // 스킨/커서팩 이름은 단일 폴더명 — 경로 구분자·불법 문자가 섞이면 Path.Combine이
            // 예외를 던진다. 안전하지 않으면 기본값으로 되돌린다 (경로 탈출도 함께 차단).
            if (string.IsNullOrEmpty(SkinName) || !IsSafeFolderName(SkinName))
                SkinName = "Default";
            if (!IsSafeFolderName(CursorPackName))
            {
                CursorPackName = "";
                CursorPackEnabled = false;
            }
        }

        // 정수: 범위 밖은 경계로 클램프 (NaN이 없으므로 fallback 불필요).
        static int Clamp(int v, int min, int max)
        {
            return v < min ? min : (v > max ? max : v);
        }

        // 실수: NaN/Infinity는 명백한 쓰레기 → fallback. 유한 범위 밖은 경계로 클램프.
        static float Clamp(float v, float min, float max, float fallback)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return fallback;
            return v < min ? min : (v > max ? max : v);
        }

        // 폴더명 하나로 쓰기 안전한지 — 경로 구분자(\ /)·드라이브(:)·와일드카드 등을 모두 거른다.
        // "."/".."는 불법 문자가 없어 통과하지만 상위/현재 디렉토리를 가리켜 경로 탈출이 되므로 별도 차단.
        static bool IsSafeFolderName(string name)
        {
            if (string.IsNullOrEmpty(name)) return true; // 빈 값 판정은 호출부에 맡긴다
            if (name == "." || name == "..") return false;
            return name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }

        // 배열이 null이거나 길이가 다르면 재구성 (기존 값 보존, 부족분은 fill).
        static T[] EnsureLength<T>(T[] arr, int len, T fill)
        {
            if (arr != null && arr.Length == len) return arr;
            T[] n = new T[len];
            for (int i = 0; i < len; i++)
                n[i] = (arr != null && i < arr.Length) ? arr[i] : fill;
            return n;
        }
    }
}