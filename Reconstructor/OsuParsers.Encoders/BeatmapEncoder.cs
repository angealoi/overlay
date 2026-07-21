using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OsuParsers.Beatmaps;
using OsuParsers.Beatmaps.Objects;
using OsuParsers.Beatmaps.Sections;
using OsuParsers.Beatmaps.Sections.Events;
using OsuParsers.Enums;
using OsuParsers.Enums.Storyboards;
using OsuParsers.Helpers;
using OsuParsers.Storyboards.Interfaces;
using OsuParsers.Storyboards.Objects;

namespace OsuParsers.Encoders;

internal class BeatmapEncoder
{
	public static List<string> Encode(Beatmap beatmap)
	{
		List<List<string>> obj = new List<List<string>>
		{
			GeneralSection(beatmap.GeneralSection),
			EditorSection(beatmap.EditorSection),
			MetadataSection(beatmap.MetadataSection),
			DifficultySection(beatmap.DifficultySection),
			EventsSection(beatmap.EventsSection),
			TimingPoints(beatmap.TimingPoints),
			Colours(beatmap.ColoursSection),
			HitObjects(beatmap.HitObjects)
		};
		List<string> contents = new List<string> { $"osu file format v{beatmap.Version}" };
		obj.ForEach(delegate(List<string> stringList)
		{
			stringList.ForEach(delegate(string item)
			{
				contents.Add(item);
			});
		});
		return contents;
	}

	public static List<string> GeneralSection(BeatmapGeneralSection section)
	{
		List<string> list = WriteHelper.BaseListFormat("General");
		list.AddRange(new List<string>
		{
			"AudioFilename: " + section.AudioFilename,
			"AudioLeadIn: " + section.AudioLeadIn,
			"PreviewTime: " + section.PreviewTime,
			"Countdown: " + section.Countdown.ToInt32(),
			"SampleSet: " + section.SampleSet,
			"StackLeniency: " + section.StackLeniency.Format(),
			"Mode: " + (int)section.Mode,
			"LetterboxInBreaks: " + section.LetterboxInBreaks.ToInt32()
		});
		if (section.StoryFireInFront)
		{
			list.Add("StoryFireInFront: " + section.StoryFireInFront.ToInt32());
		}
		if (section.UseSkinSprites)
		{
			list.Add("UseSkinSprites: " + section.UseSkinSprites.ToInt32());
		}
		if (section.EpilepsyWarning)
		{
			list.Add("EpilepsyWarning: " + section.EpilepsyWarning.ToInt32());
		}
		if (section.Mode == Ruleset.Mania)
		{
			list.Add("SpecialStyle: " + section.SpecialStyle.ToInt32());
		}
		list.Add("WidescreenStoryboard: " + section.WidescreenStoryboard.ToInt32());
		return list;
	}

	public static List<string> EditorSection(BeatmapEditorSection section)
	{
		List<string> list = WriteHelper.BaseListFormat("Editor");
		if (section.Bookmarks != null)
		{
			list.Add("Bookmarks: " + section.BookmarksString);
		}
		list.AddRange(new List<string>
		{
			"DistanceSpacing: " + section.DistanceSpacing.Format(),
			"BeatDivisor: " + section.BeatDivisor.Format(),
			"GridSize: " + section.GridSize.Format(),
			"TimelineZoom: " + section.TimelineZoom.Format()
		});
		return list;
	}

	public static List<string> MetadataSection(BeatmapMetadataSection section)
	{
		return new List<string>
		{
			string.Empty,
			"[Metadata]",
			"Title:" + section.Title,
			"TitleUnicode:" + section.TitleUnicode,
			"Artist:" + section.Artist,
			"ArtistUnicode:" + section.ArtistUnicode,
			"Creator:" + section.Creator,
			"Version:" + section.Version,
			"Source:" + section.Source,
			"Tags:" + section.TagsString,
			"BeatmapID:" + section.BeatmapID,
			"BeatmapSetID:" + section.BeatmapSetID
		};
	}

	public static List<string> DifficultySection(BeatmapDifficultySection section)
	{
		return new List<string>
		{
			string.Empty,
			"[Difficulty]",
			"HPDrainRate:" + section.HPDrainRate.Format(),
			"CircleSize:" + section.CircleSize.Format(),
			"OverallDifficulty:" + section.OverallDifficulty.Format(),
			"ApproachRate:" + section.ApproachRate.Format(),
			"SliderMultiplier:" + section.SliderMultiplier.Format(),
			"SliderTickRate:" + section.SliderTickRate.Format()
		};
	}

