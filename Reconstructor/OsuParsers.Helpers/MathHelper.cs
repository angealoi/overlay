using OsuParsers.Beatmaps;
using OsuParsers.Beatmaps.Objects;

namespace OsuParsers.Helpers;

internal class MathHelper
{
	public static double Clamp(double value, double min, double max)
	{
		if (value > max)
		{
			return max;
		}
		if (value < min)
		{
			return min;
		}
		return value;
	}

	public static double CalculateBpmMultiplier(TimingPoint timingPoint)
	{
		if (timingPoint.BeatLength >= 0.0)
		{
			return 1.0;
		}
		return Clamp((float)(0.0 - timingPoint.BeatLength), 10.0, 1000.0) / 100.0;
	}

	public static int CalculateEndTime(Beatmap beatmap, int startTime, int repeats, double pixelLength)
	{
		int num = (int)(pixelLength / (100.0 * beatmap.DifficultySection.SliderMultiplier) * (double)repeats * beatmap.BeatLengthAt(startTime));
		return startTime + num;
	}
}
