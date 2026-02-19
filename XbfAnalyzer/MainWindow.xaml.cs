using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using XbfAnalyzer.Xbf;

namespace XbfAnalyzer;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    XbfReader xbfReader;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        string[] droppedItems = e.Data.GetData(DataFormats.FileDrop) as string[];

        if (droppedItems != null && droppedItems.Length == 1)
            e.Effects = DragDropEffects.Move;
        else
            e.Effects = DragDropEffects.None;

        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        string[] droppedItems = e.Data.GetData(DataFormats.FileDrop) as string[];

        if (droppedItems == null || droppedItems.Length < 1)
            return;

        LoadXbf(droppedItems[0]);
    }

    private void commandsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        XbfCommand command = commandsListView.SelectedItem as XbfCommand;

        objectStackListBox.ItemsSource = command?.ObjectStack?.Select(ObjectInfo);
        objectCollectionStackListBox.ItemsSource = command?.ObjectCollectionStack?.Select(CollectionInfo);

        string ObjectInfo(XbfObject obj) => $"{obj.TypeName} {obj.Key ?? obj.Name}";

        string CollectionInfo(XbfObjectCollection col) => $"{col.Count} items, owner: ({ObjectInfo(col.Owner)}).{col.OwnerProperty}";
    }

    private void nodeSectionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (xbfReader == null)
            return;

        int nodeSection = (int)e.NewValue;

        GenerateDisassembly(nodeSection);
    }

    private void LoadXbf(string path)
    {
        if (xbfReader != null)
        {
            xbfReader.Dispose();
            xbfReader = null;
        }

        try
        {
            xbfReader = new XbfReader(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to read XBF file: {ex}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (xbfReader.Header.MajorFileVersion != 2)
        {
            MessageBox.Show($"Unsupported XBF file version: {xbfReader.Header.MajorFileVersion}.{xbfReader.Header.MinorFileVersion}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            xbfReader.Dispose();
            xbfReader = null;
            return;
        }

        nodeSectionSlider.Maximum = xbfReader.NodeSectionTable.Length - 1;

        GenerateDisassembly(0);
    }

    private void GenerateDisassembly(int nodeSection)
    {
        try
        {
            XbfDisassembly disassembly = nodeSection switch
            {
                0 => xbfReader.DisassembleRootNodeSection(),
                _ => xbfReader.DisassembleNodeSection(xbfReader.NodeSectionTable[nodeSection]),
            };

            commandsListView.ItemsSource = disassembly.Commands;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to disassemble node section {nodeSection} of XBF file: {ex}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
