# XbfTools
Collection of tools for parsing, analyzing and converting XBF v2 (XAML Binary Format) files.

This project is a continuation of the work done in [XbfAnalyzer](https://github.com/misenhower/XbfAnalyzer) by [Matt Isenhower](https://github.com/misenhower).

> [!NOTE]
> This program is intended to be used for research only. Do not use it for production purposes. Be aware of the following limitations:
> * XbfTools cannot parse all XBF files as some features are not supported.
> * The node parser was derived entirely from analyzing generated XBF files, so there may be some mistakes, inaccuracies, etc.
> * The converted XAML output may not completely match the original file content.

## Projects

- **xbf2xaml**: Command-line application for converting XBF files to XAML.
- **XbfFormat**: .NET library for parsing XBF files and converting them to XAML.
- **XbfAnalyzer**: WPF application for parsing and analyzing XBF files. It displays a textual representation of the XAML node structure contained in an XBF file.
- **XbfTest**: Command-line application for validating the XbfFormat library on a directory of XBF files.

## Prerequisites
- .NET 10

## Usage

### xbf2xaml
To convert an input XBF file to XAML, run the following command in the terminal:
```
xbf2xaml <input.xbf> -o <output.xaml>
```

You may optionally pass the verbose flag to print detailed information about the XBF file structure:
```
xbf2xaml <input.xbf> -o <output.xaml> --verbose
```

### XbfAnalyzer
XbfAnalyzer is built as a simple WPF application.
To use it, drag and drop an XBF file into the main window.

At the top, the application displays a textual representation of the nodes data in the XBF file.
At the bottom left and right, the contents of the object and collection stacks are displayed for the currently selected node.

By default, the application displays the root node section and all other referenced node sections.
The slider at the top can be used to filter the output to one specific node section.

## See also
* [PriTools](https://github.com/chausner/PriTools): Tools for parsing and exploring PRI (Package Resource Index) files. Integrates functionality from this project to view XBF files embedded in PRI files.
* [xbfdump](https://github.com/riverar/xbfdump): Command-line tool for dumping the contents of XBF files. Leverages the Windows SDK's XBF parser but does not support conversion to XAML.
* [XbfDump](https://github.com/WalkingCat/XbfDump) and [XbfDecompiler](https://github.com/TeamGnome/XbfDecompiler): Tools for working with XBF v1 files.
