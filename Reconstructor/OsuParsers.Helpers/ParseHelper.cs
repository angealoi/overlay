using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Numerics;
using OsuParsers.Enums;
using OsuParsers.Enums.Beatmaps;

namespace OsuParsers.Helpers;

internal static class ParseHelper
{
	public static FileSections GetCurrentSection(string line)
	{
		FileSections result = FileSections.None;
		Enum.TryParse<FileSections>(line.Trim('[', ']'), ignoreCase: true, out result);
		return result;
	}

	public static CurveType GetCurveType(char c)
	{
		return c switch
		{
			'C' => CurveType.Catmull, 
			'B' => CurveType.Bezier, 
			'L' => CurveType.Linear, 
			'P' => CurveType.PerfectCurve, 
			_ => CurveType.PerfectCurve, 
		};
	}

	public static List<Vector2> GetSliderPoints(string[] segments)
	{
		List<Vector2> list = new List<Vector2>();
		foreach (string item in segments.Skip(1))
		{
			string[] array = item.Split(':');
			if (array.Length == 2)
			{
				int num = Convert.ToInt32(array[0], CultureInfo.InvariantCulture);
				int num2 = Convert.ToInt32(array[1], CultureInfo.InvariantCulture);
				list.Add(new Vector2(num, num2));
			}
		}
		return list;
	}

	public static Color ParseColour(string line)
	{
		int[] array = (from c in line.Split(',')
			select Convert.ToInt32(c)).ToArray();
		return Color.FromArgb((array.Length == 4) ? array[3] : 255, array[0], array[1], array[2]);
	}

	public static bool IsLineValid(string line, FileSections currentSection)
	{
		switch (currentSection)
		{
		case FileSections.Format:
			return line.ToLower().Contains("osu file format v");
		case FileSections.General:
		case FileSections.Editor:
		case FileSections.Metadata:
		case FileSections.Difficulty:
		case FileSections.Fonts:
		case FileSections.Mania:
			return line.Contains(":");
		case FileSections.Events:
		case FileSections.TimingPoints:
		case FileSections.HitObjects:
			return line.Contains(",");
		case FileSections.Colours:
		case FileSections.CatchTheBeat:
			if (line.Contains(','))
			{
				return line.Contains(':');
			}
			return false;
		default:
			return false;
		}
	}

	public static bool ToBool(this string value)
	{
		if (!(value == "1"))
		{
			return value.ToLower() == "true";
		}
		return true;
	}

	public static float ToFloat(this string value)
	{
		return float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
	}

	public static double ToDouble(this string value)
	{
		return double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
	}
}
