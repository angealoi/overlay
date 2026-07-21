using System;
using System.IO;
using System.Linq;
using System.Text;
using OsuParsers.Enums;
using OsuParsers.Enums.Replays;
using OsuParsers.Helpers;
using OsuParsers.Replays;
using OsuParsers.Replays.Objects;
using OsuParsers.Replays.SevenZip;
using OsuParsers.Serialization;

namespace OsuParsers.Decoders;

public static class ReplayDecoder
{
	public static Replay Decode(string path)
	{
		if (File.Exists(path))
		{
			return Decode(new FileStream(path, FileMode.Open));
		}
		throw new FileNotFoundException();
	}

	public static Replay Decode(Stream stream)
	{
		Replay replay = new Replay();
		using (SerializationReader serializationReader = new SerializationReader(stream))
		{
			replay.Ruleset = (Ruleset)serializationReader.ReadByte();
			replay.OsuVersion = serializationReader.ReadInt32();
			replay.BeatmapMD5Hash = serializationReader.ReadString();
			replay.PlayerName = serializationReader.ReadString();
			replay.ReplayMD5Hash = serializationReader.ReadString();
			replay.Count300 = serializationReader.ReadUInt16();
			replay.Count100 = serializationReader.ReadUInt16();
			replay.Count50 = serializationReader.ReadUInt16();
			replay.CountGeki = serializationReader.ReadUInt16();
			replay.CountKatu = serializationReader.ReadUInt16();
			replay.CountMiss = serializationReader.ReadUInt16();
			replay.ReplayScore = serializationReader.ReadInt32();
			replay.Combo = serializationReader.ReadUInt16();
			replay.PerfectCombo = serializationReader.ReadBoolean();
			replay.Mods = (Mods)serializationReader.ReadInt32();
			string text = serializationReader.ReadString();
			if (!string.IsNullOrEmpty(text))
			{
				string[] array = text.Split(',');
				for (int i = 0; i < array.Length; i++)
				{
					string[] array2 = array[i].Split('|');
					if (array2.Length >= 2)
					{
						replay.LifeFrames.Add(new LifeFrame
						{
							Time = Convert.ToInt32(array2[0]),
							Percentage = array2[1].ToFloat()
						});
					}
				}
			}
			replay.ReplayTimestamp = serializationReader.ReadDateTime();
			replay.ReplayLength = serializationReader.ReadInt32();
			if (replay.ReplayLength > 0)
			{
				byte[] bytes = LZMAHelper.Decompress(serializationReader.ReadBytes(replay.ReplayLength));
				string text2 = Encoding.ASCII.GetString(bytes);
				int num = 0;
				string[] array = text2.Split(',');
				foreach (string text3 in array)
				{
					if (string.IsNullOrEmpty(text3))
					{
						continue;
					}
					string[] array3 = text3.Split('|');
					if (array3.Length < 4)
					{
						continue;
					}
					if (array3[0] == "-12345")
					{
						replay.Seed = Convert.ToInt32(array3[3]);
						continue;
					}
					ReplayFrame replayFrame = new ReplayFrame();
					replayFrame.TimeDiff = Convert.ToInt32(array3[0]);
					replayFrame.Time = Convert.ToInt32(array3[0]) + num;
					replayFrame.X = array3[1].ToFloat();
					replayFrame.Y = array3[2].ToFloat();
					switch (replay.Ruleset)
					{
					case Ruleset.Standard:
						replayFrame.StandardKeys = (StandardKeys)Convert.ToInt32(array3[3]);
						break;
					case Ruleset.Taiko:
						replayFrame.TaikoKeys = (TaikoKeys)Convert.ToInt32(array3[3]);
						break;
					case Ruleset.Fruits:
						replayFrame.CatchKeys = (CatchKeys)Convert.ToInt32(array3[3]);
						break;
					case Ruleset.Mania:
						replayFrame.ManiaKeys = (ManiaKeys)replayFrame.X;
						break;
					}
					replay.ReplayFrames.Add(replayFrame);
					num = replay.ReplayFrames.Last().Time;
				}
			}
			if (serializationReader.BaseStream.Length - serializationReader.BaseStream.Position > 0)
			{
				if (serializationReader.BaseStream.Length - serializationReader.BaseStream.Position == 4)
				{
					replay.OnlineId = serializationReader.ReadInt32();
				}
				else
				{
					replay.OnlineId = serializationReader.ReadInt64();
				}
			}
		}
		return replay;
	}
}
