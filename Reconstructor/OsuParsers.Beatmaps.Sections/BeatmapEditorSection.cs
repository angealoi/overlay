using System;
using System.Collections.Generic;
using System.Linq;
using OsuParsers.Helpers;

namespace OsuParsers.Beatmaps.Sections;

public class BeatmapEditorSection
{
	public int[] Bookmarks { get; set; }

	public string BookmarksString
	{
		get
		{
			return Bookmarks.Join(',');
		}
		set
		{
			List<string> list = value.Split(',').ToList();
			Bookmarks = list.ConvertAll((string e) => Convert.ToInt32(e)).ToArray();
		}
	}

	public double DistanceSpacing { get; set; }

	public int BeatDivisor { get; set; }

	public int GridSize { get; set; }

	public float TimelineZoom { get; set; }
}
