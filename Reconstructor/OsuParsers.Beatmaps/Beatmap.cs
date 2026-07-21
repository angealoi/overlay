using System.Collections.Generic;
using System.IO;
using OsuParsers.Beatmaps.Objects;
using OsuParsers.Beatmaps.Sections;
using OsuParsers.Encoders;
using OsuParsers.Helpers;

namespace OsuParsers.Beatmaps;

public class Beatmap
{
	public const int LATEST_OSZ_VERSION = 14;

	public int Version { get; set; } = 14;

	public BeatmapGeneralSection GeneralSection { get; set; } = new BeatmapGeneralSection();

	public BeatmapEditorSection EditorSection { get; set; } = new BeatmapEditorSection();

	public BeatmapMetadataSection MetadataSection { get; set; } = new BeatmapMetadataSection();

	public BeatmapDifficultySection DifficultySection { get; set; } = new BeatmapDifficultySection();

	public BeatmapEventsSection EventsSection { get; set; } = new BeatmapEventsSection();

	public BeatmapColoursSection ColoursSection { get; set; } = new BeatmapColoursSection();

	public List<TimingPoint> TimingPoints { get; set; } = new List<TimingPoint>();

	public List<HitObject> HitObjects { get; set; } = new List<HitObject>();

	public double BeatLengthAt(int offset)
	{
		if (TimingPoints.Count == 0)
		{
			return 0.0;
		}
		int num = 0;
		int num2 = 0;
		for (int i = 0; i < TimingPoints.Count; i++)
		{
			if (TimingPoints[i].Offset <= offset)
			{
				if (TimingPoints[i].Inherited)
				{
					num2 = i;
				}
				else
				{
					num = i;
				}
			}
		}
		double num3 = 1.0;
		if (num2 > num && TimingPoints[num2].BeatLength < 0.0)
		{
			num3 = MathHelper.CalculateBpmMultiplier(TimingPoints[num2]);
		}
		return TimingPoints[num].BeatLength * num3;
	}

	public void Save(string path)
	{
		File.WriteAllLines(path, BeatmapEncoder.Encode(this));
	}
}