	public static List<string> EventsSection(BeatmapEventsSection section)
	{
		List<string> list = WriteHelper.BaseListFormat("Events");
		list.AddRange(new List<string>
		{
			"//Background and Video events",
			"0,0,\"" + section.BackgroundImage + "\",0,0"
		});
		if (section.Video != null)
		{
			list.Add($"Video,{section.VideoOffset},\"{section.Video}\"");
		}
		list.Add("//Break Periods");
		if (section.Breaks.Any())
		{
			list.AddRange(section.Breaks.ConvertAll((BeatmapBreakEvent b) => $"2,{b.StartTime},{b.EndTime}"));
		}
		list.Add("//Storyboard Layer 0 (Background)");
		section.Storyboard.BackgroundLayer.ForEach(delegate(IStoryboardObject sbObject)
		{
			list.AddRange(WriteHelper.StoryboardObject(sbObject, StoryboardLayer.Background));
		});
		list.Add("//Storyboard Layer 1 (Fail)");
		section.Storyboard.FailLayer.ForEach(delegate(IStoryboardObject sbObject)
		{
			list.AddRange(WriteHelper.StoryboardObject(sbObject, StoryboardLayer.Fail));
		});
		list.Add("//Storyboard Layer 2 (Pass)");
		section.Storyboard.PassLayer.ForEach(delegate(IStoryboardObject sbObject)
		{
			list.AddRange(WriteHelper.StoryboardObject(sbObject, StoryboardLayer.Pass));
		});
		list.Add("//Storyboard Layer 3 (Foreground)");
		section.Storyboard.ForegroundLayer.ForEach(delegate(IStoryboardObject sbObject)
		{
			list.AddRange(WriteHelper.StoryboardObject(sbObject, StoryboardLayer.Foreground));
		});
		list.Add("//Storyboard Layer 4 (Overlay)");
		section.Storyboard.OverlayLayer.ForEach(delegate(IStoryboardObject sbObject)
		{
			list.AddRange(WriteHelper.StoryboardObject(sbObject, StoryboardLayer.Overlay));
		});
		list.Add("//Storyboard Sound Samples");
		section.Storyboard.SamplesLayer.ForEach(delegate(IStoryboardObject sbObject)
		{
			list.AddRange(WriteHelper.StoryboardObject(sbObject, (sbObject as StoryboardSample).Layer));
		});
		return list;
	}

	public static List<string> TimingPoints(List<TimingPoint> timingPoints)
	{
		if (timingPoints.Count == 0)
		{
			return new List<string>();
		}
		List<string> list = WriteHelper.BaseListFormat("TimingPoints");
		if (timingPoints != null)
		{
			list.AddRange(timingPoints.ConvertAll((TimingPoint point) => WriteHelper.TimingPoint(point)));
		}
		list.Add(string.Empty);
		return list;
	}

	public static List<string> Colours(BeatmapColoursSection section)
	{
		if (section.ComboColours.Count == 0 && section.SliderTrackOverride == default(Color) && section.SliderBorder == default(Color))
		{
			return new List<string>();
		}
		List<string> list = WriteHelper.BaseListFormat("Colours");
		if (section.ComboColours != null)
		{
			for (int i = 0; i < section.ComboColours.Count; i++)
			{
				list.Add($"Combo{i + 1} : {WriteHelper.Colour(section.ComboColours[i])}");
			}
		}
		if (section.SliderTrackOverride != default(Color))
		{
			list.Add("SliderTrackOverride : " + WriteHelper.Colour(section.SliderTrackOverride));
		}
		if (section.SliderBorder != default(Color))
		{
			list.Add("SliderBorder : " + WriteHelper.Colour(section.SliderBorder));
		}
		return list;
	}

	public static List<string> HitObjects(List<HitObject> hitObjects)
	{
		List<string> list = WriteHelper.BaseListFormat("HitObjects");
		if (hitObjects != null)
		{
			list.AddRange(hitObjects.ConvertAll((HitObject obj) => WriteHelper.HitObject(obj)));
		}
		return list;
	}
}
