using System.Text;

namespace XbfAnalyzer.Xbf;

public class XbfReader : IDisposable
{
    public XbfHeader Header { get; }
    public string[] StringTable { get; }
    public XbfAssembly[] AssemblyTable { get; }
    public XbfTypeNamespace[] TypeNamespaceTable { get; }
    public XbfType[] TypeTable { get; }
    public XbfProperty[] PropertyTable { get; }
    public string[] XmlNamespaceTable { get; }
    public XbfNodeSection[]? NodeSectionTable { get; }

    private readonly BinaryReaderEx _reader;
    private readonly int _firstNodeSectionPosition;
    private readonly Dictionary<string, string> _namespacePrefixes = new();
    private readonly Stack<XbfObject> _rootObjectStack = new();
    private readonly Stack<XbfObject> _objectStack = new();
    private readonly Stack<XbfObjectCollection> _objectCollectionStack = new();

    private XbfDisassembly? _disassembly;

    public XbfReader(Stream stream, bool leaveOpen = false)
    {
        _reader = new BinaryReaderEx(stream, Encoding.Unicode, leaveOpen);

        Header = new XbfHeader(_reader);
        StringTable = ReadStringTable();
        AssemblyTable = ReadTable(r => new XbfAssembly(this, r));
        TypeNamespaceTable = ReadTable(r => new XbfTypeNamespace(this, r));
        TypeTable = ReadTable(r => new XbfType(this, r));
        PropertyTable = ReadTable(r => new XbfProperty(this, r));
        XmlNamespaceTable = ReadTable(r => StringTable[r.ReadInt32()]);

        if (Header.MajorFileVersion >= 2)
        {
            // The first value is an int that indicates how many node sections we have.
            // Each node section comes in two parts: the nodes themselves come first, followed by line/column data (positional data
            // which indicates where the objects were located in the source XAML file).
            // For each node section, there will be two offset numbers: one for the nodes, and one for the positional data.
            //
            // There seem to be a few situations that trigger a separate node section to be generated, including:
            // - Visual state data (VisualStateGroups, VisualStates, etc.) seem to always generate a separate section.
            //   Some visual state information is included in the primary node stream (after control character 0x0F) but fully-expanded
            //   objects are only available in the secondary node streams (one per object that has VisualStateGroups defined).
            // - Resource collections (i.e., groups of objects with x:Key values) seem generate a separate section when they have more than one item.
            //   Different types of resources seem to generate multiple resource collections for the same object.
            //   For example, Brush resources are listed separately from Style resources.
            //
            // Note that secondary node sections can also contain references to other node sections as well.
            NodeSectionTable = ReadTable(r => new XbfNodeSection(this, _reader));

            // We are now at the position in the stream of the first actual node data. We'll need this position later.
            _firstNodeSectionPosition = (int)_reader.BaseStream.Position;
        }
    }

    public XbfReader(string path) : this(File.OpenRead(path))
    {        
    }

    private string ReadString(BinaryReader reader)
    {
        return new string(reader.ReadChars(reader.ReadInt32()));
    }

    private string[] ReadStringTable()
    {
        int stringCount = _reader.ReadInt32();
        string[] stringTable = new string[stringCount];

        bool isXbfV2 = Header.MajorFileVersion >= 2;

        for (int i = 0; i < stringCount; i++)
        {
            stringTable[i] = ReadString(_reader);

            // XBF v2 files have two extra null bytes after each string.
            if (isXbfV2)
                if (_reader.ReadUInt16() != 0)
                    throw new InvalidDataException("Unexpected value");
        }

        return stringTable;
    }

    private T[] ReadTable<T>(Func<BinaryReader, T> objectGenerator)
    {
        int count = _reader.ReadInt32();
        T[] result = new T[count];

        for (int i = 0; i < count; i++)
            result[i] = objectGenerator(_reader);

        return result;
    }

    public XbfObject ReadRootNodeSection()
    {
        if (disposedValue)
            throw new ObjectDisposedException(nameof(XbfReader));

        if (Header.MajorFileVersion != 2)
            throw new NotSupportedException("Only XBF v2 files are supported.");

        int startPosition = _firstNodeSectionPosition + NodeSectionTable![0].NodeOffset;
        int endPosition = _firstNodeSectionPosition + NodeSectionTable[0].PositionalOffset;

        _reader.BaseStream.Seek(startPosition, SeekOrigin.Begin);

        _disassembly = null;

        _rootObjectStack.Clear();
        _objectStack.Clear();
        _objectCollectionStack.Clear();

        ReadRoot(_reader, endPosition);

        if (_objectStack.Count != 1)
            throw new InvalidDataException("_objectStack corrupted");

        if (_objectCollectionStack.Count != 0)
            throw new InvalidDataException("_objectCollectionStack corrupted");

        XbfObject rootObject = _objectStack.Pop();

        return rootObject;
    }

    public XbfDisassembly DisassembleRootNodeSection()
    {
        if (disposedValue)
            throw new ObjectDisposedException(nameof(XbfReader));

        int startPosition = _firstNodeSectionPosition + NodeSectionTable![0].NodeOffset;
        int endPosition = _firstNodeSectionPosition + NodeSectionTable[0].PositionalOffset;

        _reader.BaseStream.Seek(startPosition, SeekOrigin.Begin);

        _disassembly = new XbfDisassembly();

        _rootObjectStack.Clear();
        _objectStack.Clear();
        _objectCollectionStack.Clear();

        ReadRoot(_reader, endPosition);

        return _disassembly;
    }

