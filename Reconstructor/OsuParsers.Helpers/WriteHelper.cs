using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using OsuParsers.Beatmaps.Objects;
using OsuParsers.Beatmaps.Objects.Mania;
using OsuParsers.Enums.Beatmaps;
using OsuParsers.Enums.Storyboards;
using OsuParsers.Storyboards.Commands;
using OsuParsers.Storyboards.Interfaces;
using OsuParsers.Storyboards.Objects;

namespace OsuParsers.Helpers;

internal class WriteHelper
{
	public static string TimingPoint(TimingPoint timingPoint)
	{
		int offset = timingPoint.Offset;
		string value = timingPoint.BeatLength.Format();
		int timeSignature = (int)timingPoint.TimeSignature;
		int sampleSet = (int)timingPoint.SampleSet;
		int customSampleSet = timingPoint.CustomSampleSet;
		int volume = timingPoint.Volume;
		int value2 = (!timingPoint.Inherited).ToInt32();
		int effects = (int)timingPoint.Effects;
		return $"{offset},{value},{timeSignature},{sampleSet},{customSampleSet},{volume},{value2},{effects}";
	}

	public static string Colour(Color colour)
	{
		byte r = colour.R;
		byte g = colour.G;
		byte b = colour.B;
		return $"{r},{g},{b}";
	}

	public static string HitObject(HitObject hitObject)
	{
		float x = hitObject.Position.X;
		float y = hitObject.Position.Y;
		int startTime = hitObject.StartTime;
		int hitSound = (int)hitObject.HitSound;
		string text = HitObjectExtras(hitObject.Extras);
		int value = TypeByte(hitObject);
		string obj = $"{x},{y},{startTime},{value},{hitSound}";
		string text2 = ",";
		if (hitObject is HitCircle && !(hitObject is ManiaHoldNote))
		{
			text2 += text;
		}
		if (hitObject is Slider slider)
		{
			text2 = text2 + SliderProperties(slider) + ((slider.EdgeHitSounds == null || !slider.EdgeHitSounds.Any()) ? string.Empty : ("," + text));
		}
		if (hitObject is Spinner spinner)
		{
			text2 += $"{spinner.EndTime},{text}";
		}
		if (hitObject is ManiaHoldNote maniaHoldNote)
		{
			text2 += $"{maniaHoldNote.EndTime}:{text}";
		}
		return obj + text2;
	}

	public static string SliderProperties(Slider slider)
	{
		char value = CurveType(slider.CurveType);
		string sliderPoints = string.Empty;
		slider.SliderPoints.ForEach(delegate(Vector2 pt)
		{
			sliderPoints += $"|{pt.X}:{pt.Y}";
		});
		int repeats = slider.Repeats;
		string value2 = slider.PixelLength.Format();
		if (slider.EdgeHitSounds != null && slider.EdgeHitSounds.Any())
		{
			string edgeHitsounds = string.Empty;
			slider.EdgeHitSounds.ForEach(delegate(HitSoundType sound)
			{
				edgeHitsounds += $"{(int)sound}|";
			});
			edgeHitsounds = edgeHitsounds.TrimEnd('|');
			if (slider.EdgeAdditions != null)
			{
				string edgeAdditions = string.Empty;
				slider.EdgeAdditions.ToList().ForEach(delegate(Tuple<SampleSet, SampleSet> e)
				{
					edgeAdditions += $"{(int)e.Item1}:{(int)e.Item2}|";
				});
				edgeAdditions = edgeAdditions.Trim('|');
				return $"{value}{sliderPoints},{repeats},{value2},{edgeHitsounds},{edgeAdditions}";
			}
			return $"{value}{sliderPoints},{repeats},{value2},{edgeHitsounds}";
		}
		return $"{value}{sliderPoints},{repeats},{value2}";
	}

	public static char CurveType(CurveType value)
	{
		return value switch
		{
			OsuParsers.Enums.Beatmaps.CurveType.Bezier => 'B', 
			OsuParsers.Enums.Beatmaps.CurveType.Catmull => 'C', 
			OsuParsers.Enums.Beatmaps.CurveType.Linear => 'L', 
			OsuParsers.Enums.Beatmaps.CurveType.PerfectCurve => 'P', 
			_ => throw new InvalidCastException(), 
		};
	}

	public static int TypeByte(HitObject hitObject)
	{
		int num = 0;
		if (hitObject is HitCircle && !(hitObject is ManiaHoldNote))
		{
			num++;
		}
		if (hitObject is Slider)
		{
			num += 2;
		}
		if (hitObject is Spinner)
		{
			num += 8;
		}
		if (hitObject is ManiaHoldNote)
		{
			num += 128;
		}
		num += (hitObject.IsNewCombo ? 4 : 0);
		return num + (hitObject.ComboOffset << 4);
	}

	public static string HitObjectExtras(Extras extras)
	{
		if (extras == null)
		{
			return "0:0:0:0:";
		}
		int sampleSet = (int)extras.SampleSet;
		int additionSet = (int)extras.AdditionSet;
		int customIndex = extras.CustomIndex;
		int volume = extras.Volume;
		string value = extras.SampleFileName ?? string.Empty;
		return $"{sampleSet}:{additionSet}:{customIndex}:{volume}:{value}";
	}

