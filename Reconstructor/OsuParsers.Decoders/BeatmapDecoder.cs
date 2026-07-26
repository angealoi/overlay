using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using OsuParsers.Beatmaps;
using OsuParsers.Beatmaps.Objects;
using OsuParsers.Enums;
using OsuParsers.Enums.Beatmaps;
using OsuParsers.Helpers;

namespace OsuParsers.Decoders;

/// <summary>
/// AimAssist용 최소 .osu 디코더.
/// HitObjects / TimingPoints / StackLeniency / SliderMultiplier 만 실제로 쓰인다.
/// Storyboard·DB·Replay·논스탠다드 모드 타입은 포함하지 않는다.
/// </summary>
public static class BeatmapDecoder
{
	private static Beatmap Beatmap;

	private static FileSections currentSection = FileSections.None;

	public static Beatmap Decode(string path)
	{
		if (File.Exists(path))
			return Decode(File.ReadAllLines(path));
		throw new FileNotFoundException();
	}

	public static Beatmap Decode(IEnumerable<string> lines)
	{
		Beatmap = new Beatmap();
		currentSection = FileSections.Format;
		foreach (string line in lines)
		{
			if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//"))
				continue;
			if (ParseHelper.GetCurrentSection(line) != FileSections.None)
			{
				currentSection = ParseHelper.GetCurrentSection(line);
			}
			else if (ParseHelper.IsLineValid(line, currentSection))
			{
				ParseLine(line);
			}
		}
		// null 히트오브젝트(미지원 Hold 등) 제거
		Beatmap.HitObjects.RemoveAll(h => h == null);
		Beatmap.GeneralSection.CirclesCount = Beatmap.HitObjects.Count(c => c is HitCircle);
		Beatmap.GeneralSection.SlidersCount = Beatmap.HitObjects.Count(c => c is Slider);
		Beatmap.GeneralSection.SpinnersCount = Beatmap.HitObjects.Count(c => c is Spinner);
		Beatmap.GeneralSection.Length = Beatmap.HitObjects.Any() ? Beatmap.HitObjects.Last().EndTime : 0;
		return Beatmap;
	}

	public static Beatmap Decode(Stream stream)
	{
		return Decode(stream.ReadAllLines());
	}

	private static void ParseLine(string line)
	{
		switch (currentSection)
		{
		case FileSections.Format:
			Beatmap.Version = Convert.ToInt32(line.Split(new string[1] { "osu file format v" }, StringSplitOptions.None)[1]);
			break;
		case FileSections.General:
			ParseGeneral(line);
			break;
		case FileSections.Difficulty:
			ParseDifficulty(line);
			break;
		case FileSections.TimingPoints:
			ParseTimingPoints(line);
			break;
		case FileSections.HitObjects:
			ParseHitObjects(line);
			break;
		// Editor / Metadata / Events / Colours — AimAssist 미사용, 스킵
		}
	}

	private static void ParseGeneral(string line)
	{
		int num = line.IndexOf(':');
		string text = line.Remove(num).Trim();
		string text2 = line.Remove(0, num + 1).Trim();
		switch (text)
		{
		case "StackLeniency":
			Beatmap.GeneralSection.StackLeniency = text2.ToDouble();
			break;
		case "Mode":
			Beatmap.GeneralSection.Mode = (Ruleset)Enum.Parse(typeof(Ruleset), text2);
			Beatmap.GeneralSection.ModeId = Convert.ToInt32(text2);
			break;
		}
	}

	private static void ParseDifficulty(string line)
	{
		int num = line.IndexOf(':');
		string text = line.Remove(num).Trim();
		string value = line.Remove(0, num + 1).Trim();
		switch (text)
		{
		case "HPDrainRate":
			Beatmap.DifficultySection.HPDrainRate = value.ToFloat();
			break;
		case "CircleSize":
			Beatmap.DifficultySection.CircleSize = value.ToFloat();
			break;
		case "OverallDifficulty":
			Beatmap.DifficultySection.OverallDifficulty = value.ToFloat();
			break;
		case "ApproachRate":
			Beatmap.DifficultySection.ApproachRate = value.ToFloat();
			break;
		case "SliderMultiplier":
			Beatmap.DifficultySection.SliderMultiplier = value.ToDouble();
			break;
		case "SliderTickRate":
			Beatmap.DifficultySection.SliderTickRate = value.ToDouble();
			break;
		}
	}

