using System.Collections.Generic;
using System.IO;
using OsuParsers.Enums.Storyboards;
using OsuParsers.Storyboards.Interfaces;
using OsuParsers.Writers;

namespace OsuParsers.Storyboards;

public class Storyboard
{
	public List<IStoryboardObject> BackgroundLayer = new List<IStoryboardObject>();

	public List<IStoryboardObject> FailLayer = new List<IStoryboardObject>();

	public List<IStoryboardObject> PassLayer = new List<IStoryboardObject>();

	public List<IStoryboardObject> ForegroundLayer = new List<IStoryboardObject>();

	public List<IStoryboardObject> OverlayLayer = new List<IStoryboardObject>();

	public List<IStoryboardObject> SamplesLayer = new List<IStoryboardObject>();

	public Dictionary<string, string> Variables = new Dictionary<string, string>();

	public List<IStoryboardObject> GetLayer(StoryboardLayer layer)
	{
		return layer switch
		{
			StoryboardLayer.Background => BackgroundLayer, 
			StoryboardLayer.Fail => FailLayer, 
			StoryboardLayer.Pass => PassLayer, 
			StoryboardLayer.Foreground => ForegroundLayer, 
			StoryboardLayer.Overlay => OverlayLayer, 
			StoryboardLayer.Samples => SamplesLayer, 
			_ => BackgroundLayer, 
		};
	}

	public void Save(string path)
	{
		File.WriteAllLines(path, StoryboardEncoder.Encode(this));
	}
}
