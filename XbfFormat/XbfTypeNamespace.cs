namespace XbfAnalyzer.Xbf;

public class XbfTypeNamespace
{
    internal XbfTypeNamespace(XbfReader xbf, BinaryReader reader)
    {
        int assemblyID = reader.ReadInt32();
        Assembly = xbf.AssemblyTable[assemblyID];
        int nameID = reader.ReadInt32();
        Name = xbf.StringTable[nameID];
    }

    public XbfAssembly Assembly { get; private set; }
    public string Name { get; private set; }
}
