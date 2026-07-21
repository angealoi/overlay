using OsuParsers.Enums.Storyboards;
using OsuParsers.Storyboards.Interfaces;

namespace OsuParsers.Storyboards.Objects;

public class StoryboardSample : IStoryboardObject
{
	public StoryboardLayer Layer;

	public int Time;

	public int Volume;

	public string FilePath { get; set; }

	public StoryboardSample(StoryboardLayer layer, int time, string filePath, int volume)
	{
		Layer = layer;
		Time = time;
		FilePath = filePath;
		Volume = volume;
	}
}
