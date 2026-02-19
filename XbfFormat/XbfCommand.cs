namespace XbfTools.XbfFormat;

public class XbfCommand
{
    public byte[]? Bytes { get; }
    public int NodeSection { get; }
    public string Command { get; }
    public IReadOnlyList<XbfObject>? ObjectStack { get; }
    public IReadOnlyList<XbfObjectCollection>? ObjectCollectionStack { get; }
    public int Indent { get; }

    public XbfCommand(BinaryReader? reader, long commandPosition, int nodeSection, string command, IEnumerable<XbfObject>? objectStack, IEnumerable<XbfObjectCollection>? objectCollectionStack, int indent)
    {
        if (reader != null)
        {
            long originalPosition = reader.BaseStream.Position;

            reader.BaseStream.Seek(commandPosition, SeekOrigin.Begin);

            Bytes = reader.ReadBytes((int)(originalPosition - commandPosition));

            reader.BaseStream.Seek(originalPosition, SeekOrigin.Begin);
        }

        NodeSection = nodeSection;
        Command = command;

        if (objectStack != null)
            ObjectStack = objectStack.Select(o => (XbfObject)o.Clone()).ToArray();

        if (objectCollectionStack != null)
            ObjectCollectionStack = objectCollectionStack.Select(o => (XbfObjectCollection)o.Clone()).ToArray();

        Indent = indent;
    }

    public string? BytesDisplay => Bytes != null ? Utils.Hex(Bytes) : null;

    public string CommandDisplay => new string(' ', Indent * 2) + Command;

    public int? ObjectStackSize => ObjectStack?.Count;

    public int? ObjectCollectionStackSize => ObjectCollectionStack?.Count;
}
