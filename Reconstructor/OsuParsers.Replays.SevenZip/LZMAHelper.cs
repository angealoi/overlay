using System;
using System.IO;
using SevenZip;
using SevenZip.Compression.LZMA;

namespace OsuParsers.Replays.SevenZip;

internal class LZMAHelper
{
	public static MemoryStream Compress(Stream inStream)
	{
		inStream.Position = 0L;
		CoderPropID[] propIDs = new CoderPropID[5]
		{
			CoderPropID.DictionarySize,
			CoderPropID.PosStateBits,
			CoderPropID.LitContextBits,
			CoderPropID.LitPosBits,
			CoderPropID.Algorithm
		};
		object[] properties = new object[5] { 65536, 2, 3, 0, 2 };
		MemoryStream memoryStream = new MemoryStream();
		Encoder encoder = new Encoder();
		encoder.SetCoderProperties(propIDs, properties);
		encoder.WriteCoderProperties(memoryStream);
		for (int i = 0; i < 8; i++)
		{
			memoryStream.WriteByte((byte)(inStream.Length >> 8 * i));
		}
		encoder.Code(inStream, memoryStream, -1L, -1L, null);
		memoryStream.Flush();
		memoryStream.Position = 0L;
		return memoryStream;
	}

	public static MemoryStream Decompress(Stream inStream)
	{
		Decoder decoder = new Decoder();
		byte[] array = new byte[5];
		if (inStream.Read(array, 0, 5) != 5)
		{
			throw new Exception("input .lzma is too short");
		}
		decoder.SetDecoderProperties(array);
		long num = 0L;
		for (int i = 0; i < 8; i++)
		{
			int num2 = inStream.ReadByte();
			if (num2 < 0)
			{
				break;
			}
			num |= (long)((ulong)(byte)num2 << 8 * i);
		}
		long inSize = inStream.Length - inStream.Position;
		MemoryStream memoryStream = new MemoryStream();
		decoder.Code(inStream, memoryStream, inSize, num, null);
		memoryStream.Flush();
		memoryStream.Position = 0L;
		return memoryStream;
	}

	public static byte[] Compress(byte[] inBytes)
	{
		using MemoryStream inStream = new MemoryStream(inBytes, writable: false);
		return Compress(inStream).ToArray();
	}

	public static byte[] Decompress(byte[] inBytes)
	{
		using MemoryStream inStream = new MemoryStream(inBytes, writable: false);
		return Decompress(inStream).ToArray();
	}
}
