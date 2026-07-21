using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using OsuParsers.Enums.Storyboards;
using OsuParsers.Helpers;
using OsuParsers.Storyboards;
using OsuParsers.Storyboards.Commands;
using OsuParsers.Storyboards.Interfaces;
using OsuParsers.Storyboards.Objects;

namespace OsuParsers.Decoders;

public static class StoryboardDecoder
{
	private static Storyboard storyboard;

	private static IStoryboardObject lastDrawable;

	private static CommandGroup commandGroup;

	public static Storyboard Decode(string path)
	{
		if (File.Exists(path))
		{
			return Decode(File.ReadAllLines(path));
		}
		throw new FileNotFoundException();
	}

	public static Storyboard Decode(IEnumerable<string> lines)
	{
		storyboard = new Storyboard();
		lastDrawable = null;
		commandGroup = null;
		foreach (string line in lines)
		{
			if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//") || line.StartsWith("["))
			{
				continue;
			}
			if (line.StartsWith("$"))
			{
				string[] array = line.Split('=');
				if (array.Length == 2)
				{
					storyboard.Variables.Add(array[0], array[1]);
				}
			}
			else if (!line.StartsWith(" ") && !line.StartsWith("_"))
			{
				ParseSbObject(ParseVariables(line));
			}
			else
			{
				ParseSbCommand(ParseVariables(line));
			}
		}
		return storyboard;
	}

	public static Storyboard Decode(Stream stream)
	{
		return Decode(stream.ReadAllLines());
	}

	private static string ParseVariables(string line)
	{
		if (storyboard.Variables == null || line.IndexOf('$') < 0)
		{
			return line;
		}
		foreach (KeyValuePair<string, string> variable in storyboard.Variables)
		{
			line = line.Replace(variable.Key, variable.Value);
		}
		return line;
	}

	private static void ParseSbObject(string line)
	{
		string[] array = line.Split(',');
		if (Enum.TryParse<EventType>(array[0], out var result))
		{
			StoryboardLayer layer = (StoryboardLayer)Enum.Parse(typeof(StoryboardLayer), array[(result != EventType.Sample) ? 1 : 2]);
			switch (result)
			{
			case EventType.Sprite:
			{
				Origins origin2 = (Origins)Enum.Parse(typeof(Origins), array[2]);
				string filePath3 = array[3].Trim('"');
				float x2 = array[4].ToFloat();
				float y2 = array[5].ToFloat();
				storyboard.GetLayer(layer).Add(new StoryboardSprite(origin2, filePath3, x2, y2));
				lastDrawable = storyboard.GetLayer(layer).Last();
				break;
			}
			case EventType.Animation:
			{
				Origins origin = (Origins)Enum.Parse(typeof(Origins), array[2]);
				string filePath2 = array[3].Trim('"');
				float x = array[4].ToFloat();
				float y = array[5].ToFloat();
				int frameCount = Convert.ToInt32(array[6]);
				double frameDelay = array[7].ToDouble();
				LoopType loopType = ((array.Length > 8) ? ((LoopType)Enum.Parse(typeof(LoopType), array[8])) : LoopType.LoopForever);
				storyboard.GetLayer(layer).Add(new StoryboardAnimation(origin, filePath2, x, y, frameCount, frameDelay, loopType));
				lastDrawable = storyboard.GetLayer(layer).Last();
				break;
			}
			case EventType.Sample:
			{
				int time = Convert.ToInt32(array[1]);
				string filePath = array[3].Trim('"');
				int volume = ((array.Length > 4) ? Convert.ToInt32(array[4]) : 100);
				storyboard.SamplesLayer.Add(new StoryboardSample(layer, time, filePath, volume));
				break;
			}
			}
		}
	}

