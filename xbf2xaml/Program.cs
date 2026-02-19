namespace xbf2xaml;

using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Text;
using XbfAnalyzer.Xbf;

internal class Program
{
    public static int Main(string[] args)
    {
        var englishCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentCulture = englishCulture;
        CultureInfo.DefaultThreadCurrentCulture = englishCulture;
        CultureInfo.CurrentUICulture = englishCulture;
        CultureInfo.DefaultThreadCurrentUICulture = englishCulture;

        var inputArg = new Argument<FileInfo>("input") { Description = "Path to the input .xbf file" }.AcceptExistingOnly();
        var outputOpt = new Option<FileInfo?>("--output", "-o") { Description = "Path to the output .xaml file (defaults to input with extension .xaml)" };
        var verboseOpt = new Option<bool>("--verbose", "-v") { Description = "Enable verbose output" };

        var rootCommand = new RootCommand("Convert XAML Binary Format (XBF) v2 files to XAML")
        {
            inputArg,
            outputOpt,
            verboseOpt
        };

        rootCommand.SetAction(parseResult =>
        {
            var input = parseResult.GetValue(inputArg)!;
            var output = parseResult.GetValue(outputOpt);
            var verbose = parseResult.GetValue(verboseOpt);

            try
            {
                return Run(input, output, verbose);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.ToString());
                return 1;
            }
        });

        return rootCommand.Parse(args).Invoke();
    }

    private static int Run(FileInfo input, FileInfo? output, bool verbose)
    {
        var outFile = output ?? new FileInfo(Path.ChangeExtension(input.FullName, ".xaml"));

        Console.WriteLine($"Input: {input.FullName}");
        Console.WriteLine($"Output: {outFile.FullName}");
        Console.WriteLine();

        using var xbfReader = new XbfReader(input.FullName);

        if (verbose)
            PrintHeaderAndTables(xbfReader);        

        if (xbfReader.Header.MajorFileVersion != 2)
        {
            Console.WriteLine("Only XBF v2 files are supported. File has version {0}.{1}", xbfReader.Header.MajorFileVersion, xbfReader.Header.MinorFileVersion);
            return 1;
        }

        var rootObject = xbfReader.ReadRootNodeSection();
        string xaml = rootObject.ToString();

        File.WriteAllText(outFile.FullName, xaml, Encoding.UTF8);

        Console.WriteLine("File converted successfully.");

        return 0;
    }

    private static void PrintHeaderAndTables(XbfReader xbfReader)
    {
        void LogHexAddressMessage(int address, string message)
        {
            Console.WriteLine("{0:X4}: {1}", address, message);
        }

        void FormatHexAddressMessage(int address, string format, params object[] args)
        {
            LogHexAddressMessage(address, string.Format(format, args));
        }

        // Header
        Console.WriteLine("File version: {0}.{1}", xbfReader.Header.MajorFileVersion, xbfReader.Header.MinorFileVersion);
        Console.WriteLine("Metadata Size: {0} (0x{0:X})", xbfReader.Header.MetadataSize);
        Console.WriteLine("Node Size: {0} (0x{0:X})", xbfReader.Header.NodeSize);
        // The offset values seem to be off by 12 bytes compared to the actual positions of these elements in the XBF files.
        // Displaying these as adjusted values to make external analysis easier.
        Console.WriteLine("Adjusted String Table Offset: {0} (0x{0:X})", xbfReader.Header.StringTableOffset + 12);
        Console.WriteLine("Adjusted Assembly Table Offset: {0} (0x{0:X})", xbfReader.Header.AssemblyTableOffset + 12);
        Console.WriteLine("Adjusted Type Namespace Table Offset: {0} (0x{0:X})", xbfReader.Header.TypeNamespaceTableOffset + 12);
        Console.WriteLine("Adjusted Type Table Offset: {0} (0x{0:X})", xbfReader.Header.TypeTableOffset + 12);
        Console.WriteLine("Adjusted Property Table Offset: {0} (0x{0:X})", xbfReader.Header.PropertyTableOffset + 12);
        Console.WriteLine("Adjusted XML Namespace Table Offset: {0} (0x{0:X})", xbfReader.Header.XmlNamespaceTableOffset + 12);
        Console.WriteLine();

        // String table
        Console.WriteLine("String table:");
        for (int i = 0; i < xbfReader.StringTable.Length; i++)
            LogHexAddressMessage(i, xbfReader.StringTable[i]);
        Console.WriteLine();

        // Assembly table
        Console.WriteLine("Assembly table:");
        for (int i = 0; i < xbfReader.AssemblyTable.Length; i++)
        {
            var assembly = xbfReader.AssemblyTable[i];
            FormatHexAddressMessage(i, "{0} (Kind: {1})", assembly.Name, assembly.Kind);
        }
        Console.WriteLine();

        // Type namespace table
        Console.WriteLine("Type namespace table:");
        for (int i = 0; i < xbfReader.TypeNamespaceTable.Length; i++)
        {
            var typeNamespace = xbfReader.TypeNamespaceTable[i];
            FormatHexAddressMessage(i, "{0} (Assembly: {1})", typeNamespace.Name, typeNamespace.Assembly.Name);
        }
        Console.WriteLine();

        // Type table
        Console.WriteLine("Type table:");
        for (int i = 0; i < xbfReader.TypeTable.Length; i++)
        {
            var type = xbfReader.TypeTable[i];
            FormatHexAddressMessage(i, "{0} (Namespace: {1}, Flags: {2})", type.Name, type.Namespace?.Name ?? "?", type.Flags);
        }
        Console.WriteLine();

        // Property table
        Console.WriteLine("Property table:");
        for (int i = 0; i < xbfReader.PropertyTable.Length; i++)
        {
            var property = xbfReader.PropertyTable[i];
            FormatHexAddressMessage(i, "{0} (Type: {1}, Flags: {2})", property.Name, property.Type.Name, property.Flags);
        }
        Console.WriteLine();

        // XML namespace table
        Console.WriteLine("XML namespace table:");
        for (int i = 0; i < xbfReader.XmlNamespaceTable.Length; i++)
            LogHexAddressMessage(i, xbfReader.XmlNamespaceTable[i]);
        Console.WriteLine();

        if (xbfReader.Header.MajorFileVersion == 2)
        {
            // Node section table
            Console.WriteLine("Node section table:");
            for (int i = 0; i < xbfReader.NodeSectionTable!.Length; i++)
                FormatHexAddressMessage(i, "Node offset: {0} (0x{0:X}) Positional offset: {1} (0x{1:X})", xbfReader.NodeSectionTable[i].NodeOffset, xbfReader.NodeSectionTable[i].PositionalOffset);
            Console.WriteLine();
        }
    }
}
