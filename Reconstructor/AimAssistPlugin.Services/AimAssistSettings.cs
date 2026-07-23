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

	/// <summary>Attack 관성 (ms) — offset이 커질 때(어시스트 켜짐)의 SmoothDamp 시간상수.
	/// 크수록 느리고 부드럽게(사람스럽게), 작을수록 빠르게 반응. 스파이크 방지의 핵심.
	/// 노트로 끌려갈 때 커서가 부드럽게 가속하도록 한다.</summary>
	public static float AttackInertia { get; set; } = 100f;

	/// <summary>Release 관성 (ms) — offset이 작아질 때(어시스트 꺼짐/풀림)의 SmoothDamp 시간상수.
	/// 작을수록 offset이 빨리 풀려 "되돌아감"이 안 보임. 크면 서서히 풀려 되돌아감이 보임.
	/// Resync와 함께 동작 — Resync가 손 움직임으로 offset을 깎고, ReleaseInertia가 나머지를 푼다.</summary>
	public static float ReleaseInertia { get; set; } = 15f;

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
	/// 작게 = 미세 움직임에도 작동(예민), 크게 = 확실한 워직임 필요(둔감).</summary>
	public static float IdleThreshold { get; set; } = 3f;

	/// <summary>Resync 비율 (0~2) — 손 움직임 기반 offset 감쇠 강도.
	/// SmoothDamp(Inertia)와 함께 동작. 손이 움직일 때 offset을 0 쪽으로 깎아
	/// "손이 노트에 도달하면 offset이 자연스럽게 0으로 수렴"하게 한다.
	/// 0 = Resync 끔 (Inertia만으로 offset 감쇠 — 되돌아감이 보일 수 있음).
	/// 0.6 = 권장. 클수록 손 움직임에 더 빨리 offset이 풀림.
	/// 핵심: 손이 멈추면 offset이 유지되어 Inertia만 쓸 때의 "되돌아감"이 사라진다.</summary>
	public static float ResyncFactor { get; set; } = 0.6f;
}
