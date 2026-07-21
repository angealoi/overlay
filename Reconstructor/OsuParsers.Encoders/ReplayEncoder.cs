using System.IO;
using System.Text;
using OsuParsers.Enums;
using OsuParsers.Helpers;
using OsuParsers.Replays;
using OsuParsers.Replays.Objects;
using OsuParsers.Replays.SevenZip;
using OsuParsers.Serialization;

namespace OsuParsers.Encoders;

internal class ReplayEncoder
{
	public static void Encode(Replay replay, string path)
	{
		using SerializationWriter serializationWriter = new SerializationWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read));
		serializationWriter.Write((byte)replay.Ruleset);
		serializationWriter.Write(replay.OsuVersion);
		serializationWriter.Write(replay.BeatmapMD5Hash);
		serializationWriter.Write(replay.PlayerName);
		serializationWriter.Write(replay.ReplayMD5Hash);
		serializationWriter.Write(replay.Count300);
		serializationWriter.Write(replay.Count100);
		serializationWriter.Write(replay.Count50);
		serializationWriter.Write(replay.CountGeki);
		serializationWriter.Write(replay.CountKatu);
		serializationWriter.Write(replay.CountMiss);
		serializationWriter.Write(replay.ReplayScore);
		serializationWriter.Write(replay.Combo);
		serializationWriter.Write(replay.PerfectCombo);
		serializationWriter.Write((int)replay.Mods);
		string text = null;
		foreach (LifeFrame lifeFrame in replay.LifeFrames)
		{
			text = text + lifeFrame.Time.Format() + "|" + lifeFrame.Percentage.Format() + ",";
		}
		serializationWriter.Write(text);
		serializationWriter.Write(replay.ReplayTimestamp.ToUniversalTime().Ticks);
		if (replay.ReplayFrames.Count == 0)
		{
			serializationWriter.Write(0);
		}
		else
		{
			string text2 = string.Empty;
			foreach (ReplayFrame replayFrame in replay.ReplayFrames)
			{
				int value = 0;
				switch (replay.Ruleset)
				{
				case Ruleset.Standard:
					value = (int)replayFrame.StandardKeys;
					break;
				case Ruleset.Taiko:
					value = (int)replayFrame.TaikoKeys;
					break;
				case Ruleset.Fruits:
					value = (int)replayFrame.CatchKeys;
					break;
				case Ruleset.Mania:
					value = (int)replayFrame.ManiaKeys;
					break;
				}
				text2 += $"{replayFrame.TimeDiff}|{replayFrame.X.Format()}|{replayFrame.Y.Format()}|{value},";
			}
			byte[] bytes = Encoding.ASCII.GetBytes(text2);
			using MemoryStream memoryStream = new MemoryStream();
			memoryStream.Write(bytes, 0, bytes.Length);
			MemoryStream memoryStream2 = LZMAHelper.Compress(memoryStream);
			byte[] array = new byte[memoryStream2.Length];
			memoryStream2.Read(array, 0, array.Length);
			serializationWriter.Write(array.Length);
			serializationWriter.Write(array);
		}
		serializationWriter.Write(replay.OnlineId);
	}
}
