﻿namespace XbfAnalyzer.Xbf;

public class XbfAssembly
{
    internal XbfAssembly(XbfReader xbf, BinaryReader reader)
    {
        Kind = (XbfAssemblyKind)reader.ReadInt32();
        int stringID = reader.ReadInt32();
        Name = xbf.StringTable[stringID];
    }

    public XbfAssemblyKind Kind { get; private set; }
    public string Name { get; private set; }
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