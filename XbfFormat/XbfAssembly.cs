namespace XbfAnalyzer.Xbf;

public class XbfAssembly
{
    internal XbfAssembly(XbfReader xbf, BinaryReader reader)
    {
        Kind = (XbfAssemblyKind)reader.ReadInt32();
        if ((int)Kind < 0 || (int)Kind > 5)
            throw new InvalidDataException($"Unknown XbfAssemblyKind: {Kind}");
        int stringID = reader.ReadInt32();
        Name = xbf.StringTable[stringID];
    }

    public XbfAssemblyKind Kind { get; }
    public string Name { get; }
}

public enum XbfAssemblyKind
{
    // TODO: These values are from XBF v1 -- are they still correct?
    Unknown = 0,
    Native = 1,
    Managed = 2,
    System = 3,
    Parser = 4,
    Alternate = 5,
}
