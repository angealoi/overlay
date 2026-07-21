using System;
using System.Collections.Generic;
using System.IO;
using OsuParsers.Database;
using OsuParsers.Database.Objects;
using OsuParsers.Enums;
using OsuParsers.Enums.Database;
using OsuParsers.Serialization;

namespace OsuParsers.Decoders;

public static class DatabaseDecoder
{
	public static OsuDatabase DecodeOsu(string path)
	{
		if (TryOpenReadFile(path, out Stream stream))
		{
			return DecodeOsu(stream);
		}
		throw new FileNotFoundException();
	}

	public static OsuDatabase DecodeOsu(Stream stream)
	{
		OsuDatabase osuDatabase = new OsuDatabase();
		using SerializationReader serializationReader = new SerializationReader(stream);
		osuDatabase.OsuVersion = serializationReader.ReadInt32();
		osuDatabase.FolderCount = serializationReader.ReadInt32();
		osuDatabase.AccountUnlocked = serializationReader.ReadBoolean();
		osuDatabase.UnlockDate = serializationReader.ReadDateTime();
		osuDatabase.PlayerName = serializationReader.ReadString();
		int num = (osuDatabase.BeatmapCount = serializationReader.ReadInt32());
		for (int i = 0; i < num; i++)
		{
			DbBeatmap dbBeatmap = new DbBeatmap();
			if (osuDatabase.OsuVersion < 20191106)
			{
				dbBeatmap.BytesOfBeatmapEntry = serializationReader.ReadInt32();
			}
			dbBeatmap.Artist = serializationReader.ReadString();
			dbBeatmap.ArtistUnicode = serializationReader.ReadString();
			dbBeatmap.Title = serializationReader.ReadString();
			dbBeatmap.TitleUnicode = serializationReader.ReadString();
			dbBeatmap.Creator = serializationReader.ReadString();
			dbBeatmap.Difficulty = serializationReader.ReadString();
			dbBeatmap.AudioFileName = serializationReader.ReadString();
			dbBeatmap.MD5Hash = serializationReader.ReadString();
			dbBeatmap.FileName = serializationReader.ReadString();
			dbBeatmap.RankedStatus = (RankedStatus)serializationReader.ReadByte();
			dbBeatmap.CirclesCount = serializationReader.ReadUInt16();
			dbBeatmap.SlidersCount = serializationReader.ReadUInt16();
			dbBeatmap.SpinnersCount = serializationReader.ReadUInt16();
			dbBeatmap.LastModifiedTime = serializationReader.ReadDateTime();
			dbBeatmap.ApproachRate = ((osuDatabase.OsuVersion >= 20140609) ? serializationReader.ReadSingle() : ((float)(int)serializationReader.ReadByte()));
			dbBeatmap.CircleSize = ((osuDatabase.OsuVersion >= 20140609) ? serializationReader.ReadSingle() : ((float)(int)serializationReader.ReadByte()));
			dbBeatmap.HPDrain = ((osuDatabase.OsuVersion >= 20140609) ? serializationReader.ReadSingle() : ((float)(int)serializationReader.ReadByte()));
			dbBeatmap.OverallDifficulty = ((osuDatabase.OsuVersion >= 20140609) ? serializationReader.ReadSingle() : ((float)(int)serializationReader.ReadByte()));
			dbBeatmap.SliderVelocity = serializationReader.ReadDouble();
			if (osuDatabase.OsuVersion >= 20140609)
			{
				dbBeatmap.StandardStarRating = serializationReader.ReadDictionary<Mods, double>();
				dbBeatmap.TaikoStarRating = serializationReader.ReadDictionary<Mods, double>();
				dbBeatmap.CatchStarRating = serializationReader.ReadDictionary<Mods, double>();
				dbBeatmap.ManiaStarRating = serializationReader.ReadDictionary<Mods, double>();
			}
			dbBeatmap.DrainTime = serializationReader.ReadInt32();
			dbBeatmap.TotalTime = serializationReader.ReadInt32();
			dbBeatmap.AudioPreviewTime = serializationReader.ReadInt32();
			int num3 = serializationReader.ReadInt32();
			for (int j = 0; j < num3; j++)
			{
				DbTimingPoint dbTimingPoint = new DbTimingPoint();
				dbTimingPoint.BPM = serializationReader.ReadDouble();
				dbTimingPoint.Offset = serializationReader.ReadDouble();
				dbTimingPoint.Inherited = !serializationReader.ReadBoolean();
				dbBeatmap.TimingPoints.Add(dbTimingPoint);
			}
			dbBeatmap.BeatmapId = serializationReader.ReadInt32();
			dbBeatmap.BeatmapSetId = serializationReader.ReadInt32();
			dbBeatmap.ThreadId = serializationReader.ReadInt32();
			dbBeatmap.StandardGrade = (Grade)serializationReader.ReadByte();
			dbBeatmap.TaikoGrade = (Grade)serializationReader.ReadByte();
			dbBeatmap.CatchGrade = (Grade)serializationReader.ReadByte();
			dbBeatmap.ManiaGrade = (Grade)serializationReader.ReadByte();
			dbBeatmap.LocalOffset = serializationReader.ReadInt16();
			dbBeatmap.StackLeniency = serializationReader.ReadSingle();
			dbBeatmap.Ruleset = (Ruleset)serializationReader.ReadByte();
			dbBeatmap.Source = serializationReader.ReadString();
			dbBeatmap.Tags = serializationReader.ReadString();
			dbBeatmap.OnlineOffset = serializationReader.ReadInt16();
			dbBeatmap.TitleFont = serializationReader.ReadString();
			dbBeatmap.IsUnplayed = serializationReader.ReadBoolean();
			dbBeatmap.LastPlayed = serializationReader.ReadDateTime();
			dbBeatmap.IsOsz2 = serializationReader.ReadBoolean();
			dbBeatmap.FolderName = serializationReader.ReadString();
			dbBeatmap.LastCheckedAgainstOsuRepo = serializationReader.ReadDateTime();
			dbBeatmap.IgnoreBeatmapSound = serializationReader.ReadBoolean();
			dbBeatmap.IgnoreBeatmapSkin = serializationReader.ReadBoolean();
			dbBeatmap.DisableStoryboard = serializationReader.ReadBoolean();
			dbBeatmap.DisableVideo = serializationReader.ReadBoolean();
			dbBeatmap.VisualOverride = serializationReader.ReadBoolean();
			if (osuDatabase.OsuVersion < 20140609)
			{
				serializationReader.BaseStream.Seek(2L, SeekOrigin.Current);
			}
			serializationReader.BaseStream.Seek(4L, SeekOrigin.Current);
			dbBeatmap.ManiaScrollSpeed = serializationReader.ReadByte();
			osuDatabase.Beatmaps.Add(dbBeatmap);
		}
		osuDatabase.Permissions = (Permissions)serializationReader.ReadInt32();
		return osuDatabase;
	}