    public XbfDisassembly DisassembleNodeSection(XbfNodeSection nodeSection)
    {
        if (disposedValue)
            throw new ObjectDisposedException(nameof(XbfReader));

        int startPosition = _firstNodeSectionPosition + nodeSection.NodeOffset;
        int endPosition = _firstNodeSectionPosition + nodeSection.PositionalOffset;

        _reader.BaseStream.Seek(startPosition, SeekOrigin.Begin);

        _disassembly = new XbfDisassembly();

        _rootObjectStack.Clear();
        _objectStack.Clear();
        _objectCollectionStack.Clear();

        XbfObject rootObject = new XbfObject();
        _rootObjectStack.Push(rootObject);
        _objectStack.Push(rootObject);

        _objectCollectionStack.Push(rootObject.Children);

        ReadNodes(_reader, endPosition);

        return _disassembly;
    }

    private void ReadRoot(BinaryReaderEx reader, int endPosition)
    {
        // The first node section contains the primary XAML data (and the root XAML object)
        XbfObject rootObject = new XbfObject();
        _rootObjectStack.Push(rootObject);
        _objectStack.Push(rootObject);

        _objectCollectionStack.Push(rootObject.Children);

        try
        {
            // Read the node bytes
            while (reader.BaseStream.Position < endPosition)
            {
                long commandPosition = reader.BaseStream.Position;
                byte nodeType = reader.ReadByte();
                switch (nodeType)
                {
                    case 0x12: // This usually appears to be the first byte encountered. I'm not sure what the difference between 0x12 and 0x03 is.
                    case 0x03: // Root node namespace declaration
                        {
                            string namespaceName = XmlNamespaceTable[reader.ReadUInt16()];
                            string prefix = ReadString(reader);
                            _namespacePrefixes[namespaceName] = prefix;
                            if (!string.IsNullOrEmpty(prefix))
                                prefix = "xmlns:" + prefix;
                            else
                                prefix = "xmlns";
                            rootObject.Properties.Add(new XbfObjectProperty(prefix, namespaceName));
                            EmitDisassembly(reader, commandPosition, $"rootnamespace {prefix}={namespaceName}");
                        }
                        break;

                    case 0x0B: // Indicates the class of the root object (i.e. x:Class)
                        {
                            string className = ReadString(reader);
                            rootObject.Properties.Add(new XbfObjectProperty("x:Class", className));
                            EmitDisassembly(reader, commandPosition, $"rootclass {className}");
                        }
                        break;

                    case 0x17: // Root object begin
                        {
                            rootObject.TypeName = GetTypeName(reader.ReadUInt16());
                            EmitDisassembly(reader, commandPosition, $"rootbegin {rootObject.TypeName}");
                            _disassembly?.Indent();
                            ReadNodes(reader, endPosition);
                            goto exitLoop;
                        }

                    default:
                        throw new InvalidDataException(string.Format("Unrecognized character 0x{0:X2} in node stream", nodeType));
                }
            }
        }
        catch (Exception e)
        {
            throw new InvalidDataException(string.Format("Error parsing node stream at file position {0} (0x{0:X}) (node start position was: {1} (0x{1:X}))" + Environment.NewLine, reader.BaseStream.Position - 1, _firstNodeSectionPosition) + e.ToString(), e);
        }

        exitLoop:

        if (_rootObjectStack.Pop() != rootObject)
            throw new InvalidDataException("_rootObjectStack corrupted");

        if (_objectStack.Peek() != rootObject)
            throw new InvalidDataException("_objectStack corrupted");
    }

