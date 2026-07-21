using System;
using System.Collections.Generic;
using System.IO;

namespace OsuParsers.Serialization;

internal class SerializationWriter : BinaryWriter
{
	public SerializationWriter(Stream s)
		: base(s)
	{
	}

	public override void Write(string str)
	{
		if (str == null)
		{
			Write((byte)0);
			return;
		}
		Write((byte)11);
		base.Write(str);
	}

	public void Write(DateTime dateTime)
	{
		Write(dateTime.ToUniversalTime().Ticks);
	}

	public void Write<T, U>(IDictionary<T, U> d)
	{
		if (d == null)
		{
			Write(-1);
			return;
		}
		Write(d.Count);
		foreach (KeyValuePair<T, U> item in d)
		{
			WriteObject(item.Key);
			WriteObject(item.Value);
		}
	}

	public void WriteObject(object obj)
	{
		if (obj == null)
		{
			Write((byte)0);
			return;
		}
		switch (obj.GetType().Name)
		{
		case "Boolean":
			Write((byte)1);
			Write((bool)obj);
			break;
		case "Byte":
			Write((byte)2);
			Write((byte)obj);
			break;
		case "UInt16":
			Write((byte)3);
			Write((ushort)obj);
			break;
		case "UInt32":
			Write((byte)4);
			Write((uint)obj);
			break;
		case "UInt64":
			Write((byte)5);
			Write((ulong)obj);
			break;
		case "SByte":
			Write((byte)6);
			Write((sbyte)obj);
			break;
		case "Int16":
			Write((byte)7);
			Write((short)obj);
			break;
		case "Int32":
			Write((byte)8);
			Write((int)obj);
			break;
		case "Int64":
			Write((byte)9);
			Write((long)obj);
			break;
		case "Char":
			Write((byte)10);
			base.Write((char)obj);
			break;
		case "String":
			Write((byte)11);
			base.Write((string)obj);
			break;
		case "Single":
			Write((byte)12);
			Write((float)obj);
			break;
		case "Double":
			Write((byte)13);
			Write((double)obj);
			break;
		case "Decimal":
			Write((byte)14);
			Write((decimal)obj);
			break;
		case "DateTime":
			Write((byte)15);
			Write((DateTime)obj);
			break;
		case "Byte[]":
			Write((byte)16);
			base.Write((byte[])obj);
			break;
		case "Char[]":
			Write((byte)17);
			base.Write((char[])obj);
			break;
		default:
			throw new NotImplementedException();
		}
	}
}