	public static CollectionDatabase DecodeCollection(string path)
	{
		if (TryOpenReadFile(path, out Stream stream))
		{
			return DecodeCollection(stream);
		}
		throw new FileNotFoundException();
	}

	public static CollectionDatabase DecodeCollection(Stream stream)
	{
		CollectionDatabase collectionDatabase = new CollectionDatabase();
		using SerializationReader serializationReader = new SerializationReader(stream);
		collectionDatabase.OsuVersion = serializationReader.ReadInt32();
		int num = (collectionDatabase.CollectionCount = serializationReader.ReadInt32());
		for (int i = 0; i < num; i++)
		{
			Collection collection = new Collection();
			collection.Name = serializationReader.ReadString();
			int num3 = (collection.Count = serializationReader.ReadInt32());
			for (int j = 0; j < num3; j++)
			{
				collection.MD5Hashes.Add(serializationReader.ReadString());
			}
			collectionDatabase.Collections.Add(collection);
		}
		return collectionDatabase;
	}

	public static ScoresDatabase DecodeScores(string path)
	{
		if (TryOpenReadFile(path, out Stream stream))
		{
			return DecodeScores(stream);
		}
		throw new FileNotFoundException();
	}

	public static ScoresDatabase DecodeScores(Stream stream)
	{
		ScoresDatabase scoresDatabase = new ScoresDatabase();
		using SerializationReader serializationReader = new SerializationReader(stream);
		scoresDatabase.OsuVersion = serializationReader.ReadInt32();
		int num = serializationReader.ReadInt32();
		for (int i = 0; i < num; i++)
		{
			string item = serializationReader.ReadString();
			int num2 = serializationReader.ReadInt32();
			List<Score> list = new List<Score>();
			for (int j = 0; j < num2; j++)
			{
				Score score = new Score();
				score.Ruleset = (Ruleset)serializationReader.ReadByte();
				score.OsuVersion = serializationReader.ReadInt32();
				score.BeatmapMD5Hash = serializationReader.ReadString();
				score.PlayerName = serializationReader.ReadString();
				score.ReplayMD5Hash = serializationReader.ReadString();
				score.Count300 = serializationReader.ReadUInt16();
				score.Count100 = serializationReader.ReadUInt16();
				score.Count50 = serializationReader.ReadUInt16();
				score.CountGeki = serializationReader.ReadUInt16();
				score.CountKatu = serializationReader.ReadUInt16();
				score.CountMiss = serializationReader.ReadUInt16();
				score.ReplayScore = serializationReader.ReadInt32();
				score.Combo = serializationReader.ReadUInt16();
				score.PerfectCombo = serializationReader.ReadBoolean();
				score.Mods = (Mods)serializationReader.ReadInt32();
				serializationReader.ReadString();
				score.ScoreTimestamp = serializationReader.ReadDateTime();
				serializationReader.BaseStream.Seek(4L, SeekOrigin.Current);
				score.ScoreId = serializationReader.ReadInt64();
				list.Add(score);
			}
			scoresDatabase.Scores.Add(new Tuple<string, List<Score>>(item, list));
		}
		return scoresDatabase;
	}