    private void ReadNodes(BinaryReaderEx reader, int endPosition, bool readSingleObject = false, bool readSingleNode = false)
    {
        XbfObject? singleObject = null;
        while (reader.BaseStream.Position < endPosition)
        {
            long commandPosition = reader.BaseStream.Position;
            byte nodeType = reader.ReadByte();
            switch (nodeType)
            {
                case 0x01: // This only occurs at the beginning of some secondary node sections -- not sure what it means
                    EmitDisassembly(reader, commandPosition, $"unk");
                    break;

                case 0x04: 
                    // This seems to have at least four different interpretations -- not sure how to properly decide which one is correct
                    // The logic below seems to work but is most definitely not the correct way

                    // If the following condition is true, we are in a collection that was started with collectionbegin,
                    // making this node likely to be a verbatim string.
                    if (_objectCollectionStack.Peek() != _objectStack.Peek().Children) // Verbatim text, appears in TextBlock.Inlines
                    {
                        object text = GetPropertyValue(reader);
                        var obj = new XbfObject() { TypeName = "Verbatim" }; // For simplicity, use a fake Verbatim object to store the text
                        obj.Properties.Add(new XbfObjectProperty("Value", text));
                        _objectStack.Push(obj);
                        EmitDisassembly(reader, commandPosition, $"verbatim {text}");
                    }
                    else if (_objectStack.Peek() == _rootObjectStack.Peek()) // Class of root object
                    {
                        object cl = GetPropertyValue(reader);
                        _objectStack.Peek().Properties.Add(new XbfObjectProperty("x:Class", cl));
                        EmitDisassembly(reader, commandPosition, $"rootclass {cl}");
                    }
                    else // Values encountered here in some files are class modifiers ("private", "internal", "public") or event handler names (e.g. "CancelButton_Click")
                    {
                        object val = GetPropertyValue(reader);
                        EmitDisassembly(reader, commandPosition, $"classmodifier/event {val}");
                    }
                    break;

                case 0x0C: // Connection
                    // This byte (0x0C) indicates the current object needs to be connected to something in the generated Connect method.
                    // This can include event handlers, named objects (to be accessed via instance variables), etc.
                    // Event handlers aren't explicitly included as part of the XBF node stream since they're wired up in (generated) code.
                    // Each object that needs to be connected to something has a unique ID indicated in this section.

                    // Connection ID
                    int id = (int)GetPropertyValue(reader);
                    _objectStack.Peek().ConnectionID = id;
                    EmitDisassembly(reader, commandPosition, $"connect {id}");
                    break;

                case 0x0D: // x:Name
                    {
                        object value = GetPropertyValue(reader);
                        _objectStack.Peek().Name = value.ToString();
                        EmitDisassembly(reader, commandPosition, $"objname {value}");
                    }
                    break;

                case 0x0E: // x:Uid
                    {
                        object value = GetPropertyValue(reader);
                        _objectStack.Peek().Uid = value.ToString();
                        EmitDisassembly(reader, commandPosition, $"objuid {value}");
                    }
                    break;

                case 0x11: // DataTemplate
                    ReadDataTemplate(reader, commandPosition);
                    break;

                case 0x1A: // Property
                case 0x1B: // Property (not sure what the difference from 0x1A is)
                    {
                        string propertyName = GetPropertyName(reader.ReadUInt16());
                        object propertyValue = GetPropertyValue(reader);
                        _objectStack.Peek().Properties.Add(new XbfObjectProperty(propertyName, propertyValue));
                        EmitDisassembly(reader, commandPosition, $"property {propertyName}={propertyValue}");
                    }
                    break;

                case 0x1C: // SetValueTypeConvertedResolvedPropertyNode
                    {
                        string propertyName = GetPropertyName(reader.ReadUInt16());
                        string resolvedPropertyName = GetPropertyName(reader.ReadUInt16());
                        _objectStack.Peek().Properties.Add(new XbfObjectProperty(propertyName, resolvedPropertyName));
                        EmitDisassembly(reader, commandPosition, $"property {propertyName}={resolvedPropertyName}");
                    }
                    break;

                case 0x1D: // Style
                    {
                        string propertyName = GetPropertyName(reader.ReadUInt16()); // Always "TargetType"
                        string targetTypeName = GetTypeName(reader.ReadUInt16());
                        _objectStack.Peek().Properties.Add(new XbfObjectProperty(propertyName, targetTypeName));
                        EmitDisassembly(reader, commandPosition, $"style {propertyName} targettype:{targetTypeName}");
                    }
                    break;

                case 0x1E: // StaticResource
                    {
                        string propertyName = GetPropertyName(reader.ReadUInt16());
                        object propertyValue = GetPropertyValue(reader);
                        propertyValue = string.Format("{{StaticResource {0}}}", propertyValue);
                        _objectStack.Peek().Properties.Add(new XbfObjectProperty(propertyName, propertyValue));

                        EmitDisassembly(reader, commandPosition, $"staticresource {propertyName}={propertyValue}");
                    }
                    break;

                case 0x1F: // TemplateBinding
                    {
                        string propertyName = GetPropertyName(reader.ReadUInt16());
                        string bindingPath = GetPropertyName(reader.ReadUInt16());
                        bindingPath = string.Format("{{TemplateBinding {0}}}", bindingPath);
                        _objectStack.Peek().Properties.Add(new XbfObjectProperty(propertyName, bindingPath));

                        EmitDisassembly(reader, commandPosition, $"templatebinding {propertyName}={bindingPath}");
                    }
                    break;

                case 0x24: // ThemeResource
                    {
                        string propertyName = GetPropertyName(reader.ReadUInt16());
                        object propertyValue = GetPropertyValue(reader);
                        propertyValue = string.Format("{{ThemeResource {0}}}", propertyValue);
                        _objectStack.Peek().Properties.Add(new XbfObjectProperty(propertyName, propertyValue));

                        EmitDisassembly(reader, commandPosition, $"themeresource {propertyName}={propertyValue}");
                    }
                    break;

                case 0x22: // StaticResource object
                    {
                        object propertyValue = GetPropertyValue(reader);
                        var obj = new XbfObject() { TypeName = "StaticResource" };
                        obj.Properties.Add(new XbfObjectProperty("ResourceKey", propertyValue));
                        _objectStack.Push(obj);

                        EmitDisassembly(reader, commandPosition, $"staticresourceobj {propertyValue}");

                        if (readSingleObject)
                            return;
                    }
                    break;

                case 0x23: // ThemeResource object
                    {
                        object propertyValue = GetPropertyValue(reader);
                        var obj = new XbfObject() { TypeName = "ThemeResource" };
                        obj.Properties.Add(new XbfObjectProperty("ResourceKey", propertyValue));
                        _objectStack.Push(obj);

                        EmitDisassembly(reader, commandPosition, $"themeresourceobj {propertyValue}");

                        if (readSingleObject)
                            return;
                    }
                    break;

                case 0x13: // Object collection begin
                    {
                        string propertyName = GetPropertyName(reader.ReadUInt16());
                        var collection = new XbfObjectCollection(_objectStack.Peek(), propertyName);
                        _objectStack.Peek().Properties.Add(new XbfObjectProperty(propertyName, collection));
                        _objectCollectionStack.Push(collection);

                        EmitDisassembly(reader, commandPosition, $"collectionbegin {propertyName}");
                        _disassembly?.Indent();
                    }
                    break;

                case 0x02: // End of collection
                    // The collection has already been added as a property, so we just need to pop it off the stack
                    _objectCollectionStack.Pop();

                    _disassembly?.Unindent();
                    EmitDisassembly(reader, commandPosition, $"collectionend");
                    break;

                case 0x14: // Object begin
                    {
                        // We are starting a new object inside of the current object. It will be applied as a property of the current object.
                        var subObj = new XbfObject();
                        subObj.TypeName = GetTypeName(reader.ReadUInt16());
                        _objectStack.Push(subObj);

                        _objectCollectionStack.Push(subObj.Children);

                        if (readSingleObject && singleObject == null)
                            singleObject = subObj;

                        EmitDisassembly(reader, commandPosition, $"objbegin {subObj.TypeName}");
                        _disassembly?.Indent();
                    }
                    break;

                case 0x21: // Object end
                    // Pop Children collection of ending object off the object collection stack if it had been pushed there earlier
                    if (_objectCollectionStack.Count > 0 && _objectCollectionStack.Peek() == _objectStack.Peek().Children)
                        _objectCollectionStack.Pop();

                    _disassembly?.Unindent();
                    EmitDisassembly(reader, commandPosition, $"objend");

                    // Return if we are supposed to read only a single object and we just reached the end of it
                    if (readSingleObject && _objectStack.Peek() == singleObject)
                        return;

                    // Return if we reached the end of a nested root object
                    if (_objectStack.Peek() == _rootObjectStack.Peek())
                        return;
                    break;

                case 0x07: // Add the new object as a property of the current object
                case 0x20: // Same as 0x07, but this occurs when the object is a Binding, TemplateBinding, CustomResource, RelativeSource or NullExtension value
                    {
                        string propertyName = GetPropertyName(reader.ReadUInt16());
                        var subObj = _objectStack.Pop();
                        _objectStack.Peek().Properties.Add(new XbfObjectProperty(propertyName, subObj));

                        EmitDisassembly(reader, commandPosition, $"setproperty {propertyName}");
                    }
                    break;

                case 0x08: // Add the object to the list (simple)
                case 0x09: // 0x09 seems to be used instead of 0x08 for Styles that don't have a Key
                    {
                        var obj = _objectStack.Pop();
                        _objectCollectionStack.Peek().Add(obj);

                        EmitDisassembly(reader, commandPosition, $"addobj");
                    }
                    break;

                case 0x0A: // Add the object to the list with a key
                    {
                        var obj = _objectStack.Pop();
                        // Note: technically the key is a property of the collection rather than the object itself, but for simplicity (and display purposes) we're just adding it to the object.
                        obj.Key = GetPropertyValue(reader).ToString();
                        _objectCollectionStack.Peek().Add(obj);

                        EmitDisassembly(reader, commandPosition, $"keyaddobj {obj.Key}");
                    }
                    break;

                case 0x0B: // x:Class property
                    {
                        string className = ReadString(reader);
                        _objectStack.Peek().Properties.Add(new XbfObjectProperty("x:Class", className));

                        EmitDisassembly(reader, commandPosition, $"class {className}");
                    }
                    break;

                case 0x15: // Literal value (x:Int32, x:String, etc. and types in Windows.UI.Xaml namespace)
                case 0x16: // Literal value of type that is not in Windows.UI.Xaml namespace
                    {
                        XbfObject obj = new XbfObject();
                        obj.TypeName = GetTypeName(reader.ReadUInt16());
                        object value = GetPropertyValue(reader);
                        obj.Properties.Add(new XbfObjectProperty("Value", value)); // TODO: This isn't really correct since the value for these types just appears in the object body
                        _objectStack.Push(obj);

                        if (readSingleObject && singleObject == null)
                            singleObject = obj;

                        EmitDisassembly(reader, commandPosition, $"literal {obj.TypeName} {value}");
                        _disassembly?.Indent();
                    }
                    break;

                case 0x0F: // Reference to a different node section
                    ReadNodeSectionReference(reader, commandPosition);
                    break;

                case 0x12: // Root objects can be nested
                case 0x17: 
                    // Rewind and handle the object in ReadRoot
                    reader.BaseStream.Seek(-1, SeekOrigin.Current);
                    ReadRoot(reader, endPosition);

                    if (readSingleObject && singleObject == null)
                        return; // The root object we just read is the single object we are supposed to read
                    break;

                case 0x18: // Looks to be equivalent to 0x17 but with an additional constructor argument
                case 0x19:
                    {
                        string typeName = GetTypeName(reader.ReadUInt16());
                        object argument = GetPropertyValue(reader);
                        // I am not aware of any way to specify constructor arguments in UWP XAML but XAML 2009 had an x:Arguments attribute for this, so let's use that instead
                        _objectStack.Peek().Properties.Add(new XbfObjectProperty("x:Class", typeName));
                        _objectStack.Peek().Properties.Add(new XbfObjectProperty("x:Arguments", argument));

                        EmitDisassembly(reader, commandPosition, $"createtype {typeName} {argument}");
                    }
                    break;

                case 0x26: // Begin Conditional XAML block
                    {
                        string typeName = GetTypeName(reader.ReadUInt16());
                        string argument = StringTable[reader.ReadUInt16()];

                        EmitDisassembly(reader, commandPosition, $"condbegin {typeName}({argument})");
                        _disassembly?.Indent();
                    }
                    break;

                case 0x27: // End Conditional XAML block
                    {
                        _disassembly?.Unindent();
                        EmitDisassembly(reader, commandPosition, $"condend");
                    }
                    break;

                case 0x28: // Object end and immediate return
                    {
                        // Pop Children collection of ending object off the object collection stack if it had been pushed there earlier
                        if (_objectCollectionStack.Count > 0 && _objectCollectionStack.Peek() == _objectStack.Peek().Children)
                            _objectCollectionStack.Pop();

                        _disassembly?.Unindent();
                        EmitDisassembly(reader, commandPosition, $"objend");
                        return;
                    }

                case 0x8B: // Unknown purpose, only encountered in one file
                    {
                        XbfObject subObj = _objectStack.Pop();

                        EmitDisassembly(reader, commandPosition, $"unk");
                    }
                    break;

                default:
                    throw new InvalidDataException(string.Format("Unrecognized character 0x{0:X2} while parsing object", nodeType));
            }

            if (readSingleNode)
                break;
        }
    }

