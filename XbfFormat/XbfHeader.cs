namespace XbfTools.XbfFormat;

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

    public byte[] MagicNumber { get; }
    public uint MetadataSize { get; }
    public uint NodeSize { get; }
    public uint MajorFileVersion { get; }
    public uint MinorFileVersion { get; }
    public ulong StringTableOffset { get; }
    public ulong AssemblyTableOffset { get; }
    public ulong TypeNamespaceTableOffset { get; }
    public ulong TypeTableOffset { get; }
    public ulong PropertyTableOffset { get; }
    public ulong XmlNamespaceTableOffset { get; }
    public string Hash { get; }
}
