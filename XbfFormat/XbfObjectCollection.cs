using System.Text;

namespace XbfTools.XbfFormat;

public class XbfObjectCollection : List<XbfObject>, ICloneable
{
    public XbfObject Owner { get; }
    public string OwnerProperty { get; }

    public XbfObjectCollection(XbfObject owner, string ownerProperty)
    {
        Owner = owner;
        OwnerProperty = ownerProperty;
    }

    public object Clone()
    {
        XbfObjectCollection clone = new XbfObjectCollection((XbfObject)Owner.Clone(), OwnerProperty);

        clone.AddRange(this.Select(o => (XbfObject)o.Clone()));

        return clone;
    }

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
