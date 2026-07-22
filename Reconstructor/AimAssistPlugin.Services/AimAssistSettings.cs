namespace AimAssistPlugin.Services;

/// <summary>
/// 어시스트 튜닝 — 경로추종 + 인간모션 모델의 설정.
/// </summary>
public static class AimAssistSettings
{
	/// <summary>어시스트 강도. 0 = 끔. 노트로 당기는 최대 비율(kMax)을 정한다.
	/// 포화 곡선: 1→36%, 3→74%, 5→90%. 높일수록 라인에 더 강하게 흡착.</summary>
	public static float Strength { get; set; } = 1.6f;

	/// <summary>작동 반경 (노트 반경의 배수). guide 라인에서 이 거리 안이면 흡착 시작.
	/// 4 = 노트 반경 4배 안에 들어오면 작동. 멀리서부터 잡히길 원하면 올림.</summary>
	public static float Range { get; set; } = 4.0f;

	/// <summary>guide 라인 곡률. 0 = 노트끼리 직선, 1 = 풀 커브(Catmull-Rom).
	/// 노트 사이에서 커서가 얼마나 부드럽게 휘어 흐르는지.</summary>
	public static float Curviness { get; set; } = 0.6f;

	/// <summary>최대 오프셋 (화면 px). 커서가 raw(손)에서 벗어나는 한계.
	/// 작을수록 젠틀·자연스럽고, 클수록 세게 당기지만 티가 나기 시작.</summary>
	public static float MaxOffset { get; set; } = 70f;

	/// <summary>어시스트 관성 (ms). 오프셋이 목표로 붙는 SmoothDamp 시간상수.
	/// 클수록 느리고 부드럽게(사람스럽게), 작을수록 빠르게 반응. 스파이크 방지의 핵심.</summary>
	public static float Inertia { get; set; } = 100f;

	/// <summary>데드존 반경 (노트 반경의 배수, 0~1).
	/// 커서가 노트 중심에 이 거리 안으로 들어오면 어시스트를 부드럽게 0으로 줄임.
	/// 노트 안에서 커서가 노트 중심으로 끌려 튀는 현상을 막는다.
	/// 0.5 = 노트 반경의 절반. 0 = 데드존 없음(기존 동작). 1.0 = 노트 반경 전체가 데드존.</summary>
	public static float DeadZone { get; set; } = 0.5f;

	/// <summary>이동 게이트 시간창 (ms, 0~500).
	/// 이 시간 안에 커서가 IdleThreshold 이상 움직였을 때만 어시스트가 작동한다.
	/// 커서가 멈춰있을 때 노트로 끌려가는 드리프트를 막는 핵심.
	/// 0 = 게이트 없음(항상 작동, 기존 동작). 50 = 최근 50ms 이동량으로 판정(권장).
	/// 크게 = 움직임을 더 오래 기억(더 잘 켜짐), 작게 = 순간 움직임만 감지(엄격).</summary>
	public static float IdleGateWindow { get; set; } = 50f;

	/// <summary>이동 게이트 임계값 (px, 0~50).
	/// IdleGateWindow 시간 안에 커서가 이 값 이상 움직여야 어시스트가 켜진다.
	/// 0 = 게이트 없음. 3px = 권장(손 떨림은 무시, 의도한 움직임만 인식).
	/// 작게 = 미세 움직임에도 작동(예민), 크게 = 확실한 움직임 필요(둔감).</summary>
	public static float IdleThreshold { get; set; } = 3f;
}
