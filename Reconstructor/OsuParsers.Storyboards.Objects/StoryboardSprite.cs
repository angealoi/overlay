using OsuParsers.Enums.Storyboards;
using OsuParsers.Storyboards.Commands;
using OsuParsers.Storyboards.Interfaces;

namespace OsuParsers.Storyboards.Objects;

public class StoryboardSprite : IStoryboardObject, IHasCommands
{
	public Origins Origin;

	public float X;

	public float Y;

	public CommandGroup Commands { get; } = new CommandGroup();

	public string FilePath { get; set; }

	public StoryboardSprite(Origins origin, string filePath, float x, float y)
	{
		Origin = origin;
		FilePath = filePath;
		X = x;
		Y = y;
	}
}
