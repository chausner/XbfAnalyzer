using System.Text;

namespace XbfAnalyzer.Xbf;

public class XbfObjectCollection : List<XbfObject>
{
    public override string ToString()
    {
        return ToString(0);
    }

    public string ToString(int indentLevel)
    {
        StringBuilder sb = new StringBuilder();

        foreach (var obj in this)
            sb.AppendLine(obj.ToString(indentLevel));
        return sb.ToString();
    }
}
