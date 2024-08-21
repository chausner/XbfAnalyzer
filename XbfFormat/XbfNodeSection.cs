namespace XbfAnalyzer.Xbf;

public class XbfNodeSection
{
    public XbfNodeSection(XbfReader xbf, BinaryReader reader)
    {
        NodeOffset = reader.ReadInt32();
        PositionalOffset = reader.ReadInt32();
    }

    public int NodeOffset { get; private set; }
    public int PositionalOffset { get; private set; }
}