    private void ReadNodeSectionReference(BinaryReaderEx reader, long commandPosition)
    {
        // The node section we're skipping to
        XbfNodeSection nodeSection = NodeSectionTable![reader.Read7BitEncodedInt()];

        if (reader.ReadUInt16() != 0) // TODO: unknown purpose
            throw new InvalidDataException($"Unexpected value");

        // Get the type of nodes contained in this section
        int type = reader.Read7BitEncodedInt();

        EmitDisassembly(reader, commandPosition, $"refsection {nodeSection} type:{type}");
        _disassembly?.Indent();

        switch (type)
        {
            case 2: // Style
            case 8: // 8 seems to be equivalent
                ReadStyle(reader, nodeSection, false);
                break;

            case 11: // Style
                ReadStyle(reader, nodeSection, true);
                break;

            case 7: // ResourceDictionary
                ReadResourceDictionary(reader, nodeSection, false, false);
                break;

            case 371: // ResourceDictionary
                ReadResourceDictionary(reader, nodeSection, true, false);
                break;

            case 10: // ResourceDictionary
                ReadResourceDictionary(reader, nodeSection, true, true);
                break;

            case 5: // Visual states
                SkipVisualStateBytes(reader, nodeSection);
                ReadNodeSection(reader, nodeSection);
                break;

            case 6: // DeferredElement
                ReadDeferredElement(reader, nodeSection, true, false);
                break;

            case 746: // DeferredElement
                ReadDeferredElement(reader, nodeSection, false, false);
                break;

            case 9: // DeferredElement                    
                ReadDeferredElement(reader, nodeSection, true, true);
                break;

            default:
                throw new InvalidDataException(string.Format("Unknown node type {0} while parsing referenced code section", type));
        }

        _disassembly?.Unindent();
        EmitDisassembly(reader, commandPosition, $"refsectionend {nodeSection} type:{type}");
    }

