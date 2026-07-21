using System.Collections.Generic;
using OsuParsers.Database.Objects;
using OsuParsers.Encoders;

namespace OsuParsers.Database;

public class CollectionDatabase
{
	public int OsuVersion { get; set; }

	public int CollectionCount { get; set; }

	public List<Collection> Collections { get; set; } = new List<Collection>();

	public void Save(string path)
	{
		DatabaseEncoder.EncodeCollectionDatabase(path, this);
	}
}
