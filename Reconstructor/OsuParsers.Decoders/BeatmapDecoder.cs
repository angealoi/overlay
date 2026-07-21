using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using OsuParsers.Beatmaps;
using OsuParsers.Beatmaps.Objects;
using OsuParsers.Beatmaps.Objects.Catch;
using OsuParsers.Beatmaps.Objects.Mania;
using OsuParsers.Beatmaps.Objects.Taiko;
using OsuParsers.Beatmaps.Sections.Events;
using OsuParsers.Enums;
using OsuParsers.Enums.Beatmaps;
using OsuParsers.Enums.Storyboards;
using OsuParsers.Helpers;

namespace OsuParsers.Decoders;

public static class BeatmapDecoder
{
	private static Beatmap Beatmap;

	private static FileSections currentSection = FileSections.None;

	private static List<string> sbLines = new List<string>();

	public static Beatmap Decode(string path)
	{
		if (File.Exists(path))
		{
			return Decode(File.ReadAllLines(path));
		}
		throw new FileNotFoundException();
	}

	public static Beatmap Decode(IEnumerable<string> lines)
	{
		Beatmap = new Beatmap();
		currentSection = FileSections.Format;
		sbLines.Clear();
		foreach (string line in lines)
		{
			if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("//"))
			{
				if (ParseHelper.GetCurrentSection(line) != FileSections.None)
				{
					currentSection = ParseHelper.GetCurrentSection(line);
				}
				else if (ParseHelper.IsLineValid(line, currentSection))
				{
					ParseLine(line);
				}
			}
		}
		Beatmap.EventsSection.Storyboard = StoryboardDecoder.Decode(sbLines.ToArray());
		Beatmap.GeneralSection.CirclesCount = Beatmap.HitObjects.Count((HitObject c) => c is HitCircle || c is TaikoHit || c is ManiaNote || c is CatchFruit);
		Beatmap.GeneralSection.SlidersCount = Beatmap.HitObjects.Count((HitObject c) => c is Slider || c is TaikoDrumroll || c is ManiaHoldNote || c is CatchJuiceStream);
		Beatmap.GeneralSection.SpinnersCount = Beatmap.HitObjects.Count((HitObject c) => c is Spinner || c is TaikoSpinner || c is CatchBananaRain);
		Beatmap.GeneralSection.Length = (Beatmap.HitObjects.Any() ? Beatmap.HitObjects.Last().EndTime : 0);
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
		case FileSections.Editor:
			ParseEditor(line);
			break;
		case FileSections.Metadata:
			ParseMetadata(line);
			break;
		case FileSections.Difficulty:
			ParseDifficulty(line);
			break;
		case FileSections.Events:
			ParseEvents(line);
			break;
		case FileSections.TimingPoints:
			ParseTimingPoints(line);
			break;
		case FileSections.Colours:
			ParseColours(line);
			break;
		case FileSections.HitObjects:
			ParseHitObjects(line);
			break;
		}
	}

	private static void ParseGeneral(string line)
	{
		int num = line.IndexOf(':');
		string text = line.Remove(num).Trim();
		string text2 = line.Remove(0, num + 1).Trim();
		if (text == null)
		{
			return;
		}
		switch (text.Length)
		{
		case 13:
			switch (text[0])
			{
			case 'A':
				if (text == "AudioFilename")
				{
					Beatmap.GeneralSection.AudioFilename = text2;
				}
				break;
			case 'S':
				if (text == "StackLeniency")
				{
					Beatmap.GeneralSection.StackLeniency = text2.ToDouble();
				}
				break;
			}
			break;
		case 11:
			switch (text[0])
			{
			case 'A':
				if (text == "AudioLeadIn")
				{
					Beatmap.GeneralSection.AudioLeadIn = Convert.ToInt32(text2);
				}
				break;
			case 'P':
				if (text == "PreviewTime")
				{
					Beatmap.GeneralSection.PreviewTime = Convert.ToInt32(text2);
				}
				break;
			}
			break;
		case 9:
			switch (text[0])
			{
			case 'C':
				if (text == "Countdown")
				{
					Beatmap.GeneralSection.Countdown = text2.ToBool();
				}
				break;
			case 'S':
				if (text == "SampleSet")
				{
					Beatmap.GeneralSection.SampleSet = (SampleSet)Enum.Parse(typeof(SampleSet), text2);
				}
				break;
			}
			break;
		case 4:
			if (text == "Mode")
			{
				Beatmap.GeneralSection.Mode = (Ruleset)Enum.Parse(typeof(Ruleset), text2);
				Beatmap.GeneralSection.ModeId = Convert.ToInt32(text2);
			}
			break;
		case 17:
			if (text == "LetterboxInBreaks")
			{
				Beatmap.GeneralSection.LetterboxInBreaks = text2.ToBool();
			}
			break;
		case 20:
			if (text == "WidescreenStoryboard")
			{
				Beatmap.GeneralSection.WidescreenStoryboard = text2.ToBool();
			}
			break;
		case 16:
			if (text == "StoryFireInFront")
			{
				Beatmap.GeneralSection.StoryFireInFront = text2.ToBool();
			}
			break;
		case 12:
			if (text == "SpecialStyle")
			{
				Beatmap.GeneralSection.SpecialStyle = text2.ToBool();
			}
			break;
		case 15:
			if (text == "EpilepsyWarning")
			{
				Beatmap.GeneralSection.EpilepsyWarning = text2.ToBool();
			}
			break;
		case 14:
			if (text == "UseSkinSprites")
			{
				Beatmap.GeneralSection.UseSkinSprites = text2.ToBool();
			}
			break;
		case 5:
		case 6:
		case 7:
		case 8:
		case 10:
		case 18:
		case 19:
			break;
		}
	}

	private static void ParseEditor(string line)
	{
		int num = line.IndexOf(':');
		string text = line.Remove(num).Trim();
		string text2 = line.Remove(0, num + 1).Trim();
		switch (text)
		{
		case "Bookmarks":
			Beatmap.EditorSection.Bookmarks = (from b in text2.Split(new char[1] { ',' }, StringSplitOptions.RemoveEmptyEntries)
				select Convert.ToInt32(b)).ToArray();
			break;
		case "DistanceSpacing":
			Beatmap.EditorSection.DistanceSpacing = text2.ToDouble();
			break;
		case "BeatDivisor":
			Beatmap.EditorSection.BeatDivisor = Convert.ToInt32(text2);
			break;
		case "GridSize":
			Beatmap.EditorSection.GridSize = Convert.ToInt32(text2);
			break;
		case "TimelineZoom":
			Beatmap.EditorSection.TimelineZoom = text2.ToFloat();
			break;
		}
	}

	private static void ParseMetadata(string line)
	{
		int num = line.IndexOf(':');
		string text = line.Remove(num).Trim();
		string text2 = line.Remove(0, num + 1).Trim();
		if (text == null)
		{
			return;
		}
		switch (text.Length)
		{
		case 12:
			switch (text[0])
			{
			case 'T':
				if (text == "TitleUnicode")
				{
					Beatmap.MetadataSection.TitleUnicode = text2;
				}
				break;
			case 'B':
				if (text == "BeatmapSetID")
				{
					Beatmap.MetadataSection.BeatmapSetID = Convert.ToInt32(text2);
				}
				break;
			}
			break;
		case 6:
			switch (text[0])
			{
			case 'A':
				if (text == "Artist")
				{
					Beatmap.MetadataSection.Artist = text2;
				}
				break;
			case 'S':
				if (text == "Source")
				{
					Beatmap.MetadataSection.Source = text2;
				}
				break;
			}
			break;
		case 7:
			switch (text[0])
			{
			case 'C':
				if (text == "Creator")
				{
					Beatmap.MetadataSection.Creator = text2;
				}
				break;
			case 'V':
				if (text == "Version")
				{
					Beatmap.MetadataSection.Version = text2;
				}
				break;
			}
			break;
		case 5:
			if (text == "Title")
			{
				Beatmap.MetadataSection.Title = text2;
			}
			break;
		case 13:
			if (text == "ArtistUnicode")
			{
				Beatmap.MetadataSection.ArtistUnicode = text2;
			}
			break;
		case 4:
			if (text == "Tags")
			{
				Beatmap.MetadataSection.Tags = text2.Split(new char[2] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
			}
			break;
		case 9:
			if (text == "BeatmapID")
			{
				Beatmap.MetadataSection.BeatmapID = Convert.ToInt32(text2);
			}
			break;
		case 8:
		case 10:
		case 11:
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

	private static void ParseEvents(string line)
	{
		string[] array = line.Split(',');
		EventType eventType;
		if (Enum.TryParse<EventType>(array[0], out var _))
		{
			eventType = (EventType)Enum.Parse(typeof(EventType), array[0]);
		}
		else
		{
			if (!line.StartsWith(" ") && !line.StartsWith("_"))
			{
				return;
			}
			eventType = EventType.StoryboardCommand;
		}
		switch (eventType)
		{
		case EventType.Background:
			Beatmap.EventsSection.BackgroundImage = array[2].Trim('"');
			break;
		case EventType.Video:
			Beatmap.EventsSection.Video = array[2].Trim('"');
			Beatmap.EventsSection.VideoOffset = Convert.ToInt32(array[1]);
			break;
		case EventType.Break:
			Beatmap.EventsSection.Breaks.Add(new BeatmapBreakEvent(Convert.ToInt32(array[1]), Convert.ToInt32(array[2])));
			break;
		case EventType.Sprite:
		case EventType.Sample:
		case EventType.Animation:
		case EventType.StoryboardCommand:
			sbLines.Add(line);
			break;
		case EventType.Colour:
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
		{
			timeSignature = (TimeSignature)Convert.ToInt32(array[2]);
		}
		if (array.Length >= 4)
		{
			sampleSet = (SampleSet)Convert.ToInt32(array[3]);
		}
		if (array.Length >= 5)
		{
			customSampleSet = Convert.ToInt32(array[4]);
		}
		if (array.Length >= 6)
		{
			volume = Convert.ToInt32(array[5]);
		}
		if (array.Length >= 7)
		{
			inherited = !array[6].ToBool();
		}
		if (array.Length >= 8)
		{
			effects = (Effects)Convert.ToInt32(array[7]);
		}
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

	private static void ParseColours(string line)
	{
		int num = line.IndexOf(':');
		string text = line.Remove(num).Trim();
		string line2 = line.Remove(0, num + 1).Trim();
		if (!(text == "SliderTrackOverride"))
		{
			if (text == "SliderBorder")
			{
				Beatmap.ColoursSection.SliderBorder = ParseHelper.ParseColour(line2);
			}
			else
			{
				Beatmap.ColoursSection.ComboColours.Add(ParseHelper.ParseColour(line2));
			}
		}
		else
		{
			Beatmap.ColoursSection.SliderTrackOverride = ParseHelper.ParseColour(line2);
		}
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
		switch (hitObjectType)
		{
		case HitObjectType.Circle:
			if (Beatmap.GeneralSection.Mode == Ruleset.Standard)
			{
				item = new HitCircle(position, num, num, hitSound, extras, isNewCombo, comboOffset);
			}
			else if (Beatmap.GeneralSection.Mode == Ruleset.Taiko)
			{
				item = new TaikoHit(position, num, num, hitSound, extras, isNewCombo, comboOffset);
			}
			else if (Beatmap.GeneralSection.Mode == Ruleset.Fruits)
			{
				item = new CatchFruit(position, num, num, hitSound, extras, isNewCombo, comboOffset);
			}
			else if (Beatmap.GeneralSection.Mode == Ruleset.Mania)
			{
				item = new ManiaNote(position, num, num, hitSound, extras, isNewCombo, comboOffset);
			}
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
				edgeHitSounds = new List<HitSoundType>();
				edgeHitSounds = Array.ConvertAll(array[8].Split('|'), (string s) => (HitSoundType)Convert.ToInt32(s)).ToList();
			}
			List<Tuple<SampleSet, SampleSet>> list = null;
			if (array.Length > 9 && array[9].Length > 0)
			{
				list = new List<Tuple<SampleSet, SampleSet>>();
				string[] array3 = array[9].Split('|');
				foreach (string text in array3)
				{
					list.Add(new Tuple<SampleSet, SampleSet>((SampleSet)Convert.ToInt32(text.Split(':').First()), (SampleSet)Convert.ToInt32(text.Split(':').Last())));
				}
			}
			if (Beatmap.GeneralSection.Mode == Ruleset.Standard)
			{
				item = new Slider(position, num, endTime3, hitSound, curveType, sliderPoints, repeats, pixelLength, isNewCombo, comboOffset, edgeHitSounds, list, extras);
			}
			else if (Beatmap.GeneralSection.Mode == Ruleset.Taiko)
			{
				item = new TaikoDrumroll(position, num, endTime3, hitSound, curveType, sliderPoints, repeats, pixelLength, edgeHitSounds, list, extras, isNewCombo, comboOffset);
			}
			else if (Beatmap.GeneralSection.Mode == Ruleset.Fruits)
			{
				item = new CatchJuiceStream(position, num, endTime3, hitSound, curveType, sliderPoints, repeats, pixelLength, isNewCombo, comboOffset, edgeHitSounds, list, extras);
			}
			else if (Beatmap.GeneralSection.Mode == Ruleset.Mania)
			{
				item = new ManiaHoldNote(position, num, endTime3, hitSound, extras, isNewCombo, comboOffset);
			}
			break;
		}
		case HitObjectType.Spinner:
		{
			int endTime2 = Convert.ToInt32(array[5].Trim());
			if (Beatmap.GeneralSection.Mode == Ruleset.Standard)
			{
				item = new Spinner(position, num, endTime2, hitSound, extras, isNewCombo, comboOffset);
			}
			else if (Beatmap.GeneralSection.Mode == Ruleset.Taiko)
			{
				item = new TaikoSpinner(position, num, endTime2, hitSound, extras, isNewCombo, comboOffset);
			}
			else if (Beatmap.GeneralSection.Mode == Ruleset.Fruits)
			{
				item = new CatchBananaRain(position, num, endTime2, hitSound, extras, isNewCombo, comboOffset);
			}
			break;
		}
		case HitObjectType.Hold:
		{
			int endTime = Convert.ToInt32(array[5].Split(':')[0].Trim());
			item = new ManiaHoldNote(position, num, endTime, hitSound, extras, isNewCombo, comboOffset);
			break;
		}
		}
		// stacking 알고리즘용 — Type 비트마스크(1=circle, 2=slider, 8=spinner)와
		// BasePosition(stack 적용 전 원본 위치)을 채운다. osu! stable UpdateStacking이
		// BasePosition 기준으로 겹침을 판정하고 Position을 덮어쓴다.
		if (item != null)
		{
			item.Type = (int)hitObjectType & 0x0B; // NewCombo 비트(4)와 ColourHax 제외 — circle/slider/spinner만
			item.BasePosition = position;
		}
		Beatmap.HitObjects.Add(item);
	}
}
