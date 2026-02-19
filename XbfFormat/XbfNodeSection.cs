namespace XbfTools.XbfFormat;

public class XbfNodeSection
{
    internal XbfNodeSection(XbfReader xbf, BinaryReader reader)
    {
        NodeOffset = reader.ReadInt32();
        PositionalOffset = reader.ReadInt32();
    }

    public int NodeOffset { get; }
    public int PositionalOffset { get; }
}
