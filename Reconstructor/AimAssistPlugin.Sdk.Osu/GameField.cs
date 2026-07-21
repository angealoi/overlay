using System.Numerics;
using System.Runtime.InteropServices;
using AimAssistPlugin.Sdk;
using AimAssistPlugin.Services;

namespace AimAssistPlugin.Sdk.Osu;

internal class GameField
{
    private static class ScreenInfo
    {
        public static int ScreenWidth => GetSystemMetrics(0);

        public static int ScreenHeight => GetSystemMetrics(1);

        public static int VirtualX => GetSystemMetrics(76);

        public static int VirtualY => GetSystemMetrics(77);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);
    }

    public static Vector2 CalculateScreenOffset()
    {
        float x = -ScreenInfo.VirtualX;
        float y = -ScreenInfo.VirtualY;
        return new Vector2(x, y);
    }

    private static float GetWidth()
    {
        return ScreenInfo.ScreenWidth;
    }

    private static float GetHeight()
    {
        return ScreenInfo.ScreenHeight;
    }

    /// <summary>
    /// 멀티 모니터 가상 화면 좌표계 → 주 모니터 로컬 좌표계 보정용 offset.
    /// Reconstructor가 받는 태블릿 좌표는 주 모니터 로컬 기준이므로 이 값을 빼야 맞춤.
    /// </summary>
    private static readonly Vector2 monitorOffsets = CalculateScreenOffset();

    /// <summary>
    /// HitObject 크기 스케일링용 ratio. OsuEnlightenOverlay2의 GameField.Ratio를 그대로 사용.
    /// 폴백: GameFieldReady가 0일 때 기존 공식 (Height*0.8/384).
    /// </summary>
    public static float GetRatio()
    {
        var state = EnlightenService.LatestState;
        if (state != null && state.Value.GameFieldReady == 1 && state.Value.GameFieldRatio > 0f)
            return state.Value.GameFieldRatio;
        // 폴백 — 오버레이 미연결 시. 기존 Reconstructor 공식.
        return GetHeight() * 0.8f / 384f;
    }

    /// <summary>
    /// osu! playfield 좌표(0~512, 0~384) → 화면 픽셀 좌표.
    /// OsuEnlightenOverlay2의 GameField.FieldToDisplay와 동일한 변환을 사용 —
    /// 양쪽이 동일한 좌표계를 써야 어시스트가 정확히 노트 위치로 향함.
    /// 폴백: GameFieldReady가 0일 때 기존 공식.
    /// </summary>
    /// <param name="forCursor">true면 osu! 커서 좌표계로 변환 (렌더링 modeOffset 제외).
    /// 어시스트는 커서 좌표계 기준으로 노트 위치를 봐야 하므로 true로 호출.
    /// 오버레이의 OffsetVector1에는 modeOffset(-16*ratio)이 포함되어 있는데,
    /// 이건 노트 스프라이트 렌더링용 보정이라 커서 좌표계엔 들어가지 않는다.
    /// 실측: osu! 커서 vs 오버레이 노트 위치가 Y축으로 일관 -35px(ratio=2.0) 차이.</param>
	public static Vector2 FieldToDisplay(Vector2 field, bool forCursor = false)
	{
		var state = EnlightenService.LatestState;
		if (state != null && state.Value.GameFieldReady == 1 && state.Value.GameFieldRatio > 0f)
		{
			float ratio = state.Value.GameFieldRatio;
			// 어시스트/커서 좌표계 — 렌더링 modeOffset을 역적용.
			// Δscreen=(0,0) 실측 완료 — 좌표 변환은 정확함.
			// osu! 커서 히트 판정 위치와 노트 field 좌표가 일치하므로 추가 보정 불필요.
			float modeOffset = forCursor ? 16f * ratio : 0f;
			// osu! 창 위치(화면 좌표) 추가 — OTD 좌표계(전체 화면)로 변환.
			float winX = state.Value.OsuWindowX;
			float winY = state.Value.OsuWindowY;
			return new Vector2(
				winX + state.Value.GameFieldOffsetX + field.X * ratio,
				winY + state.Value.GameFieldOffsetY + field.Y * ratio + modeOffset);
		}
        // 폴백 — 오버레이 미연결 시. 기존 Reconstructor 공식.
        return field * GetRatio() + GetOffset();
    }

    /// <summary>화면 좌표 → playfield 좌표. FieldToDisplay의 역변환.</summary>
    public static Vector2 DisplayToField(Vector2 display)
    {
        var state = EnlightenService.LatestState;
        if (state != null && state.Value.GameFieldReady == 1 && state.Value.GameFieldRatio > 0f)
        {
            return new Vector2(
                (display.X - state.Value.GameFieldOffsetX) / state.Value.GameFieldRatio,
                (display.Y - state.Value.GameFieldOffsetY) / state.Value.GameFieldRatio);
        }
        return (display - GetOffset()) / GetRatio();
    }

    // ── 폴백용 기존 공식 (오버레이가 안 켜졌을 때만 사용) ──
    private static Vector2 GetOffset()
    {
        float num = GetHeight() * 0.8f;
        float num2 = 1.3333334f * num;
        float x = (GetWidth() - num2) / 2f;
        float y = (GetHeight() - num) / 2f + 0.02f * GetHeight();
        return new Vector2(x, y);
    }
}