    private void ReadDataTemplate(BinaryReaderEx reader, long commandPosition)
    {
        string propertyName = GetPropertyName(reader.ReadUInt16()); // Always "Template"

        XbfNodeSection nodeSection = NodeSectionTable![reader.Read7BitEncodedInt()];

        // List of StaticResources and ThemeResources referenced from inside the DataTemplate
        int staticResourceCount = reader.Read7BitEncodedInt();
        int themeResourceCount = reader.Read7BitEncodedInt();
        for (int i = 0; i < staticResourceCount; i++)
        {
            string staticResource = StringTable[reader.ReadUInt16()];
        }
        for (int i = 0; i < themeResourceCount; i++)
        {
            string themeResource = StringTable[reader.ReadUInt16()];
        }

        EmitDisassembly(reader, commandPosition, $"datatemplate {propertyName} {nodeSection}");
        _disassembly?.Indent();

        ReadNodeSection(reader, nodeSection);

        _disassembly?.Unindent();
        EmitDisassembly(reader, commandPosition, $"datatemplateend {propertyName} {nodeSection}");

        // Add the object we found as a property
        var obj = _objectStack.Pop();
        _objectStack.Peek().Properties.Add(new XbfObjectProperty(propertyName, obj));
    }

    private void ReadStyle(BinaryReaderEx reader, XbfNodeSection nodeSection, bool extended)
    {
        int setterCount = reader.Read7BitEncodedInt();
        for (int i = 0; i < setterCount; i++)
        {
            int valueType = reader.ReadByte();

            string? propertyName = null;
            string typeName; // Name of type implementing the property, currently ignored
            object? propertyValue = null;
            int valueOffset = 0;

            switch (valueType)
            {
                case 0x01: // ThemeResource
                case 0x02: // StaticResource
                case 0x08: // General objects
                    propertyName = StringTable[reader.ReadUInt16()];
                    typeName = GetTypeName(reader.ReadUInt16());
                    valueOffset = reader.Read7BitEncodedInt();
                    break;
                case 0x11: // ThemeResource
                case 0x12: // StaticResource
                case 0x18: // General objects
                    propertyName = GetPropertyName(reader.ReadUInt16());
                    valueOffset = reader.Read7BitEncodedInt();
                    break;
                case 0x20:
                    propertyName = StringTable[reader.ReadUInt16()];
                    typeName = GetTypeName(reader.ReadUInt16());
                    propertyValue = GetPropertyValue(reader);
                    break;
                case 0x30:
                    propertyName = GetPropertyName(reader.ReadUInt16());
                    propertyValue = GetPropertyValue(reader);
                    break;
                case 0x40: // general objects, setter is encoded as normal object
                case 0x50:
                    if (!extended)
                        propertyName = GetPropertyName(reader.ReadUInt16());
                    valueOffset = reader.Read7BitEncodedInt();
                    break;
                case 0xC0: // general objects, setter is encoded as normal object
                case 0xD0:
                    if (reader.Read7BitEncodedInt() != 1) // TODO: purpose unknown
                        throw new InvalidDataException("Unexpected value");
                    if (!extended)
                        propertyName = GetPropertyName(reader.ReadUInt16());
                    valueOffset = reader.Read7BitEncodedInt();
                    break;
                default:
                    throw new InvalidDataException($"Unknown property value type {valueType}");
            }

            switch (valueType)
            {
                case 0x01:
                case 0x02:
                case 0x11:
                case 0x12:
                    {
                        // StaticResource or ThemeResource need to be read with the Setter already on the stack
                        XbfObject setter = new XbfObject();
                        setter.TypeName = "Setter";
                        setter.Properties.Add(new XbfObjectProperty("Property", propertyName!));
                        _objectCollectionStack.Peek().Add(setter);

                        _objectStack.Push(setter);
                        ReadNodeInNodeSection(reader, nodeSection, valueOffset);
                        _objectStack.Pop();
                        break;
                    }
                case 0x08:
                case 0x18:
                    {
                        // General objects can be read directly with ReadObjectInNodeSection
                        propertyValue = ReadObjectInNodeSection(reader, nodeSection, valueOffset);

                        XbfObject setter = new XbfObject();
                        setter.TypeName = "Setter";
                        setter.Properties.Add(new XbfObjectProperty("Property", propertyName!));
                        setter.Properties.Add(new XbfObjectProperty("Value", propertyValue));
                        _objectCollectionStack.Peek().Add(setter);
                        break;
                    }
                case 0x20:
                case 0x30:
                    {
                        XbfObject setter = new XbfObject();
                        setter.TypeName = "Setter";
                        setter.Properties.Add(new XbfObjectProperty("Property", propertyName!));
                        setter.Properties.Add(new XbfObjectProperty("Value", propertyValue!));
                        _objectCollectionStack.Peek().Add(setter);
                        break;
                    }
                case 0x40:
                case 0x50:
                case 0xC0:
                case 0xD0:
                    {
                        // General objects can be read directly with ReadObjectInNodeSection
                        XbfObject setter = ReadObjectInNodeSection(reader, nodeSection, valueOffset);
                        _objectCollectionStack.Peek().Add(setter);
                        break;
                    }
            }
        }

        if (extended)
            if (reader.Read7BitEncodedInt() != 0) // TODO: purpose unknown
                throw new InvalidDataException("Unexpected value");
    }

