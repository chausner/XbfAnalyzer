using XbfAnalyzer.Xbf;

List<string> paths = [];

foreach (string arg in args)
{
    if (Directory.Exists(arg))
        paths.AddRange(Directory.GetFiles(arg, "*.xbf", SearchOption.AllDirectories));
    else if (File.Exists(arg))
        paths.Add(arg);
    else
        throw new Exception($"Path not found: {arg}");
}

int success = 0;
Dictionary<string, int> failures = [];
List<string> failingFiles = [];

foreach (string path in paths)
{
    XbfReader? reader = null;

    try
    {
        reader = new XbfReader(path);
        string xaml = reader.ReadRootNodeSection().ToString();
        success++;
    }
    catch (Exception ex)
    {
        failingFiles.Add(path);

        Console.WriteLine(path);
        Console.WriteLine(ex.ToString());
        Console.WriteLine();

        while (ex is InvalidDataException invalidDataException && 
            invalidDataException.Message.StartsWith("Error parsing node stream at file position") && 
            invalidDataException.InnerException != null)
            ex = invalidDataException.InnerException;

        string exceptionText = string.Join("\r\n", ex.ToString().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Take(4));

        if (failures.ContainsKey(exceptionText))
            failures[exceptionText]++;
        else
            failures[exceptionText] = 1;
    }
    finally
    {
        reader?.Dispose();
    }
}

Console.WriteLine();
Console.WriteLine($"Total files: {paths.Count}");
Console.WriteLine($"Success: {success}");
Console.WriteLine($"Failures ({failures.Values.Sum()}):");
foreach (var kvp in failures.OrderByDescending(kvp => kvp.Value))
    Console.WriteLine($"({kvp.Value}): {kvp.Key}");

Console.WriteLine();
Console.WriteLine("Failing files:");
foreach (var path in failingFiles.OrderBy(p => new FileInfo(p).Length))
    Console.WriteLine(path);
