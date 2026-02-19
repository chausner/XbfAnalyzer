namespace XbfTools.XbfFormat;

public class XbfProperty
{
    internal XbfProperty(XbfReader xbf, BinaryReader reader)
    {
        Flags = (XbfPropertyFlags)reader.ReadInt32();
        if ((int)Flags < 0 || (int)Flags > 16 + 8 + 4 + 2 + 1)
            throw new InvalidDataException($"Unknown XbfPropertyFlags: {Flags}");
        int typeID = reader.ReadInt32();
        Type = xbf.TypeTable[typeID];
        int nameID = reader.ReadInt32();
        Name = xbf.StringTable[nameID];
    }

    public XbfPropertyFlags Flags { get; }
    public XbfType Type { get; }
    public string Name { get; }
}

[Flags]
public enum XbfPropertyFlags
{
    // TODO: These values are from XBF v1 -- are they still correct?
    None = 0,
    IsXmlProperty = 1,
    IsMarkupDirective = 2,
    IsImplicitProperty = 4,
    Unknown8 = 8,
    Unknown16 = 16
}
