using System.Collections.Generic;
using System.Linq;
using OsuParsers.Enums.Storyboards;
using OsuParsers.Helpers;
using OsuParsers.Storyboards;
using OsuParsers.Storyboards.Interfaces;
using OsuParsers.Storyboards.Objects;

namespace OsuParsers.Writers;

internal class StoryboardEncoder
{
	public static List<string> Encode(Storyboard storyboard)
	{
		List<string> list = new List<string>();
		if (storyboard.Variables != null && storyboard.Variables.Any())
		{
			list.Add("[Variables]");
			foreach (KeyValuePair<string, string> variable in storyboard.Variables)
			{
				list.Add(variable.Key + "=" + variable.Value);
			}
			list.Add(string.Empty);
		}
		list.AddRange(new List<string> { "[Events]", "//Background and Video events" });
		list.Add("//Storyboard Layer 0 (Background)");
		storyboard.BackgroundLayer.ForEach(delegate(IStoryboardObject sbObject)
		{
			list.AddRange(WriteHelper.StoryboardObject(sbObject, StoryboardLayer.Background));
		});
		list.Add("//Storyboard Layer 1 (Fail)");
		storyboard.FailLayer.ForEach(delegate(IStoryboardObject sbObject)
		{
			list.AddRange(WriteHelper.StoryboardObject(sbObject, StoryboardLayer.Fail));
		});
		list.Add("//Storyboard Layer 2 (Pass)");
		storyboard.PassLayer.ForEach(delegate(IStoryboardObject sbObject)
		{
			list.AddRange(WriteHelper.StoryboardObject(sbObject, StoryboardLayer.Pass));
		});
		list.Add("//Storyboard Layer 3 (Foreground)");
		storyboard.ForegroundLayer.ForEach(delegate(IStoryboardObject sbObject)
		{
			list.AddRange(WriteHelper.StoryboardObject(sbObject, StoryboardLayer.Foreground));
		});
		list.Add("//Storyboard Layer 4 (Overlay)");
		storyboard.OverlayLayer.ForEach(delegate(IStoryboardObject sbObject)
		{
			list.AddRange(WriteHelper.StoryboardObject(sbObject, StoryboardLayer.Overlay));
		});
		list.Add("//Storyboard Sound Samples");
		storyboard.SamplesLayer.ForEach(delegate(IStoryboardObject sbObject)
		{
			list.AddRange(WriteHelper.StoryboardObject(sbObject, (sbObject as StoryboardSample).Layer));
		});
		for (int num = 0; num < list.Count; num++)
		{
			foreach (KeyValuePair<string, string> variable2 in storyboard.Variables)
			{
				list[num] = list[num].Replace("," + variable2.Value, "," + variable2.Key);
			}
		}
		return list;
	}
}