    private void ReadDeferredElement(BinaryReaderEx reader, XbfNodeSection nodeSection, bool extended, bool extended2)
    {
        string deferredElementName = StringTable[reader.ReadUInt16()];

        if (extended)
        {
            // The following properties can be ignored as they will appear in the secondary node section again
            int count = reader.Read7BitEncodedInt();
            for (int i = 0; i < count; i++)
            {
                string propertyName = GetPropertyName(reader.ReadUInt16());
                object propertyValue = GetPropertyValue(reader);
            }
        }

        ReadNodeSection(reader, nodeSection);
        XbfObject childObj = _objectStack.Pop();
        XbfObject deferredElement = _objectStack.Peek();
        deferredElement.Children.Add(childObj);

        if (extended2)
            reader.Read7BitEncodedInt(); // TODO: purpose unknown          
    }

    private void ReadNodeSection(BinaryReaderEx reader, XbfNodeSection nodeSection)
    {
        // Save the current position and skip ahead to the new position
        long originalPosition = reader.BaseStream.Position;
        int newPosition = _firstNodeSectionPosition + nodeSection.NodeOffset;
        int newEndPosition = _firstNodeSectionPosition + nodeSection.PositionalOffset;

        reader.BaseStream.Position = newPosition;

        // Read the nodes from the specified position
        ReadNodes(reader, newEndPosition);

        // Return to the original position
        reader.BaseStream.Position = originalPosition;
    }

    private XbfObject ReadObjectInNodeSection(BinaryReaderEx reader, XbfNodeSection nodeSection, int offset)
    {
        // Save the current position and skip ahead to the new position
        long originalPosition = reader.BaseStream.Position;
        int newPosition = _firstNodeSectionPosition + nodeSection.NodeOffset;
        int newEndPosition = _firstNodeSectionPosition + nodeSection.PositionalOffset;

        reader.BaseStream.Position = newPosition + offset;

        int objectStackDepthBefore = _objectStack.Count;
        int objectCollectionStackDepthBefore = _objectCollectionStack.Count;

        // Read the node from the specified position
        ReadNodes(reader, int.MaxValue, true);

        XbfObject obj = _objectStack.Pop();

        if (_objectStack.Count != objectStackDepthBefore)
            throw new InvalidDataException("_objectStack corrupted");
        if (_objectCollectionStack.Count != objectCollectionStackDepthBefore)
            throw new InvalidDataException("_objectCollectionStack corrupted");

        // Return to the original position
        reader.BaseStream.Position = originalPosition;

        return obj;
    }

    private void ReadNodeInNodeSection(BinaryReaderEx reader, XbfNodeSection nodeSection, int offset)
    {
        // Save the current position and skip ahead to the new position
        long originalPosition = reader.BaseStream.Position;
        int newPosition = _firstNodeSectionPosition + nodeSection.NodeOffset;
        int newEndPosition = _firstNodeSectionPosition + nodeSection.PositionalOffset;

        reader.BaseStream.Position = newPosition + offset;

        int objectStackDepthBefore = _objectStack.Count;
        int objectCollectionStackDepthBefore = _objectCollectionStack.Count;

        // Read the node from the specified position
        ReadNodes(reader, int.MaxValue, false, true);

        if (_objectStack.Count != objectStackDepthBefore)
            throw new InvalidDataException("_objectStack corrupted");
        if (_objectCollectionStack.Count != objectCollectionStackDepthBefore)
            throw new InvalidDataException("_objectCollectionStack corrupted");

        // Return to the original position
        reader.BaseStream.Position = originalPosition;
    }

