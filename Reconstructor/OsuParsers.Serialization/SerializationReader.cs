using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OsuParsers.Serialization;

internal class SerializationReader : BinaryReader
{
	public SerializationReader(Stream s)
		: base(s, Encoding.UTF8)
	{
	}

	public override string ReadString()
	{
		if (ReadByte() == 0)
		{
			return null;
		}
		return base.ReadString();
	}

	public byte[] ReadByteArray()
	{
		int num = ReadInt32();
		if (num > 0)
		{
			return ReadBytes(num);
		}
		if (num < 0)
		{
			return null;
		}
		return new byte[0];
	}

	public char[] ReadCharArray()
	{
		int num = ReadInt32();
		if (num > 0)
		{
			return ReadChars(num);
		}
		if (num < 0)
		{
			return null;
		}
		return new char[0];
	}

	public DateTime ReadDateTime()
	{
		long num = ReadInt64();
		if (num < 0 || num > 3155378975999999999L)
		{
			num = 0L;
		}
		return new DateTime(num, DateTimeKind.Utc);
	}

	public List<T> ReadList<T>()
	{
		int num = ReadInt32();
		if (num < 0)
		{
			return null;
		}
		List<T> list = new List<T>(num);
		for (int i = 0; i < num; i++)
		{
			list.Add((T)ReadObject());
		}
		return list;
	}

	public Dictionary<T, U> ReadDictionary<T, U>()
	{
		int num = ReadInt32();
		if (num < 0)
		{
			return null;
		}
		Dictionary<T, U> dictionary = new Dictionary<T, U>();
		for (int i = 0; i < num; i++)
		{
			dictionary[(T)ReadObject()] = (U)Convert.ChangeType(ReadObject(), typeof(U));
		}
		return dictionary;
	}

	public object ReadObject()
	{
		return (ObjType)ReadByte() switch
		{
			ObjType.Bool => ReadBoolean(), 
			ObjType.Byte => ReadByte(), 
			ObjType.UShort => ReadUInt16(), 
			ObjType.UInt => ReadUInt32(), 
			ObjType.ULong => ReadUInt64(), 
			ObjType.SByte => ReadSByte(), 
			ObjType.Short => ReadInt16(), 
			ObjType.Int => ReadInt32(), 
			ObjType.Long => ReadInt64(), 
			ObjType.Char => ReadChar(), 
			ObjType.String => base.ReadString(), 
			ObjType.Float => ReadSingle(), 
			ObjType.Double => ReadDouble(), 
			ObjType.Decimal => ReadDecimal(), 
			ObjType.DateTime => ReadDateTime(), 
			ObjType.ByteArray => ReadByteArray(), 
			ObjType.CharArray => ReadCharArray(), 
			_ => null, 
		};
	}
}
