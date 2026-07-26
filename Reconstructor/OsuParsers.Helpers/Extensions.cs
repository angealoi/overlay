using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OsuParsers.Helpers;

internal static class Extensions
{
	public static string Join(this IEnumerable<string> stringGroup, char splitter = ' ')
	{
		if (stringGroup != null)
		{
			string ret = string.Empty;
			stringGroup.ToList().ForEach(delegate(string line)
			{
				ret = ret + line + splitter;
			});
			return ret.TrimEnd(splitter);
		}
		return string.Empty;
	}

	public static string Join(this IEnumerable<int> intGroup, char splitter = ' ')
	{
		if (intGroup != null)
			return intGroup.ToList().ConvertAll((int e) => e.ToString()).Join(splitter);
		return string.Empty;
	}

	public static IEnumerable<string> ReadAllLines(this Stream stream)
	{
		using StreamReader streamReader = new StreamReader(stream);
		return streamReader.ReadToEnd().Split(new string[1] { Environment.NewLine }, StringSplitOptions.None);
	}
}
