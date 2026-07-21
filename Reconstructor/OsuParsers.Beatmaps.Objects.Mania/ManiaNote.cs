using System;
using System.Numerics;
using OsuParsers.Enums.Beatmaps;

namespace OsuParsers.Beatmaps.Objects.Mania;

public class ManiaNote : HitCircle
{
	public new Vector2 Position
	{
		get
		{
			return new Vector2(base.Position.X, 0f);
		}
		set
		{
			base.Position = value;
		}
	}

	public ManiaNote(Vector2 position, int startTime, int endTime, HitSoundType hitSound, Extras extras, bool isNewCombo, int comboOffset)
		: base(position, startTime, endTime, hitSound, extras, isNewCombo, comboOffset)
	{
	}

	public void SetColumn(int count, int column)
	{
		double num = 512.0 / (double)count;
		int num2 = Convert.ToInt32(Math.Floor((double)column * num));
		Position = new Vector2(num2, 0f);
	}

	public int GetColumn(int count)
	{
		double num = 512.0 / (double)count;
		return (int)((double)Position.X / num);
	}
}