	public static List<string> StoryboardObject(IStoryboardObject storyboardObject, StoryboardLayer layer)
	{
		List<string> list = new List<string>();
		if (storyboardObject is StoryboardSprite storyboardSprite)
		{
			list.Add($"Sprite,{layer},{storyboardSprite.Origin},\"{storyboardSprite.FilePath}\",{storyboardSprite.X.Format()},{storyboardSprite.Y.Format()}");
		}
		else if (storyboardObject is StoryboardAnimation storyboardAnimation)
		{
			list.Add($"Animation,{layer},{storyboardAnimation.Origin},\"{storyboardAnimation.FilePath}\",{storyboardAnimation.X.Format()},{storyboardAnimation.Y.Format()},{storyboardAnimation.FrameCount},{storyboardAnimation.FrameDelay},{storyboardAnimation.LoopType}");
		}
		else if (storyboardObject is StoryboardSample storyboardSample)
		{
			list.Add($"Sample,{storyboardSample.Time},{layer},\"{storyboardSample.FilePath}\",{storyboardSample.Volume}");
		}
		if (storyboardObject is IHasCommands hasCommands)
		{
			foreach (LoopCommand loop in hasCommands.Commands.Loops)
			{
				list.Add($" L,{loop.LoopStartTime},{loop.LoopCount}");
				foreach (Command command in loop.Commands.Commands)
				{
					if (command.StartTime == command.EndTime)
					{
						list.Add($"  {command.GetAcronym()},{(int)command.Easing},{command.StartTime},,{GetCommandArguments(command)}");
					}
					else
					{
						list.Add($"  {command.GetAcronym()},{(int)command.Easing},{command.StartTime},{command.EndTime},{GetCommandArguments(command)}");
					}
				}
			}
			foreach (Command command2 in hasCommands.Commands.Commands)
			{
				if (command2.StartTime == command2.EndTime)
				{
					list.Add($" {command2.GetAcronym()},{(int)command2.Easing},{command2.StartTime},,{GetCommandArguments(command2)}");
				}
				else
				{
					list.Add($" {command2.GetAcronym()},{(int)command2.Easing},{command2.StartTime},{command2.EndTime},{GetCommandArguments(command2)}");
				}
			}
			foreach (TriggerCommand trigger in hasCommands.Commands.Triggers)
			{
				if (trigger.TriggerEndTime == 0)
				{
					list.Add(" T," + trigger.TriggerName + ((trigger.GroupNumber != 0) ? $",{-trigger.GroupNumber}" : string.Empty));
				}
				else
				{
					list.Add($" T,{trigger.TriggerName},{trigger.TriggerStartTime},{trigger.TriggerEndTime}{((trigger.GroupNumber != 0) ? $",{-trigger.GroupNumber}" : string.Empty)}");
				}
				foreach (Command command3 in trigger.Commands.Commands)
				{
					if (command3.StartTime == command3.EndTime)
					{
						list.Add($"  {command3.GetAcronym()},{(int)command3.Easing},{command3.StartTime},,{GetCommandArguments(command3)}");
					}
					else
					{
						list.Add($"  {command3.GetAcronym()},{(int)command3.Easing},{command3.StartTime},{command3.EndTime},{GetCommandArguments(command3)}");
					}
				}
			}
		}
		return list;
	}

	private static string GetCommandArguments(Command command)
	{
		switch (command.Type)
		{
		case CommandType.Movement:
		case CommandType.VectorScale:
			if (command.StartVector.Equals(command.EndVector))
			{
				return command.StartVector.X.Format() + "," + command.StartVector.Y.Format();
			}
			return $"{command.StartVector.X.Format()},{command.StartVector.Y.Format()},{command.EndVector.X.Format()},{command.EndVector.Y.Format()}";
		case CommandType.MovementX:
		case CommandType.MovementY:
		case CommandType.Fade:
		case CommandType.Scale:
		case CommandType.Rotation:
			if (command.StartFloat == command.EndFloat)
			{
				return command.StartFloat.Format() ?? "";
			}
			return command.StartFloat.Format() + "," + command.EndFloat.Format();
		case CommandType.Colour:
			if (!(command.StartColour == command.EndColour))
			{
				return $"{command.StartColour.R},{command.StartColour.G},{command.StartColour.B},{command.EndColour.R},{command.EndColour.G},{command.EndColour.B}";
			}
			return $"{command.StartColour.R},{command.StartColour.G},{command.StartColour.B}";
		case CommandType.FlipHorizontal:
			return "H";
		case CommandType.FlipVertical:
			return "V";
		case CommandType.BlendingMode:
			return "A";
		default:
			return string.Empty;
		}
	}

	public static List<string> BaseListFormat(string SectionName)
	{
		return new List<string>
		{
			string.Empty,
			"[" + SectionName + "]"
		};
	}
}
