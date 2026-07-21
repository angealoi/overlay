using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OsuParsers.Database;
using OsuParsers.Database.Objects;
using OsuParsers.Enums;
using OsuParsers.Serialization;

namespace OsuParsers.Encoders;

internal class DatabaseEncoder
{
	public static void EncodeOsuDatabase(string path, OsuDatabase db)
	{
		using SerializationWriter serializationWriter = new SerializationWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read));
		serializationWriter.Write(db.OsuVersion);
		serializationWriter.Write(db.FolderCount);
		serializationWriter.Write(db.AccountUnlocked);
		serializationWriter.Write(db.UnlockDate);
		serializationWriter.Write(db.PlayerName);
		serializationWriter.Write(db.BeatmapCount);
		foreach (DbBeatmap beatmap in db.Beatmaps)
		{
			if (db.OsuVersion < 20191106)
			{
				serializationWriter.Write(beatmap.BytesOfBeatmapEntry);
			}
			serializationWriter.Write(beatmap.Artist);
			serializationWriter.Write(beatmap.ArtistUnicode);
			serializationWriter.Write(beatmap.Title);
			serializationWriter.Write(beatmap.TitleUnicode);
			serializationWriter.Write(beatmap.Creator);
			serializationWriter.Write(beatmap.Difficulty);
			serializationWriter.Write(beatmap.AudioFileName);
			serializationWriter.Write(beatmap.MD5Hash);
			serializationWriter.Write(beatmap.FileName);
			serializationWriter.Write((byte)beatmap.RankedStatus);
			serializationWriter.Write(beatmap.CirclesCount);
			serializationWriter.Write(beatmap.SlidersCount);
			serializationWriter.Write(beatmap.SpinnersCount);
			serializationWriter.Write(beatmap.LastModifiedTime);
			serializationWriter.Write(beatmap.ApproachRate);
			serializationWriter.Write(beatmap.CircleSize);
			serializationWriter.Write(beatmap.HPDrain);
			serializationWriter.Write(beatmap.OverallDifficulty);
			serializationWriter.Write(beatmap.SliderVelocity);
			if (db.OsuVersion >= 20250107)
			{
				serializationWriter.Write(beatmap.StandardStarRating.ToDictionary((KeyValuePair<Mods, double> d) => (int)d.Key, (KeyValuePair<Mods, double> d) => (float)d.Value));
				serializationWriter.Write(beatmap.TaikoStarRating.ToDictionary((KeyValuePair<Mods, double> d) => (int)d.Key, (KeyValuePair<Mods, double> d) => (float)d.Value));
				serializationWriter.Write(beatmap.CatchStarRating.ToDictionary((KeyValuePair<Mods, double> d) => (int)d.Key, (KeyValuePair<Mods, double> d) => (float)d.Value));
				serializationWriter.Write(beatmap.ManiaStarRating.ToDictionary((KeyValuePair<Mods, double> d) => (int)d.Key, (KeyValuePair<Mods, double> d) => (float)d.Value));
			}
			else if (db.OsuVersion >= 20140609)
			{
				serializationWriter.Write(beatmap.StandardStarRating.ToDictionary((KeyValuePair<Mods, double> d) => (int)d.Key, (KeyValuePair<Mods, double> d) => d.Value));
				serializationWriter.Write(beatmap.TaikoStarRating.ToDictionary((KeyValuePair<Mods, double> d) => (int)d.Key, (KeyValuePair<Mods, double> d) => d.Value));
				serializationWriter.Write(beatmap.CatchStarRating.ToDictionary((KeyValuePair<Mods, double> d) => (int)d.Key, (KeyValuePair<Mods, double> d) => d.Value));
				serializationWriter.Write(beatmap.ManiaStarRating.ToDictionary((KeyValuePair<Mods, double> d) => (int)d.Key, (KeyValuePair<Mods, double> d) => d.Value));
			}
			serializationWriter.Write(beatmap.DrainTime);
			serializationWriter.Write(beatmap.TotalTime);
			serializationWriter.Write(beatmap.AudioPreviewTime);
			serializationWriter.Write(beatmap.TimingPoints.Count);
			for (int num = 0; num < beatmap.TimingPoints.Count; num++)
			{
				serializationWriter.Write(beatmap.TimingPoints[num].BPM);
				serializationWriter.Write(beatmap.TimingPoints[num].Offset);
				serializationWriter.Write(!beatmap.TimingPoints[num].Inherited);
			}
			serializationWriter.Write(beatmap.BeatmapId);
			serializationWriter.Write(beatmap.BeatmapSetId);
			serializationWriter.Write(beatmap.ThreadId);
			serializationWriter.Write((byte)beatmap.StandardGrade);
			serializationWriter.Write((byte)beatmap.TaikoGrade);
			serializationWriter.Write((byte)beatmap.CatchGrade);
			serializationWriter.Write((byte)beatmap.ManiaGrade);
			serializationWriter.Write(beatmap.LocalOffset);
			serializationWriter.Write(beatmap.StackLeniency);
			serializationWriter.Write((byte)beatmap.Ruleset);
			serializationWriter.Write(beatmap.Source);
			serializationWriter.Write(beatmap.Tags);
			serializationWriter.Write(beatmap.OnlineOffset);
			serializationWriter.Write(beatmap.TitleFont);
			serializationWriter.Write(beatmap.IsUnplayed);
			serializationWriter.Write(beatmap.LastPlayed);
			serializationWriter.Write(beatmap.IsOsz2);
			serializationWriter.Write(beatmap.FolderName);
			serializationWriter.Write(beatmap.LastCheckedAgainstOsuRepo);
			serializationWriter.Write(beatmap.IgnoreBeatmapSound);
			serializationWriter.Write(beatmap.IgnoreBeatmapSkin);
			serializationWriter.Write(beatmap.DisableStoryboard);
			serializationWriter.Write(beatmap.DisableVideo);
			serializationWriter.Write(beatmap.VisualOverride);
			if (db.OsuVersion < 20140609)
			{
				serializationWriter.Write((short)0);
			}
			serializationWriter.Write(0);
			serializationWriter.Write(beatmap.ManiaScrollSpeed);
		}
		serializationWriter.Write((int)db.Permissions);
	}

	public static void EncodeCollectionDatabase(string path, CollectionDatabase db)
	{
		using SerializationWriter serializationWriter = new SerializationWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read));
		serializationWriter.Write(db.OsuVersion);
		serializationWriter.Write(db.CollectionCount);
		foreach (Collection collection in db.Collections)
		{
			serializationWriter.Write(collection.Name);
			serializationWriter.Write(collection.Count);
			foreach (string mD5Hash in collection.MD5Hashes)
			{
				serializationWriter.Write(mD5Hash);
			}
		}
	}

	public static void EncodeScoresDatabase(string path, ScoresDatabase db)
	{
		using SerializationWriter serializationWriter = new SerializationWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read));
		serializationWriter.Write(db.OsuVersion);
		serializationWriter.Write(db.Scores.Count);
		foreach (Tuple<string, List<Score>> score in db.Scores)
		{
			serializationWriter.Write(score.Item1);
			serializationWriter.Write(score.Item2.Count);
			foreach (Score item in score.Item2)
			{
				serializationWriter.Write((byte)item.Ruleset);
				serializationWriter.Write(item.OsuVersion);
				serializationWriter.Write(item.BeatmapMD5Hash);
				serializationWriter.Write(item.PlayerName);
				serializationWriter.Write(item.ReplayMD5Hash);
				serializationWriter.Write(item.Count300);
				serializationWriter.Write(item.Count100);
				serializationWriter.Write(item.Count50);
				serializationWriter.Write(item.CountGeki);
				serializationWriter.Write(item.CountKatu);
				serializationWriter.Write(item.CountMiss);
				serializationWriter.Write(item.ReplayScore);
				serializationWriter.Write(item.Combo);
				serializationWriter.Write(item.PerfectCombo);
				serializationWriter.Write((int)item.Mods);
				serializationWriter.Write(string.Empty);
				serializationWriter.Write(item.ScoreTimestamp);
				serializationWriter.Write(-1);
				serializationWriter.Write(item.ScoreId);
			}
		}
	}

	public static void EncodePresenceDatabase(string path, PresenceDatabase db)
	{
		using SerializationWriter serializationWriter = new SerializationWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read));
		serializationWriter.Write(db.OsuVersion);
		serializationWriter.Write(db.Players.Count);
		foreach (Player player in db.Players)
		{
			serializationWriter.Write(player.UserId);
			serializationWriter.Write(player.Username);
			serializationWriter.Write((byte)(player.Timezone + 24));
			serializationWriter.Write(player.CountryCode);
			serializationWriter.Write((byte)(((byte)player.Permissions & 0x1F) | (((byte)player.Ruleset & 7) << 5)));
			serializationWriter.Write(player.Longitude);
			serializationWriter.Write(player.Latitude);
			serializationWriter.Write(player.Rank);
			serializationWriter.Write(player.LastUpdateTime);
		}
	}
}
