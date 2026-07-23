using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using AimAssistPlugin.Sdk.Audio;
using AimAssistPlugin.Sdk.Osu;
using AimAssistPlugin.Sdk.Player;
using OsuParsers.Beatmaps.Objects;

namespace AimAssistPlugin.Services;

/// <summary>
/// 경로추종(path-following) + 인간모션 어시스트.
///
/// 3단계:
///  1) guide(time): 노트들을 시간순 waypoint 타임라인으로 잇고 Catmull-Rom(곡률 blend)으로
///     평가한 점. 한 점(타겟)이 아니라 곡선을 따라 흐른다.
///     슬라이더/스피너는 [StartTime, EndTime] 구간을 "바디"로 점유 → 그 동안 어시스트 OFF.
///     다음 객체로의 접근은 이전 객체의 EndTime부터 시작 (슬라이더 끝난 뒤 다음 서클 어시스트).
///  2) 목표 오프셋 = (guide - cursor) × k,  k = kMax(Strength) × kDist(거리).
///     guide까지 거리가 노트반경 ~ 작동반경(노트반경×Range) 사이일 때만 작동.
///  3) 인간 모션: 실제 오프셋을 목표로 SmoothDamp(임계감쇠)로 따라가되
///     |오프셋| ≤ MaxOffset 로 캡. 오프셋이 "작고 + 느리게만" 변하므로
///     어떤 입력에도 출력 커서에 스파이크/스냅이 구조적으로 생기지 않는다.
///
/// SmoothDamp은 프레임독립(실경과시간 dt 기반)이라 태블릿 리포트 레이트가 달라도 동일하게 동작.
/// </summary>
public class AimAssistService
{
	public static float Power
	{
		get => AimAssistSettings.Strength;
		set => AimAssistSettings.Strength = value;
	}

	private static readonly Vector2 monitorOffsets = GameField.CalculateScreenOffset();

	private static float hitObjectRadius;
	private static int lastAudioTime = -1;
	private static int seg; // 현재 waypoint 세그먼트 (시간 기준, 전진만).

	// ── waypoint 타임라인 (Initialize에서 구축, field 좌표) ──
	// 서클: (head, StartTime, gap). 슬라이더/스피너: (head, StartTime, body) + (exit, EndTime, gap).
	private static float[] _wpX = Array.Empty<float>();
	private static float[] _wpY = Array.Empty<float>();
	private static int[] _wpT = Array.Empty<int>();
	private static bool[] _wpGap = Array.Empty<bool>(); // true=접근 구간(ON), false=바디(OFF)
	private static int _wpN;
	private static bool _assistOff; // GuideAt이 설정 — 바디/맵끝이면 true.

	// 인간 모션 상태 — 오프셋과 그 속도(SmoothDamp용).
	private static Vector2 _offset;
	private static float _offVelX;
	private static float _offVelY;
	private static readonly Stopwatch _clock = Stopwatch.StartNew();
	private static long _lastTicks;

	// 이전 프레임 raw 커서 위치 — Resync(손 움직임 기반 offset 감쇠)용.
	// SmoothDamp만 쓰면 손 멈췄을 때 offset이 Inertia 시간에 걸쳐 풀려 "되돌아감"이 보인다.
	// Resync는 손 움직임(displacement)에 비례해 offset을 0 쪽으로 깎아, 손이 멈추면 offset을 유지한다.
	private static Vector2 _lastRawPos;

	// ── 이동 게이트 (idle drift 방지) ──
	// 최근 커서 위치를 시간과 함께 저장. 게이트 시간창 안의 순수 이동거리를 측정.
	// 단순히 (lastPos - curPos)를 쓰면 정지 중 어시스트 offset이 위치를 흔들어
	// "움직이고 있다"로 오인되므로, 원본(raw) 위치만 기록한다.
	private const int MoveHistoryMax = 64;
	private static readonly long[] _moveTicks = new long[MoveHistoryMax];
	private static readonly float[] _movePosX = new float[MoveHistoryMax];
	private static readonly float[] _movePosY = new float[MoveHistoryMax];
	private static int _moveHead; // 다음 write 위치 (ring buffer)

	public static void Initialize()
	{
		HitObjectManager.CacheHitObjects();
		hitObjectRadius = HitObjectManager.GetHitObjectRadius();
		BuildWaypoints();
		lastAudioTime = -1;
		seg = 0;
		_offset = Vector2.Zero;
		_offVelX = 0f;
		_offVelY = 0f;
		_lastTicks = _clock.ElapsedTicks;
		_lastRawPos = Vector2.Zero;
		_moveHead = 0;
	}

