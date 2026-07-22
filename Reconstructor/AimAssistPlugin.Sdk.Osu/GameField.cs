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
    ///
    /// 참고: 오버레이 OffsetVector1(GameFieldOffsetX/Y)에는 osu! stable 플레이필드 수직 보정
    /// modeOffset(-16*ratio)이 이미 포함되어 있다. 이 값은 노트 "렌더링 위치"이자 곧 "히트 판정 위치"이므로
    /// 커서 좌표계로 변환할 때 역상쇄(+16*ratio)를 해서는 안 된다 — 역상쇄하면 노트가 항상
    /// 실제 중심보다 아래로 쏠리게 된다. (이전 +16*ratio 보정은 삭제됨.)
    /// </summary>
	public static Vector2 FieldToDisplay(Vector2 field)
	{
		var state = EnlightenService.LatestState;
		if (state != null && state.Value.GameFieldReady == 1 && state.Value.GameFieldRatio > 0f)
		{
			float ratio = state.Value.GameFieldRatio;
			// 게임 필드 좌상단(화면 좌표) — 렌터박싱 시 검은 여백이 이미 제외된 실제 렌더 영역 기준.
			// OTD 좌표계(전체 화면)로 변환하려면 이 기준점이 필요. 클라이언트 영역 좌상단이 아님에 주의.
			float winX = state.Value.OsuWindowX;
			float winY = state.Value.OsuWindowY;
			return new Vector2(
				winX + state.Value.GameFieldOffsetX + field.X * ratio,
				winY + state.Value.GameFieldOffsetY + field.Y * ratio);
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
