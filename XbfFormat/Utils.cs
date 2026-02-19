namespace XbfTools.XbfFormat;

internal static class Utils
{
    public static string Hex<T>(IEnumerable<T> values)
    {
        return string.Join(" ", values.Select(v => Convert.ToInt32(v).ToString("X2")));
    }
}