    private void ReadResourceDictionary(BinaryReaderEx reader, XbfNodeSection nodeSection, bool extended, bool extended2)
    {
        // Resources with keys
        int resourcesCount = reader.Read7BitEncodedInt();
        for (int i = 0; i < resourcesCount; i++)
        {
            string resourceKey = StringTable[reader.ReadUInt16()]; 
            int position = reader.Read7BitEncodedInt(); // Secondary node stream offset

            XbfObject obj = ReadObjectInNodeSection(reader, nodeSection, position);
            obj.Key = resourceKey;
            _objectCollectionStack.Peek().Add(obj);
        }

        // A subset of the resource keys from above seem to get repeated here, purpose unknown
        int count = reader.Read7BitEncodedInt();
        for (int i = 0; i < count; i++)
        {
            string resourceKey = StringTable[reader.ReadUInt16()];
        }

        // Styles with TargetType and no key
        int styleCount = reader.Read7BitEncodedInt();
        for (int i = 0; i < styleCount; i++)
        {
            string targetType = StringTable[reader.ReadUInt16()]; 
            int position = reader.Read7BitEncodedInt();  // Secondary node stream offset

            XbfObject obj = ReadObjectInNodeSection(reader, nodeSection, position);
            _objectCollectionStack.Peek().Add(obj);
        }

        if (!extended2)
        {
            if (extended)
                if (reader.Read7BitEncodedInt() != 0) // TODO: purpose unknown
                    throw new InvalidDataException("Unexpected value");

            // A subset of the target types from above seem to get repeated here, purpose unknown
            int count2 = reader.Read7BitEncodedInt();
            for (int i = 0; i < count2; i++)
            {
                string targetType = StringTable[reader.ReadUInt16()];
            }
        }
        else
        {
            // TODO: unknown purpose, commented-out code appears to work for most but not all files
            int count1 = reader.Read7BitEncodedInt();
            if (count1 != 0)
                throw new InvalidDataException("Unsupported value");
            /*for (int i = 0; i < count1; i++)
            {
                var v1 = StringTable[reader.ReadUInt16()];
                var v2 = reader.Read7BitEncodedInt();
                var startOffset = reader.Read7BitEncodedInt();
                var endOffset = reader.Read7BitEncodedInt();
            }*/

            int count2 = reader.Read7BitEncodedInt();
            if (count2 != 0)
                throw new InvalidDataException("Unsupported value");
            /*for (int i = 0; i < count2; i++)
            {
                var v3 = reader.ReadUInt16();
                var v2 = reader.Read7BitEncodedInt();
                var startOffset = reader.Read7BitEncodedInt();
                var endOffset = reader.Read7BitEncodedInt();
            }*/

            int count3 = reader.Read7BitEncodedInt();
            if (count3 != 0)
                throw new InvalidDataException("Unsupported value");
            /*for (int i = 0; i < count3; i++)
            {
                var startOffset = reader.Read7BitEncodedInt();
                var v2 = reader.Read7BitEncodedInt();
                var v3 = GetTypeName(reader.ReadUInt16());
                var v4 = StringTable[reader.ReadUInt16()];
            }*/
        }
    }

    private void SkipVisualStateBytes(BinaryReaderEx reader, XbfNodeSection nodeSection)
    {
        // Number of visual states
        int visualStateCount = reader.Read7BitEncodedInt();
        // The following bytes indicate which visual states belong in each group
        int[] visualStateGroupMemberships = new int[visualStateCount];
        for (int i = 0; i < visualStateCount; i++)
            visualStateGroupMemberships[i] = reader.Read7BitEncodedInt();

        // Number of visual states (again?)
        int visualStateCount2 = reader.Read7BitEncodedInt();
        if (visualStateCount != visualStateCount2)
            throw new InvalidDataException("Visual state counts did not match"); // TODO: What does it mean when this happens? Will it ever happen?
        // Get the VisualState objects
        var visualStates = new XbfObject[visualStateCount2];
        for (int i = 0; i < visualStateCount2; i++)
        {
            int nameID = reader.ReadUInt16();

            reader.Read7BitEncodedInt(); // TODO: purpose unknown
            reader.Read7BitEncodedInt(); // TODO: purpose unknown

            // Get the Setters for this VisualState
            int setterCount = reader.Read7BitEncodedInt();
            for (int j = 0; j < setterCount; j++)
            {
                int setterOffset = reader.Read7BitEncodedInt();
            }

            // Get the AdaptiveTriggers for this VisualState
            int adaptiveTriggerCount = reader.Read7BitEncodedInt();
            for (int j = 0; j < adaptiveTriggerCount; j++)
            {
                // I'm not sure what this second count is for -- possibly for the number of properties set on the trigger
                int count = reader.Read7BitEncodedInt();
                for (int k = 0; k < count; k++)
                    reader.Read7BitEncodedInt(); // TODO: purpose unknown
            }

            // Get the StateTriggers for this VisualState
            int stateTriggerCount = reader.Read7BitEncodedInt();
            for (int j = 0; j < stateTriggerCount; j++)
            {
                int stateTriggerOffset = reader.Read7BitEncodedInt();
                //var obj = ReadObjectInNodeSection(reader, nodeSection, stateTriggerOffset); // these are also Adaptive Triggers!
            }

            int offsetCount = reader.Read7BitEncodedInt(); // Always 0 or 2
            for (int j = 0; j < offsetCount; j++)
            {
                var offset = reader.Read7BitEncodedInt(); // Secondary node stream offset of StateTriggers and Setters collection
            }

            if (reader.Read7BitEncodedInt() != 0) // TODO: purpose unknown
                throw new InvalidDataException("Unexpected value");

            var vs = new XbfObject();
            vs.TypeName = "VisualState";
            vs.Name = StringTable[nameID];

            visualStates[i] = vs;
        }

        // Number of VisualStateGroups
        int visualStateGroupCount = reader.Read7BitEncodedInt();

        // Get the VisualStateGroup objects
        var visualStateGroups = new XbfObject[visualStateGroupCount];
        for (int i = 0; i < visualStateGroupCount; i++)
        {
            int nameID = reader.ReadUInt16();

            reader.Read7BitEncodedInt(); // TODO, always 1 or 2

            // The offset within the node section for this VisualStateGroup
            int objectOffset = reader.Read7BitEncodedInt();

            var vsg = new XbfObject();
            vsg.TypeName = "VisualStateGroup";
            vsg.Name = StringTable[nameID];

            // Get the visual states that belong to this group
            var states = new List<XbfObject>();
            for (int j = 0; j < visualStateGroupMemberships.Length; j++)
            {
                if (visualStateGroupMemberships[j] == i)
                    states.Add(visualStates[j]);
            }
            if (states.Count > 0)
                vsg.Properties.Add(new XbfObjectProperty("States", states));

            visualStateGroups[i] = vsg;
        }

        int visualTransitionCount = reader.Read7BitEncodedInt();
        for (int i = 0; i < visualTransitionCount; i++)
        {
            string toState = StringTable[reader.ReadUInt16()]; 
            string fromState = StringTable[reader.ReadUInt16()]; 
            int visualTransitionOffset = reader.Read7BitEncodedInt();
        }

        reader.Read7BitEncodedInt(); // TODO: always 1 or 2

        int count2 = reader.Read7BitEncodedInt();
        for (int i = 0; i < count2; i++)
        {
            int visualStateIndex1 = reader.Read7BitEncodedInt(); // Visual state index or -1
            int visualStateIndex2 = reader.Read7BitEncodedInt(); // Visual state index or -1
            reader.Read7BitEncodedInt();
        }

        int count3 = reader.Read7BitEncodedInt(); // TODO: unknown purpose
        for (int i = 0; i < count3; i++)
            reader.Read7BitEncodedInt();

        reader.Read7BitEncodedInt(); // TODO: unknown purpose

        // At the end we have a list of string references
        int stringCount = reader.Read7BitEncodedInt();
        for (int i = 0; i < stringCount; i++)
        {
            string str = StringTable[reader.ReadUInt16()];
        }

        // At this point we have a list of VisualStateGroup objects in the visualStateGroups variable.
        // These could be added to the result, but we already have them there from parsing the specified node section.
    }

