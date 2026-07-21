using System.Numerics;
using OsuParsers.Enums.Beatmaps;

namespace OsuParsers.Beatmaps.Objects.Taiko;

public class TaikoHit : HitCircle
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

	public TaikoColor Color
	{
		get
		{
			if (base.HitSound.HasFlag(HitSoundType.Whistle) || base.HitSound.HasFlag(HitSoundType.Clap))
			{
				return TaikoColor.Blue;
			}
			return TaikoColor.Red;
		}
		set
		{
			switch (value)
			{
			case TaikoColor.Red:
				if (base.HitSound.HasFlag(HitSoundType.Whistle))
				{
					base.HitSound -= 2;
				}
				if (base.HitSound.HasFlag(HitSoundType.Clap))
				{
					base.HitSound -= 8;
				}
				if (!base.HitSound.HasFlag(HitSoundType.Normal))
				{
					base.HitSound++;
				}
				break;
			case TaikoColor.Blue:
				if (base.HitSound.HasFlag(HitSoundType.Normal))
				{
					base.HitSound--;
				}
				if (!base.HitSound.HasFlag(HitSoundType.Whistle))
				{
					base.HitSound += 2;
				}
				break;
			}
		}
	}

	public TaikoHit(Vector2 position, int startTime, int endTime, HitSoundType hitSound, Extras extras, bool isNewCombo, int comboOffset)
		: base(position, startTime, endTime, hitSound, extras, isNewCombo, comboOffset)
	{
	}
}
