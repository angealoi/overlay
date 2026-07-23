using System;
using System.Numerics;
using AimAssistPlugin.Sdk.Audio;
using AimAssistPlugin.Sdk.Player;
using AimAssistPlugin.Services;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;

namespace AimAssistPlugin;

[PluginName("Tablet Area Randomizer")]
public class AimAssistPlugin : IPositionedPipelineElement<IDeviceReport>, IPipelineElement<IDeviceReport>
{
	private readonly object _syncLock = new object();

	public PipelinePosition Position => (PipelinePosition)2;

	[Property("Strength")]
	[DefaultPropertyValue(1.0f)]
	[ToolTip("Default: 1.0\n\n어시스트 강도.\n  0 = 끔\n  1 = 기본\n  3 = 강함\n  5+ = 매우 강함")]
	public float Strength
	{
		get => AimAssistSettings.Strength;
		set => AimAssistSettings.Strength = value;
	}

	[Property("Range")]
	[DefaultPropertyValue(3.0f)]
	[ToolTip("Default: 3.0\n\n작동 반경 (노트 반경의 배수). 어시스트가 켜지는 거리.\n  3 = 노트 반경 3배 안에 들어와야 작동\n  크게 = 멀리서부터 작동\n  작게 = 가까이 가야 작동\nPullPower(끌기 힘)와 다름: Range는 언제 켜지나, PullPower는 얼마나 세게 당기나.")]
	public float Range
	{
		get => AimAssistSettings.Range;
		set => AimAssistSettings.Range = value;
	}

	[Property("Curviness")]
	[DefaultPropertyValue(0.6f)]
	[ToolTip("Default: 0.6\n\nguide 라인 곡률.\n  0 = 노트끼리 직선\n  1 = 풀 커브 (부드럽게 흐름)")]
	public float Curviness
	{
		get => AimAssistSettings.Curviness;
		set => AimAssistSettings.Curviness = value;
	}

	[Property("Max Offset")]
	[DefaultPropertyValue(70f)]
	[ToolTip("Default: 70px\n\n커서가 raw 입력에서 벗어나는 최대 거리.\n  작게 = 젠틀·자연스러움\n  크게 = 세게 당김 (티남)")]
	public float MaxOffset
	{
		get => AimAssistSettings.MaxOffset;
		set => AimAssistSettings.MaxOffset = value;
	}

	[Property("Attack Inertia")]
	[DefaultPropertyValue(100f)]
	[ToolTip("Default: 100ms\n\nAttack 관성 — offset이 커질 때(어시스트 켜짐)의 SmoothDamp 시간.\n  크게 = 느리고 부드럽게 (사람스러움, 스파이크 방지)\n  작게 = 빠르게 반응\n노트로 끌려갈 때 커서가 부드럽게 가속합니다.")]
	public float AttackInertia
	{
		get => AimAssistSettings.AttackInertia;
		set => AimAssistSettings.AttackInertia = value;
	}

	[Property("Release Inertia")]
	[DefaultPropertyValue(15f)]
	[ToolTip("Default: 15ms\n\nRelease 관성 — offset이 작아질 때(어시스트 꺼짐/풀림)의 SmoothDamp 시간.\n  작게 = offset이 빨리 풀려 되돌아감이 안 보임 (권장)\n  크게 = 서서히 풀려 되돌아감이 보임\nResync와 함께 동작 — Resync가 손 움직임으로 offset을 깎고, Release Inertia가 나머지를 풉니다.")]
	public float ReleaseInertia
	{
		get => AimAssistSettings.ReleaseInertia;
		set => AimAssistSettings.ReleaseInertia = value;
	}

	[Property("Dead Zone")]
	[DefaultPropertyValue(0.5f)]
	[ToolTip("Default: 0.5\n\n데드존 반경 (노트 반경의 배수, 0~1).\n커서가 노트 중심에 이 거리 안으로 들어오면 어시스트를 부드럽게 끕니다.\n노트 안에서 노트 중심으로 끌려 튀는 걸 막아줍니다.\n  0 = 데드존 없음 (기존 동작)\n  0.5 = 노트 반경의 절반 (권장)\n  1.0 = 노트 반경 전체가 데드존")]
	public float DeadZone
	{
		get => AimAssistSettings.DeadZone;
		set => AimAssistSettings.DeadZone = value;
	}

