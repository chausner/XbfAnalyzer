namespace XbfAnalyzer.Xbf;

public class XbfType
{
    internal XbfType(XbfReader xbf, BinaryReader reader)
    {
        Flags = (XbfTypeFlags)reader.ReadInt32();
        if ((int)Flags < 0 || (int)Flags > 2 + 1)
            throw new InvalidDataException($"Unknown XbfTypeFlags: {Flags}");
        int namespaceID = reader.ReadInt32();
        Namespace = xbf.TypeNamespaceTable[namespaceID];
        int nameID = reader.ReadInt32();
        Name = xbf.StringTable[nameID];
    }

    public XbfTypeFlags Flags { get; }
    public XbfTypeNamespace Namespace { get; }
    public string Name { get; }
}

[Flags]
public enum XbfTypeFlags
{
    // TODO: These values are from XBF v1 -- are they still correct?
    None = 0,
    IsMarkupDirective = 1,
    Unknown2 = 2
}
