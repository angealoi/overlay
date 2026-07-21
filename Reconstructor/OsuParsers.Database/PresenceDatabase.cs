using System.Collections.Generic;
using OsuParsers.Database.Objects;
using OsuParsers.Encoders;

namespace OsuParsers.Database;

public class PresenceDatabase
{
	public int OsuVersion { get; set; }

	public List<Player> Players { get; set; } = new List<Player>();

	public void Save(string path)
	{
		DatabaseEncoder.EncodePresenceDatabase(path, this);
	}
}