	public static void Reset()
	{
		lastAudioTime = -1;
		seg = 0;
		_wpN = 0;
		_offset = Vector2.Zero;
		_offVelX = 0f;
		_offVelY = 0f;
		_lastTicks = _clock.ElapsedTicks;
		_lastRawPos = Vector2.Zero;
		_moveHead = 0;
	}

	/// <summary>노트 타임라인을 waypoint 배열로 구축. 슬라이더/스피너는 바디 구간을 갖는다.</summary>
	private static void BuildWaypoints()
	{
		int count = HitObjectManager.GetHitObjectsCount();
		var xs = new List<float>(count + 4);
		var ys = new List<float>(count + 4);
		var ts = new List<int>(count + 4);
		var gaps = new List<bool>(count + 4); // true=접근 구간(ON), false=바디/스피너진입(OFF)

		// gap[k] = "wp[k] → wp[k+1] 구간"의 ON/OFF.
		// 다음 객체가 스피너면 그 구간(스피너로의 진입)을 끈다 — 스피너는 중심 고정 회전이라
		// 경로 추종이 무의미하고, head.Position이 실제 중심과 어긋난 경우가 많아 오히려 방해됨.
		for (int i = 0; i < count; i++)
		{
			HitObject ho = HitObjectManager.GetHitObject(i);
			Vector2 head = ho.Position;
			bool nextIsSpinner = (i + 1 < count) && HitObjectManager.IsSpinner(HitObjectManager.GetHitObject(i + 1));

			if (HitObjectManager.IsBodyObject(i))
			{
				// 진입(head) — 이후 구간은 바디(OFF).
				xs.Add(head.X); ys.Add(head.Y); ts.Add(ho.StartTime); gaps.Add(false);
				// 탈출(exit) — 이후 구간은 다음 객체로의 접근. 다음이 스피너면 OFF, 아니면 ON.
				Vector2 exit = HitObjectManager.GetExitFieldPosition(i);
				xs.Add(exit.X); ys.Add(exit.Y); ts.Add(ho.EndTime); gaps.Add(!nextIsSpinner);
			}
			else
			{
				// 서클 — 이후 구간이 스피너로의 진입이면 OFF, 아니면 다음 객체로의 접근(ON).
				xs.Add(head.X); ys.Add(head.Y); ts.Add(ho.StartTime); gaps.Add(!nextIsSpinner);
			}
		}

		_wpX = xs.ToArray();
		_wpY = ys.ToArray();
		_wpT = ts.ToArray();
		_wpGap = gaps.ToArray();
		_wpN = xs.Count;
	}

	/// <summary>waypoint field 좌표 → 화면 좌표.</summary>
	private static Vector2 ScreenWp(int k)
		=> GameField.FieldToDisplay(new Vector2(_wpX[k], _wpY[k])) + monitorOffsets;

