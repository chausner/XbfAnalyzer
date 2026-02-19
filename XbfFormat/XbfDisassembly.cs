namespace XbfAnalyzer.Xbf;

using System.Collections.Generic;

public class XbfDisassembly
{
    private List<XbfCommand> commands = new List<XbfCommand>();
    private int _indent;

    public IReadOnlyList<XbfCommand> Commands => commands;

    internal void AddCommand(BinaryReader reader, long commandPosition, int nodeSection, string command, IEnumerable<XbfObject> objectStack, IEnumerable<XbfObjectCollection> objectCollectionStack)
    {
        XbfCommand xbfCommand = new XbfCommand(reader, commandPosition, nodeSection, command, objectStack, objectCollectionStack, _indent);

        commands.Add(xbfCommand);
    }

    internal void Indent()
    {
        _indent++;
    }

    internal void Unindent()
    {
        if (_indent == 0)
            return;

        _indent--;
    }
}
