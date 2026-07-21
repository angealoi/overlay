using System.IO;

namespace SevenZip;

internal interface IWriteCoderProperties
{
	void WriteCoderProperties(Stream outStream);
}