	public static Vector2 GetOffset(Vector2 cursorPosition)
	{
		if (_wpN <= 0) return Vector2.Zero;

		int time = AudioEngine.GetTime();

		// 되감기(리트라이/시크) 감지 — 재초기화.
		if (lastAudioTime != -1 && time < lastAudioTime)
			Initialize();
		lastAudioTime = time;

		// 실제 경과시간(초) — SmoothDamp은 프레임독립.
		long now = _clock.ElapsedTicks;
		float dt = (float)((now - _lastTicks) / (double)Stopwatch.Frequency);
		_lastTicks = now;
		dt = Math.Clamp(dt, 0.001f, 0.05f);

		// 이동 게이트 — raw 커서 이력을 시간창 안에서 평가해 "의도한 움직임인가?" 판정.
		// 어시스트 offset으로 가공된 위치가 아닌, 손(raw) 위치만 기록한다.
		float gateScale = ComputeIdleGate(cursorPosition, now);

		// 손 이동량 (이전 프레임 raw 대비). Resync(손 움직임 기반 offset 감쇠)에 사용.
		// offset이 가공된 위치가 아니라 순수 손 움직임만 봐야 한다.
		Vector2 displacement = _lastRawPos - cursorPosition;
		_lastRawPos = cursorPosition;

		Vector2 rawTarget = ComputeTargetOffset(cursorPosition, time);
		// 게이트가 닫히면 target을 0으로 → SmoothDamp이 부드럽게 offset을 풀어 드리프트 차단.
		Vector2 target = rawTarget * gateScale;

		// ── 인간 모션: 오프셋을 목표로 SmoothDamp (attack/release 분리) + 크기 캡 ──
		// attack  = offset이 커지는 방향 (어시스트 켜짐) → AttackInertia로 부드럽게 (스파이크 방지)
		// release = offset이 작아지는 방향 (어시스트 꺼짐/풀림) → ReleaseInertia로 빠르게 (되돌아감 방지)
		// 축별로 |target| vs |현재 offset| 로 attack/release 판정.
		float attackSt  = Math.Clamp(AimAssistSettings.AttackInertia, 1f, 500f) / 1000f;
		float releaseSt = Math.Clamp(AimAssistSettings.ReleaseInertia, 1f, 500f) / 1000f;
		float stX = Math.Abs(target.X) >= Math.Abs(_offset.X) ? attackSt : releaseSt;
		float stY = Math.Abs(target.Y) >= Math.Abs(_offset.Y) ? attackSt : releaseSt;
		_offset.X = SmoothDamp(_offset.X, target.X, ref _offVelX, stX, dt);
		_offset.Y = SmoothDamp(_offset.Y, target.Y, ref _offVelY, stY, dt);

		// ── Resync: 손 움직임 기반 offset 감쇠 ──
		// SmoothDamp만으로는 손 멈췄을 때 offset이 Inertia 시간에 걸쳐 풀려 "되돌아감"이 보인다.
		// Resync는 손 움직임(displacement)에 비례해 offset을 0 쪽으로만 깎는다 —
		// 손이 멈추면 displacement=0 → offset 유지 (되돌아감 없음).
		// 손이 노트로 움직이면 offset이 그 움직임에 맞춰 자연스럽게 0으로 수렴.
		float resyncRate = Math.Clamp(AimAssistSettings.ResyncFactor, 0f, 2f);
		if (resyncRate > 0f)
			_offset = ResyncOffset(displacement, _offset, resyncRate);

		float maxOff = Math.Clamp(AimAssistSettings.MaxOffset, 0f, 1000f);
		float m = _offset.Length();
		if (m > maxOff && m > 0.0001f)
			_offset *= maxOff / m;

		return _offset;
	}

	/// <summary>
	/// 손 움직임(displacement)에 비례해 offset을 0 쪽으로만 감쇠 — 원본 Reconstructor Resync 포팅.
	/// offset을 절대 반대 방향으로 키우지 않고, 손이 움직인 만큼만 0으로 깎는다.
	/// 손이 멈추면 displacement=0 → offset 변화 없음 (되돌아감 차단).
	/// </summary>
	private static Vector2 ResyncOffset(Vector2 displacement, Vector2 offset, float resyncFactor)
	{
		if (offset.Length() <= float.Epsilon) return offset;
		Vector2 v = displacement * resyncFactor;
		// 각 축: offset 부호 방향으로 displacement만큼 깎되, 0을 넘어 반대로 가진 않음.
		offset.X = ResyncAxis(offset.X, v.X);
		offset.Y = ResyncAxis(offset.Y, v.Y);
		return offset;
	}

	/// <summary>단일 축 Resync — offset을 0 방향으로만 감소시킨다.</summary>
	private static float ResyncAxis(float offset, float v)
	{
		if (offset > 0f)
			return Math.Max(0f, v >= 0f ? offset - v : offset + v);
		return Math.Min(0f, v <= 0f ? offset - v : offset + v);
	}

