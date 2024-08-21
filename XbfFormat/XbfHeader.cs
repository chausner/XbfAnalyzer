namespace XbfAnalyzer.Xbf;

public class XbfHeader
{
    internal XbfHeader(BinaryReader reader)
    {
        // Verify magic number
        var magicNumber = reader.ReadBytes(4);
        if (magicNumber[0] != 'X' || magicNumber[1] != 'B' || magicNumber[2] != 'F' || magicNumber[3] != 0)
            throw new InvalidDataException("File does not have XBF header");
        MagicNumber = magicNumber;

        MetadataSize = reader.ReadUInt32();
        NodeSize = reader.ReadUInt32();
        MajorFileVersion = reader.ReadUInt32();
        MinorFileVersion = reader.ReadUInt32();
        StringTableOffset = reader.ReadUInt64();
        AssemblyTableOffset = reader.ReadUInt64();
        TypeNamespaceTableOffset = reader.ReadUInt64();
        TypeTableOffset = reader.ReadUInt64();
        PropertyTableOffset = reader.ReadUInt64();
        XmlNamespaceTableOffset = reader.ReadUInt64();
        Hash = new string(reader.ReadChars(32));
    }

    public byte[] MagicNumber { get; private set; }
    public uint MetadataSize { get; private set; }
    public uint NodeSize { get; private set; }
    public uint MajorFileVersion { get; private set; }
    public uint MinorFileVersion { get; private set; }
    public ulong StringTableOffset { get; private set; }
    public ulong AssemblyTableOffset { get; private set; }
    public ulong TypeNamespaceTableOffset { get; private set; }
    public ulong TypeTableOffset { get; private set; }
    public ulong PropertyTableOffset { get; private set; }
    public ulong XmlNamespaceTableOffset { get; private set; }
    public string Hash { get; private set; }
}
