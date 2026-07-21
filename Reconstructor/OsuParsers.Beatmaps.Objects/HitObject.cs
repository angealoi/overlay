using System;
using System.Numerics;
using OsuParsers.Enums.Beatmaps;

namespace OsuParsers.Beatmaps.Objects;

public class HitObject
{
	public Vector2 Position { get; set; } = Vector2.Zero;

	/// <summary>
	/// 스택 적용 전 원본 위치. stacking 알고리즘이 Position을 덮어쓰기 전에 저장해둠.
	/// osu! stable UpdateStacking이 Position = BasePosition - StackCount*stackVector 형태로 동작.
	/// </summary>
	public Vector2 BasePosition { get; set; } = Vector2.Zero;

	/// <summary>스택 카운트 — 같은 위치에 쌓이는 노트 수. 오버레이와 동일 알고리즘.</summary>
	public int StackCount { get; set; }

	/// <summary>타입 비트마스크 — 1=circle, 2=slider, 8=spinner, 4=newCombo. .osu 파일에서 파싱.</summary>
	public int Type { get; set; }

	public int StartTime { get; set; }

	public int EndTime { get; set; }

	public HitSoundType HitSound { get; set; }

	public Extras Extras { get; set; } = new Extras();

	public bool IsNewCombo { get; set; }

	public int ComboOffset { get; set; }

	public TimeSpan StartTimeSpan => TimeSpan.FromMilliseconds(StartTime);

	public TimeSpan EndTimeSpan => TimeSpan.FromMilliseconds(EndTime);

	public TimeSpan TotalTimeSpan => TimeSpan.FromMilliseconds(EndTime - StartTime);

	public HitObject()
	{
	}

	public HitObject(Vector2 position, int startTime, int endTime, HitSoundType hitSound, Extras extras, bool isNewCombo, int comboOffset)
	{
		Position = position;
		StartTime = startTime;
		EndTime = endTime;
		HitSound = hitSound;
		Extras = extras;
		IsNewCombo = isNewCombo;
		ComboOffset = comboOffset;
	}

	public float DistanceFrom(HitObject otherObject)
	{
		return Vector2.Distance(Position, otherObject.Position);
	}
}