	private static void ParseTimingPoints(string line)
	{
		string[] array = line.Split(',');
		int offset = (int)array[0].ToFloat();
		double beatLength = array[1].ToDouble();
		TimeSignature timeSignature = TimeSignature.SimpleQuadruple;
		SampleSet sampleSet = SampleSet.None;
		int customSampleSet = 0;
		int volume = 100;
		bool inherited = true;
		Effects effects = Effects.None;
		if (array.Length >= 3)
			timeSignature = (TimeSignature)Convert.ToInt32(array[2]);
		if (array.Length >= 4)
			sampleSet = (SampleSet)Convert.ToInt32(array[3]);
		if (array.Length >= 5)
			customSampleSet = Convert.ToInt32(array[4]);
		if (array.Length >= 6)
			volume = Convert.ToInt32(array[5]);
		if (array.Length >= 7)
			inherited = !array[6].ToBool();
		if (array.Length >= 8)
			effects = (Effects)Convert.ToInt32(array[7]);
		Beatmap.TimingPoints.Add(new TimingPoint
		{
			Offset = offset,
			BeatLength = beatLength,
			TimeSignature = timeSignature,
			SampleSet = sampleSet,
			CustomSampleSet = customSampleSet,
			Volume = volume,
			Inherited = inherited,
			Effects = effects
		});
	}

	private static void ParseHitObjects(string line)
	{
		string[] array = line.Split(',');
		Vector2 position = new Vector2(array[0].ToFloat(), array[1].ToFloat());
		int num = Convert.ToInt32(array[2]);
		HitObjectType hitObjectType = (HitObjectType)int.Parse(array[3]);
		int comboOffset = (int)(hitObjectType & HitObjectType.ComboOffset) >> 4;
		hitObjectType &= (HitObjectType)(-113);
		bool isNewCombo = hitObjectType.HasFlag(HitObjectType.NewCombo);
		hitObjectType &= (HitObjectType)(-5);
		HitSoundType hitSound = (HitSoundType)Convert.ToInt32(array[4]);
		HitObject item = null;
		string[] array2 = array.Last().Split(':');
		int num2 = (hitObjectType.HasFlag(HitObjectType.Hold) ? 1 : 0);
		Extras extras = (array.Last().Contains(":") ? new Extras
		{
			SampleSet = (SampleSet)Convert.ToInt32(array2[num2]),
			AdditionSet = (SampleSet)Convert.ToInt32(array2[1 + num2]),
			CustomIndex = ((array2.Length > 2 + num2) ? Convert.ToInt32(array2[2 + num2]) : 0),
			Volume = ((array2.Length > 3 + num2) ? Convert.ToInt32(array2[3 + num2]) : 0),
			SampleFileName = ((array2.Length > 4 + num2) ? array2[4 + num2] : string.Empty)
		} : new Extras());

		// 스탠다드 타입만 생성 — AimAssist는 osu!standard 전용.
		switch (hitObjectType)
		{
		case HitObjectType.Circle:
			item = new HitCircle(position, num, num, hitSound, extras, isNewCombo, comboOffset);
			break;
		case HitObjectType.Slider:
		{
			CurveType curveType = ParseHelper.GetCurveType(array[5].Split('|')[0][0]);
			List<Vector2> sliderPoints = ParseHelper.GetSliderPoints(array[5].Split('|'));
			int repeats = Convert.ToInt32(array[6]);
			double pixelLength = array[7].ToDouble();
			int endTime3 = MathHelper.CalculateEndTime(Beatmap, num, repeats, pixelLength);
			List<HitSoundType> edgeHitSounds = null;
			if (array.Length > 8 && array[8].Length > 0)
			{
				edgeHitSounds = Array.ConvertAll(array[8].Split('|'), (string s) => (HitSoundType)Convert.ToInt32(s)).ToList();
			}
			List<Tuple<SampleSet, SampleSet>> list = null;
			if (array.Length > 9 && array[9].Length > 0)
			{
				list = new List<Tuple<SampleSet, SampleSet>>();
				foreach (string text in array[9].Split('|'))
				{
					list.Add(new Tuple<SampleSet, SampleSet>(
						(SampleSet)Convert.ToInt32(text.Split(':').First()),
						(SampleSet)Convert.ToInt32(text.Split(':').Last())));
				}
			}
			item = new Slider(position, num, endTime3, hitSound, curveType, sliderPoints, repeats, pixelLength, isNewCombo, comboOffset, edgeHitSounds, list, extras);
			break;
		}
		case HitObjectType.Spinner:
		{
			int endTime2 = Convert.ToInt32(array[5].Trim());
			item = new Spinner(position, num, endTime2, hitSound, extras, isNewCombo, comboOffset);
			break;
		}
		}

		if (item != null)
		{
			item.Type = (int)hitObjectType & 0x0B;
			item.BasePosition = position;
			Beatmap.HitObjects.Add(item);
		}
	}
}
