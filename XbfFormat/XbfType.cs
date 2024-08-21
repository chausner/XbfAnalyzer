namespace XbfAnalyzer.Xbf;

public class XbfType
{
    internal XbfType(XbfReader xbf, BinaryReader reader)
    {
        Flags = (XbfTypeFlags)reader.ReadInt32();
        int namespaceID = reader.ReadInt32();
        Namespace = xbf.TypeNamespaceTable[namespaceID];
        int nameID = reader.ReadInt32();
        Name = xbf.StringTable[nameID];
    }

    public XbfTypeFlags Flags { get; }
    public XbfTypeNamespace Namespace { get; }
    public string Name { get; }
}

public enum XbfTypeFlags
{
    // TODO: These values are from XBF v1 -- are they still correct?
    None = 0,
    IsMarkupDirective = 1,
}