	/// <summary>목표 오프셋 = (guide - cursor) × k. 바디 구간이거나 거리 밖이면 0.</summary>
	private static Vector2 ComputeTargetOffset(Vector2 cursor, int time)
	{
		float strength = Math.Clamp(AimAssistSettings.Strength, 0f, 10f);
		if (strength <= 0f) return Vector2.Zero;

		Vector2 guide = GuideAt(time);
		if (_assistOff) return Vector2.Zero; // 슬라이더/스피너 바디, 또는 맵 끝.

		Vector2 toGuide = guide - cursor;
		float dist = toGuide.Length();

		float ratio = GameField.GetRatio();
		float hitR = Math.Max(hitObjectRadius * ratio, 1f);
		float maxR = hitR * Math.Clamp(AimAssistSettings.Range, 1f, 20f);
		if (dist > maxR || dist < 0.0001f) return Vector2.Zero;

		// 거리 응답 곡선 — 3구간 페이드:
		//   [0, deadR)            : 데드존 — k=0. 노트 중심근처 튐 방지.
		//   [deadR, hitR]         : 데드존 → 최대로 부드럽게 상승.
		//   (hitR, maxR]          : 최대 → 0으로 선형 감소 (기존 동작).
		//
		// ⚠️ 데드존 기준점은 guide(경로상 점)가 아니라 "가장 가까운 노트"다.
		// guide는 두 노트 사이 경로 위 임의의 점이라 그 점 기준 deadzone이 켜지면
		// 이동 중에도 어시스트가 꺼지는 의도치 않은 동작이 생긴다.
		// deadzone의 본래 목적은 "노트 중심 근처에서 커서가 튀는 걸 막는 것"이므로
		// 실제 히트 위치(가장 가까운 서클/슬라이더 head) 기준으로 판정해야 한다.
		float deadR = hitR * Math.Clamp(AimAssistSettings.DeadZone, 0f, 1f);
		bool inDeadZone = IsCursorNearClosestCircle(cursor, time, deadR);

		float kDist;
		if (inDeadZone)
		{
			kDist = 0f; // 데드존 — 가장 가까운 노트 중심 근처. 어시스트 끔.
		}
		else
		{
			// 데드존~hitR 구간: 0 → 1로 상승, hitR~maxR 구간: 1 → 0으로 감소.
			// (kDist의 거리 인자는 여전히 guide 기준 dist — 어시스트 강도 곡선.)
			float t = Math.Clamp((dist - deadR) / Math.Max(hitR - deadR, 1f), 0f, 1f);
			kDist = t * (1f - Math.Clamp((dist - hitR) / Math.Max(maxR - hitR, 1f), 0f, 1f));
		}
		float kMax = Math.Clamp(1f - MathF.Exp(-strength * 0.45f), 0f, 0.95f);
		float k = kMax * kDist;

		return toGuide * k;
	}

	/// <summary>
	/// 커서가 "가장 가까운 다가오는 노트(서클/슬라이더 head)"의 중심 근처에 있는가.
	/// deadzone 판정 전용 — guide(경로점)가 아니라 실제 히트 위치 기준.
	/// 스피너는 제외(회전이라 히트 위치 개념 없음). 다가오는 노트가 없으면 false.
	/// </summary>
	private static bool IsCursorNearClosestCircle(Vector2 cursor, int time, float deadR)
	{
		if (deadR <= 0f) return false;
		int idx = HitObjectManager.GetUpcomingNonSpinnerIndex(time);
		if (idx < 0) return false;
		HitObject ho = HitObjectManager.GetHitObject(idx);
		Vector2 circleScreen = GameField.FieldToDisplay(ho.Position) + monitorOffsets;
		float distToCircle = Vector2.Distance(cursor, circleScreen);
		return distToCircle <= deadR;
	}

	/// <summary>
	/// 이동 게이트 — 최근 시간창 안의 순수 raw 커서 이동거리로 "의도한 움직임인가?" 평가.
	/// 반환값: 0(게이트 닫힘, 어시스트 끔) ~ 1(게이트 열림, 정상 작동).
	/// IdleGateWindow=0 또는 IdleThreshold=0 이면 항상 1 (게이트 없음, 기존 동작).
	///
	/// 핵심 설계:
	/// - raw 위치(손)만 기록한다. 어시스트 offset이 더해진 최종 위치를 기록하면
	///   정지 중에도 offset이 위치를 흔들어 "움직인다"고 오인해 게이트가 안 닫힌다.
	/// - 이동거리는 경로 길이가 아닌 창 내 시작점-끝점 거리. 한 점 주위를 맴도는
	///   미세 떨림(jitter)은 시작-끝이 가까워 게이트를 열지 못한다.
	/// </summary>
	private static float ComputeIdleGate(Vector2 cursorPosition, long nowTicks)
	{
		float windowMs = Math.Clamp(AimAssistSettings.IdleGateWindow, 0f, 500f);
		float threshold = Math.Clamp(AimAssistSettings.IdleThreshold, 0f, 50f);
		// 게이트 비활성 — 항상 작동 (기존 동작).
		if (windowMs <= 0f || threshold <= 0f)
		{
			RecordMove(cursorPosition, nowTicks);
			return 1f;
		}

		// 현재 raw 위치 기록.
		RecordMove(cursorPosition, nowTicks);

		// 시간창 시작 tick 계산.
		long windowTicks = (long)(windowMs * Stopwatch.Frequency / 1000.0);
		long cutoff = nowTicks - windowTicks;

		// ring buffer에서 시간창 안에 드는 가장 오래된 샘플을 찾아 시작-끝 거리 측정.
		// _moveHead는 다음 write 위치 = 가장 최근 샘플의 다음 칸.
		// 가장 최근 샘플 = (_moveHead - 1 + Max) % Max = cursorPosition (방금 기록).
		// 창 안 가장 오래된 샘플을 찾아 거리를 잰다.
		float oldestX = cursorPosition.X, oldestY = cursorPosition.Y;
		bool found = false;
		for (int i = 1; i < MoveHistoryMax; i++)
		{
			int idx = (_moveHead - 1 - i + MoveHistoryMax) % MoveHistoryMax;
			// 빈 칸(tick=0)이면 이전 샘플 없음 — 찾은 것 중 가장 오래된 것 사용.
			if (_moveTicks[idx] == 0) break;
			if (_moveTicks[idx] < cutoff) break; // 창 밖 — 직전 샘플까지만 유효.
			oldestX = _movePosX[idx];
			oldestY = _movePosY[idx];
			found = true;
		}

		if (!found)
		{
			// 창 안 샘플이 현재 한 개뿐 — 아직 판정 불가. 안전하게 닫지 않는다.
			return 1f;
		}

		float dx = cursorPosition.X - oldestX;
		float dy = cursorPosition.Y - oldestY;
		float travel = MathF.Sqrt(dx * dx + dy * dy);

		// 임계값 도달 여부로 이진 판정. SmoothDamp(Inertia)가 전환을 부드럽게 흡수.
		return travel >= threshold ? 1f : 0f;
	}

