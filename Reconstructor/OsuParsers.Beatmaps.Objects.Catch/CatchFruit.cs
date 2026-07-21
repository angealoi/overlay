using System.Numerics;
using OsuParsers.Enums.Beatmaps;

namespace OsuParsers.Beatmaps.Objects.Catch;

public class CatchFruit : HitCircle
{
	public CatchFruit(Vector2 position, int startTime, int endTime, HitSoundType hitSound, Extras extras, bool isNewCombo, int comboOffset)
		: base(position, startTime, endTime, hitSound, extras, isNewCombo, comboOffset)
	{
	}
}