    private string GetTypeName(int id)
    {
        // If the highest bit is set, this is a standard framework type
        if ((id & 0x8000) != 0)
            return XbfFrameworkTypes.GetNameForTypeID(id & ~0x8000) ?? string.Format("UnknownType0x{0:X4}", id);

        var type = TypeTable[id];
        if (type.Namespace != null)
        {
            var namespaceName = "using:" + type.Namespace.Name;
            if (_namespacePrefixes.TryGetValue(namespaceName, out var prefix))
                return prefix + ":" + type.Name;
        }
        return type.Name;
    }

    private string GetPropertyName(int id)
    {
        // If the highest bit is set, this is a standard framework property
        if ((id & 0x8000) != 0)
            return XbfFrameworkTypes.GetNameForPropertyID(id & ~0x8000) ?? string.Format("UnknownProperty0x{0:X4}", id);

        return PropertyTable[id].Name;
    }

    private string GetEnumerationValue(int enumID, int enumValue)
    {
        return XbfFrameworkTypes.GetNameForEnumValue(enumID, enumValue) ?? string.Format("(Enum0x{0:X4}){1}", enumID, enumValue);
    }

    private object GetPropertyValue(BinaryReader reader)
    {
        byte valueType = reader.ReadByte();
        switch (valueType)
        {
            case 0x01: // bool value false
                return false;

            case 0x02: // bool value true
                return true;

            case 0x03: // float
                return reader.ReadSingle();

            case 0x04: // int
                return reader.ReadInt32();

            case 0x05: // string
                return StringTable[reader.ReadUInt16()];

            case 0x06: // Thickness
                float left = reader.ReadSingle();
                float top = reader.ReadSingle();
                float right = reader.ReadSingle();
                float bottom = reader.ReadSingle();

                // Attempt to combine values
                if (left == right && top == bottom)
                {
                    // If all values are equal, just return one value
                    if (left == top)
                        return left;

                    // If the left/right and top/bottom values are equal, return a simple "x,y" string
                    return string.Format("{0},{1}", left, top);
                }

                // Otherwise, just return all values as a string
                return string.Format("{0},{1},{2},{3}", left, top, right, bottom);

            case 0x07: // GridLength
                int gridLengthType = reader.ReadInt32();
                float gridLengthValue = reader.ReadSingle();
                switch (gridLengthType)
                {
                    case 0: return "Auto";
                    case 1: return gridLengthValue;
                    case 2: return (gridLengthValue == 1) ? "*" : gridLengthValue + "*";
                    default: throw new InvalidDataException("Unexpected value");
                }

            case 0x08: // Color (or Brush?)
                byte b = reader.ReadByte();
                byte g = reader.ReadByte();
                byte r = reader.ReadByte();
                byte a = reader.ReadByte();
                return string.Format("#{0:X2}{1:X2}{2:X2}{3:X2}", a, r, g, b);

            case 0x09: // Duration (as an in-line string)
                return ReadString(reader);

            case 0x0A: // Empty string
                return string.Empty;

            case 0x0B: // Enumeration
                int enumID = reader.ReadUInt16();
                int enumValue = reader.ReadInt32();
                return GetEnumerationValue(enumID, enumValue);

            default:
                throw new InvalidDataException(string.Format("Unrecognized value type 0x{0:X2}", valueType));
        }
    }

    private void EmitDisassembly(BinaryReader reader, long commandPosition, FormattableString command)
    {
        if (_disassembly == null)
            return;

        int nodeSection = Array.FindLastIndex(NodeSectionTable!, nso => nso.NodeOffset <= commandPosition - _firstNodeSectionPosition);

        _disassembly.AddCommand(reader, commandPosition, nodeSection, command.ToString(), _objectStack, _objectCollectionStack);
    }

    private bool disposedValue = false;

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                _reader.Dispose();
            }

            disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
    }
}