	/// <summary>raw 커서 위치를 ring buffer에 기록.</summary>
	private static void RecordMove(Vector2 pos, long ticks)
	{
		int i = _moveHead;
		_moveTicks[i] = ticks;
		_movePosX[i] = pos.X;
		_movePosY[i] = pos.Y;
		_moveHead = (i + 1) % MoveHistoryMax;
	}

	/// <summary>
	/// waypoint 타임라인을 현재 시간으로 평가한 guide 점.
	/// 바디 구간이면 _assistOff=true 로 표시(어시스트 없음).
	/// </summary>
	private static Vector2 GuideAt(int time)
	{
		_assistOff = false;

		// 첫 노트 StartTime 전 — 어시스트 끔.
		// 곡 시작 직후(skip 버튼 타이밍)에 GameField 정보가 아직 갱신되지 않아 폴백 좌표가
		// 쓰이면 엉뚱한 곳으로 끌려간다. 첫 노트가 화면에 나오기 전엔 어시스트할 필요가 없다.
		if (time < _wpT[0]) { _assistOff = true; return Vector2.Zero; }
		if (_wpN == 1) return ScreenWp(0);
		if (time >= _wpT[_wpN - 1]) { _assistOff = true; return Vector2.Zero; } // 맵 끝

		// 세그먼트 전진 — 되감기는 Initialize가 처리하므로 전진만.
		while (seg + 1 < _wpN && time >= _wpT[seg + 1])
			seg++;
		if (seg > _wpN - 2) seg = _wpN - 2;
		if (seg < 0) seg = 0;

		// 바디 구간 — 어시스트 OFF (오프셋은 SmoothDamp로 0으로 수렴).
		if (!_wpGap[seg]) { _assistOff = true; return Vector2.Zero; }

		int t0 = _wpT[seg];
		int t1 = _wpT[seg + 1];
		float u = (t1 > t0) ? (float)(time - t0) / (t1 - t0) : 0f;
		u = Math.Clamp(u, 0f, 1f);

		Vector2 p1 = ScreenWp(seg);
		Vector2 p2 = ScreenWp(seg + 1);
		Vector2 p0 = ScreenWp(Math.Max(seg - 1, 0));
		Vector2 p3 = ScreenWp(Math.Min(seg + 2, _wpN - 1));

		Vector2 lin = Vector2.Lerp(p1, p2, u);
		float u2 = u * u, u3 = u2 * u;
		Vector2 cr = 0.5f * ((2f * p1)
			+ (-p0 + p2) * u
			+ (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2
			+ (-p0 + 3f * p1 - 3f * p2 + p3) * u3);

		float curv = Math.Clamp(AimAssistSettings.Curviness, 0f, 1f);
		return lin + (cr - lin) * curv;
	}

	/// <summary>Unity SmoothDamp 포팅 — 임계감쇠로 target을 부드럽게 추종. 프레임독립.</summary>
	private static float SmoothDamp(float current, float target, ref float vel, float smoothTime, float dt)
	{
		smoothTime = MathF.Max(smoothTime, 1e-4f);
		float omega = 2f / smoothTime;
		float x = omega * dt;
		float exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
		float change = current - target;
		float temp = (vel + omega * change) * dt;
		vel = (vel - omega * temp) * exp;
		return target + (change + temp) * exp;
	}
}