	public static PresenceDatabase DecodePresence(string path)
	{
		if (TryOpenReadFile(path, out Stream stream))
		{
			return DecodePresence(stream);
		}
		throw new FileNotFoundException();
	}

	public static PresenceDatabase DecodePresence(Stream stream)
	{
		PresenceDatabase presenceDatabase = new PresenceDatabase();
		using SerializationReader serializationReader = new SerializationReader(stream);
		presenceDatabase.OsuVersion = serializationReader.ReadInt32();
		int num = serializationReader.ReadInt32();
		for (int i = 0; i < num; i++)
		{
			Player player = new Player();
			player.UserId = serializationReader.ReadInt32();
			player.Username = serializationReader.ReadString();
			player.Timezone = serializationReader.ReadByte() - 24;
			player.CountryCode = serializationReader.ReadByte();
			byte b = serializationReader.ReadByte();
			player.Permissions = (Permissions)(b & -225);
			player.Ruleset = (Ruleset)Math.Max(0, Math.Min(3, (b & 0xE0) >> 5));
			player.Longitude = serializationReader.ReadSingle();
			player.Latitude = serializationReader.ReadSingle();
			player.Rank = serializationReader.ReadInt32();
			player.LastUpdateTime = serializationReader.ReadDateTime();
			presenceDatabase.Players.Add(player);
		}
		return presenceDatabase;
	}

	private static bool TryOpenReadFile(string path, out Stream stream)
	{
		if (File.Exists(path))
		{
			stream = new FileStream(path, FileMode.Open);
			return true;
		}
		stream = null;
		return false;
	}
}
