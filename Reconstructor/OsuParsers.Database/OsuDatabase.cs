using System;
using System.Collections.Generic;
using OsuParsers.Database.Objects;
using OsuParsers.Encoders;
using OsuParsers.Enums.Database;

namespace OsuParsers.Database;

public class OsuDatabase
{
	public int OsuVersion { get; set; }

	public int FolderCount { get; set; }

	public bool AccountUnlocked { get; set; }

	public DateTime UnlockDate { get; set; }

	public string PlayerName { get; set; }

	public int BeatmapCount { get; set; }

	public List<DbBeatmap> Beatmaps { get; set; } = new List<DbBeatmap>();

	public Permissions Permissions { get; set; }

	public void Save(string path)
	{
		DatabaseEncoder.EncodeOsuDatabase(path, this);
	}
}