	[Property("Idle Gate Window")]
	[DefaultPropertyValue(50f)]
	[ToolTip("Default: 50ms\n\n이동 게이트 시간창. 이 시간 안에 커서가 Idle Threshold 이상 움직였을 때만 어시스트가 작동합니다.\n커서를 멈추면 어시스트가 스스로 꺼져 노트로 끌려가는 드리프트를 막습니다.\n  0 = 게이트 없음 (항상 작동, 기존 동작)\n  50 = 최근 50ms 이동량으로 판정 (권장)\n  크게 = 움직임을 더 오래 기억 (더 잘 켜짐)\n  작게 = 순간 움직임만 감지 (엄격)")]
	public float IdleGateWindow
	{
		get => AimAssistSettings.IdleGateWindow;
		set => AimAssistSettings.IdleGateWindow = value;
	}

	[Property("Idle Threshold")]
	[DefaultPropertyValue(3f)]
	[ToolTip("Default: 3px\n\n이동 게이트 임계값. Idle Gate Window 시간 안에 커서가 이 값 이상 움직여야 어시스트가 켜집니다.\n  0 = 게이트 없음\n  3px = 권장 (손 떨림은 무시, 의도한 움직임만 인식)\n  작게 = 미세 움직임에도 작동 (예민)\n  크게 = 확실한 움직임 필요 (둔감)")]
	public float IdleThreshold
	{
		get => AimAssistSettings.IdleThreshold;
		set => AimAssistSettings.IdleThreshold = value;
	}

	[Property("Resync Factor")]
	[DefaultPropertyValue(0.6f)]
	[ToolTip("Default: 0.6\n\n손 움직임 기반 offset 감쇠 비율. Inertia와 함께 동작.\n손이 움직일 때 offset을 0 쪽으로 깎아 손이 노트에 도달하면 offset이 자연스럽게 풀립니다.\n  0 = Resync 끔 (Inertia만으로 감쇠 — 되돌아감이 보일 수 있음)\n  0.6 = 권장\n  클수록 손 움직임에 더 빨리 offset이 풀림\n핵심: 손이 멈추면 offset이 유지되어 되돌아감이 사라집니다.")]
	public float ResyncFactor
	{
		get => AimAssistSettings.ResyncFactor;
		set => AimAssistSettings.ResyncFactor = value;
	}

	public event Action<IDeviceReport>? Emit;

	private static float RoundOff(float val)
	{
		return (float)Math.Floor(val + 0.5f * (float)Math.Sign(val));
	}

	private static Vector2 RoundVec(Vector2 v)
	{
		return new Vector2(RoundOff(v.X), RoundOff(v.Y));
	}

	public void Consume(IDeviceReport report)
	{
		ITabletReport val = (ITabletReport)(object)((report is ITabletReport) ? report : null);
		if (val == null)
		{
			this.Emit?.Invoke(report);
			return;
		}
		if (!EnlightenService.IsRunning)
		{
			EnlightenService.Start();
		}
		if (Player.IsPlaying)
		{
			if (HitObjectManager.hitObjects.Count == 0)
			{
				AimAssistService.Initialize();
			}
			lock (_syncLock)
			{
				try
				{
					Vector2 offset = AimAssistService.GetOffset(((IAbsolutePositionReport)val).Position);
					Vector2 v = ((IAbsolutePositionReport)val).Position + offset;
					((IAbsolutePositionReport)val).Position = RoundVec(v);
					return;
				}
				catch (Exception)
				{
					return;
				}
				finally
				{
					this.Emit?.Invoke((IDeviceReport)(object)val);
				}
			}
		}
		// 메뉴/비플레이 상태 — AQN은 어시스트 없이 raw 그대로 출력.
		// Reset을 호출해 다음 플레이 진입 시 virtual_pos가 새 raw로 동기화되게 함.
		HitObjectManager.hitObjects.Clear();
		AimAssistService.Reset();
		this.Emit?.Invoke((IDeviceReport)(object)val);
	}
}