	private static void ParseSbCommand(string line)
	{
		int num = 0;
		while (line.StartsWith(" ") || line.StartsWith("_"))
		{
			num++;
			line = line.Substring(1);
		}
		if (num < 2)
		{
			if (lastDrawable == null)
			{
				return;
			}
			commandGroup = (lastDrawable as IHasCommands).Commands;
		}
		string[] array = line.Split(',');
		string text = array[0];
		if (!(text == "T"))
		{
			if (text == "L")
			{
				int startTime = Convert.ToInt32(array[1]);
				int loopCount = Convert.ToInt32(array[2]);
				commandGroup = commandGroup.AddLoop(startTime, loopCount).Commands;
				return;
			}
			if (string.IsNullOrEmpty(array[3]))
			{
				array[3] = array[2];
			}
			Easing easing = (Easing)Convert.ToInt32(array[1]);
			int startTime2 = Convert.ToInt32(array[2]);
			int endTime = Convert.ToInt32(array[3]);
			switch (text)
			{
			case "F":
			{
				float num11 = array[4].ToFloat();
				float endValue2 = ((array.Length > 5) ? array[5].ToFloat() : num11);
				commandGroup.Commands.Add(new Command(CommandType.Fade, easing, startTime2, endTime, num11, endValue2));
				break;
			}
			case "M":
			{
				float num14 = array[4].ToFloat();
				float num15 = array[5].ToFloat();
				float x2 = ((array.Length > 6) ? array[6].ToFloat() : num14);
				float y2 = ((array.Length > 7) ? array[7].ToFloat() : num15);
				commandGroup.Commands.Add(new Command(CommandType.Movement, easing, startTime2, endTime, new Vector2(num14, num15), new Vector2(x2, y2)));
				break;
			}
			case "MX":
			{
				float num13 = array[4].ToFloat();
				float endValue4 = ((array.Length > 5) ? array[5].ToFloat() : num13);
				commandGroup.Commands.Add(new Command(CommandType.MovementX, easing, startTime2, endTime, num13, endValue4));
				break;
			}
			case "MY":
			{
				float num12 = array[4].ToFloat();
				float endValue3 = ((array.Length > 5) ? array[5].ToFloat() : num12);
				commandGroup.Commands.Add(new Command(CommandType.MovementY, easing, startTime2, endTime, num12, endValue3));
				break;
			}
			case "S":
			{
				float num16 = array[4].ToFloat();
				float endValue5 = ((array.Length > 5) ? array[5].ToFloat() : num16);
				commandGroup.Commands.Add(new Command(CommandType.Scale, easing, startTime2, endTime, num16, endValue5));
				break;
			}
			case "V":
			{
				float num8 = array[4].ToFloat();
				float num9 = array[5].ToFloat();
				float x = ((array.Length > 6) ? array[6].ToFloat() : num8);
				float y = ((array.Length > 7) ? array[7].ToFloat() : num9);
				commandGroup.Commands.Add(new Command(CommandType.VectorScale, easing, startTime2, endTime, new Vector2(num8, num9), new Vector2(x, y)));
				break;
			}
			case "R":
			{
				float num10 = array[4].ToFloat();
				float endValue = ((array.Length > 5) ? array[5].ToFloat() : num10);
				commandGroup.Commands.Add(new Command(CommandType.Rotation, easing, startTime2, endTime, num10, endValue));
				break;
			}
			case "C":
			{
				float num2 = array[4].ToFloat();
				float num3 = array[5].ToFloat();
				float num4 = array[6].ToFloat();
				float num5 = ((array.Length > 7) ? array[7].ToFloat() : num2);
				float num6 = ((array.Length > 8) ? array[8].ToFloat() : num3);
				float num7 = ((array.Length > 9) ? array[9].ToFloat() : num4);
				commandGroup.Commands.Add(new Command(easing, startTime2, endTime, Color.FromArgb(255, (int)num2, (int)num3, (int)num4), Color.FromArgb(255, (int)num5, (int)num6, (int)num7)));
				break;
			}
			case "P":
				switch (array[4])
				{
				case "H":
					commandGroup.Commands.Add(new Command(CommandType.FlipHorizontal, easing, startTime2, endTime));
					break;
				case "V":
					commandGroup.Commands.Add(new Command(CommandType.FlipVertical, easing, startTime2, endTime));
					break;
				case "A":
					commandGroup.Commands.Add(new Command(CommandType.BlendingMode, easing, startTime2, endTime));
					break;
				}
				break;
			}
		}
		else
		{
			string triggerName = array[1];
			int startTime3 = ((array.Length > 2) ? Convert.ToInt32(array[2]) : 0);
			int endTime2 = ((array.Length > 3) ? Convert.ToInt32(array[3]) : 0);
			int groupNumber = ((array.Length > 4) ? Convert.ToInt32(array[4]) : 0);
			commandGroup = commandGroup.AddTrigger(triggerName, startTime3, endTime2, groupNumber).Commands;
		}
	}
}
