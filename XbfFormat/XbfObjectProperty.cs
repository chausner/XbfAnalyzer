namespace XbfAnalyzer.Xbf;

public class XbfObjectProperty
{
    internal XbfObjectProperty(string name, object value)
    {
        Name = name;
        Value = value;
    }

    public string Name { get; private set; }
    public object Value { get; private set; }
}
