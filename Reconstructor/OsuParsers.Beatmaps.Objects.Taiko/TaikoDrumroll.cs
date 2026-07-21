using System;
using System.Collections.Generic;
using System.Numerics;
using OsuParsers.Enums.Beatmaps;

namespace OsuParsers.Beatmaps.Objects.Taiko;

public class TaikoDrumroll : Slider
{
	public bool IsBig
	{
		get
		{
			return base.HitSound.HasFlag(HitSoundType.Finish);
		}
		set
		{
			if (value && !base.HitSound.HasFlag(HitSoundType.Finish))
			{
				base.HitSound += 4;
			}
			else if (base.HitSound.HasFlag(HitSoundType.Finish))
			{
				base.HitSound -= 4;
			}
		}
	}

	public TaikoDrumroll(Vector2 position, int startTime, int endTime, HitSoundType hitSound, CurveType type, List<Vector2> points, int repeats, double pixelLength, List<HitSoundType> edgeHitSounds, List<Tuple<SampleSet, SampleSet>> edgeAdditions, Extras extras, bool isNewCombo, int comboOffset)
		: base(position, startTime, endTime, hitSound, type, points, repeats, pixelLength, isNewCombo, comboOffset, edgeHitSounds, edgeAdditions, extras)
	{
	}

	public TaikoDrumroll(Vector2 position, int startTime, int endTime, HitSoundType hitSound, CurveType type, List<Vector2> points, int repeats, double pixelLength, bool isNewCombo, int comboOffset)
		: base(position, startTime, endTime, hitSound, type, points, repeats, pixelLength, isNewCombo, comboOffset)
	{
	}
}
