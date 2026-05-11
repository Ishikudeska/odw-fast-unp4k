using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace unforge
{
	public enum ByteOrderEnum
    {
        AutoDetect,
        BigEndian,
        LittleEndian,
    }

    public static class CryXmlSerializer
    {
        public static Int64 ReadInt64(this BinaryReader br, ByteOrderEnum byteOrder = ByteOrderEnum.BigEndian)
        {
			var value = br.ReadInt64();
			return byteOrder == ByteOrderEnum.LittleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
        }

        public static Int32 ReadInt32(this BinaryReader br, ByteOrderEnum byteOrder = ByteOrderEnum.BigEndian)
        {
			var value = br.ReadInt32();
			return byteOrder == ByteOrderEnum.LittleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
        }

        public static Int16 ReadInt16(this BinaryReader br, ByteOrderEnum byteOrder = ByteOrderEnum.BigEndian)
        {
			var value = br.ReadInt16();
			return byteOrder == ByteOrderEnum.LittleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
        }

        public static UInt64 ReadUInt64(this BinaryReader br, ByteOrderEnum byteOrder = ByteOrderEnum.BigEndian)
        {
			var value = br.ReadUInt64();
			return byteOrder == ByteOrderEnum.LittleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
        }

        public static UInt32 ReadUInt32(this BinaryReader br, ByteOrderEnum byteOrder = ByteOrderEnum.BigEndian)
        {
			var value = br.ReadUInt32();
			return byteOrder == ByteOrderEnum.LittleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
        }

        public static UInt16 ReadUInt16(this BinaryReader br, ByteOrderEnum byteOrder = ByteOrderEnum.BigEndian)
        {
			var value = br.ReadUInt16();
			return byteOrder == ByteOrderEnum.LittleEndian ? BinaryPrimitives.ReverseEndianness(value) : value;
        }

        public static XmlDocument ReadFile(String inFile, ByteOrderEnum byteOrder = ByteOrderEnum.AutoDetect, Boolean writeLog = false)
        {
            return CryXmlSerializer.ReadStream(File.OpenRead(inFile), byteOrder, writeLog);
        }

        public static XmlDocument ReadStream(Stream inStream, ByteOrderEnum byteOrder = ByteOrderEnum.AutoDetect, Boolean writeLog = false)
        {
            using (BinaryReader br = new BinaryReader(inStream))
            {
                var peek = br.PeekChar();

                if (peek == '<')
                {
                    return null; // File is already XML
                }
                else if (peek != 'C')
                {
                    throw new FormatException("Unknown File Format"); // Unknown file format
                }

                String header = br.ReadFString(7);

                if (header == "CryXml" || header == "CryXmlB")
                {
                    br.ReadCString();
                }
                else if (header == "CRY3SDK")
                {
                    var bytes = br.ReadBytes(2);
                }
                else
                {
                    throw new FormatException("Unknown File Format");
                }

                var headerLength = br.BaseStream.Position;

                byteOrder = ByteOrderEnum.BigEndian;

                var fileLength = br.ReadInt32(byteOrder);

                if (fileLength != br.BaseStream.Length)
                {
                    br.BaseStream.Seek(headerLength, SeekOrigin.Begin);
                    byteOrder = ByteOrderEnum.LittleEndian;
                    fileLength = br.ReadInt32(byteOrder);
                }

                var nodeTableOffset = br.ReadInt32(byteOrder);
                var nodeTableCount = br.ReadInt32(byteOrder);
                var nodeTableSize = 28;

                var attributeTableOffset = br.ReadInt32(byteOrder);
                var attributeTableCount = br.ReadInt32(byteOrder);
                var referenceTableSize = 8;

                var childTableOffset = br.ReadInt32(byteOrder);
                var childTableCount = br.ReadInt32(byteOrder);
                var length3 = 4;

                var stringTableOffset = br.ReadInt32(byteOrder);
                var stringTableCount = br.ReadInt32(byteOrder);

                if (writeLog)
                {
                    // Regex byteFormatter = new Regex("([0-9A-F]{8})");
                    Console.WriteLine("Header");
                    Console.WriteLine("0x{0:X6}: {1}", 0x00, header);
                    Console.WriteLine("0x{0:X6}: {1:X8} (Dec: {1:D8})", headerLength + 0x00, fileLength);
                    Console.WriteLine("0x{0:X6}: {1:X8} (Dec: {1:D8}) node offset", headerLength + 0x04, nodeTableOffset);
                    Console.WriteLine("0x{0:X6}: {1:X8} (Dec: {1:D8}) nodes", headerLength + 0x08, nodeTableCount);
                    Console.WriteLine("0x{0:X6}: {1:X8} (Dec: {1:D8}) reference offset", headerLength + 0x12, attributeTableOffset);
                    Console.WriteLine("0x{0:X6}: {1:X8} (Dec: {1:D8}) references", headerLength + 0x16, attributeTableCount);
                    Console.WriteLine("0x{0:X6}: {1:X8} (Dec: {1:D8}) child offset", headerLength + 0x20, childTableOffset);
                    Console.WriteLine("0x{0:X6}: {1:X8} (Dec: {1:D8}) child", headerLength + 0x24, childTableCount);
                    Console.WriteLine("0x{0:X6}: {1:X8} (Dec: {1:D8}) content offset", headerLength + 0x28, stringTableOffset);
                    Console.WriteLine("0x{0:X6}: {1:X8} (Dec: {1:D8}) content", headerLength + 0x32, stringTableCount);
                    Console.WriteLine("");
                    Console.WriteLine("Node Table");
                }

                List<CryXmlNode> nodeTable = new List<CryXmlNode>(nodeTableCount);
                br.BaseStream.Seek(nodeTableOffset, SeekOrigin.Begin);
                Int32 nodeID = 0;
                while (br.BaseStream.Position < nodeTableOffset + nodeTableCount * nodeTableSize)
                {
                    var position = br.BaseStream.Position;
                    var value = new CryXmlNode
                    {
                        NodeID = nodeID++,
                        NodeNameOffset = br.ReadInt32(byteOrder),
                        ContentOffset = br.ReadInt32(byteOrder),
                        AttributeCount = br.ReadInt16(byteOrder),
                        ChildCount = br.ReadInt16(byteOrder),
                        ParentNodeID = br.ReadInt32(byteOrder),
                        FirstAttributeIndex = br.ReadInt32(byteOrder),
                        FirstChildIndex = br.ReadInt32(byteOrder),
                        Reserved = br.ReadInt32(byteOrder),
                    };

                    nodeTable.Add(value);
                    if (writeLog)
                    {
                        Console.WriteLine(
                            "0x{0:X6}: {1:X8} {2:X8} attr:{3:X4} {4:X4} {5:X8} {6:X8} {7:X8} {8:X8}",
                            position,
                            value.NodeNameOffset,
                            value.ContentOffset,
                            value.AttributeCount,
                            value.ChildCount,
                            value.ParentNodeID,
                            value.FirstAttributeIndex,
                            value.FirstChildIndex,
                            value.Reserved);
                    }
                }

                if (writeLog)
                {
                    Console.WriteLine("");
                    Console.WriteLine("Reference Table");
                }

                List<CryXmlReference> attributeTable = new List<CryXmlReference>(attributeTableCount);
                br.BaseStream.Seek(attributeTableOffset, SeekOrigin.Begin);
                while (br.BaseStream.Position < attributeTableOffset + attributeTableCount * referenceTableSize)
                {
                    var position = br.BaseStream.Position;
                    var value = new CryXmlReference
                    {
                        NameOffset = br.ReadInt32(byteOrder),
                        ValueOffset = br.ReadInt32(byteOrder)
                    };

                    attributeTable.Add(value);
                    if (writeLog)
                    {
                        Console.WriteLine("0x{0:X6}: {1:X8} {2:X8}", position, value.NameOffset, value.ValueOffset);
                    }
                }
                if (writeLog)
                {
                    Console.WriteLine("");
                    Console.WriteLine("Order Table");
                }

                List<Int32> parentTable = new List<Int32>(childTableCount);
                br.BaseStream.Seek(childTableOffset, SeekOrigin.Begin);
                while (br.BaseStream.Position < childTableOffset + childTableCount * length3)
                {
                    var position = br.BaseStream.Position;
                    var value = br.ReadInt32(byteOrder);

                    parentTable.Add(value);
                    if (writeLog)
                    {
                        Console.WriteLine("0x{0:X6}: {1:X8}", position, value);
                    }
                }

                if (writeLog)
                {
                    Console.WriteLine("");
                    Console.WriteLine("Dynamic Dictionary");
                }

                br.BaseStream.Seek(stringTableOffset, SeekOrigin.Begin);
                var stringBytes = br.ReadBytes((Int32)(br.BaseStream.Length - br.BaseStream.Position));
                var dataMap = new Dictionary<Int32, String>(1024);
                var bytePos = 0;
                while (bytePos < stringBytes.Length)
                {
                    var end = Array.IndexOf(stringBytes, (Byte)0, bytePos);
                    if (end < 0) end = stringBytes.Length;
                    var strVal = end > bytePos ? Encoding.Latin1.GetString(stringBytes, bytePos, end - bytePos) : null;
                    dataMap[bytePos] = strVal;
                    if (writeLog)
                    {
                        Console.WriteLine("0x{0:X6}: {1:X8} {2}", stringTableOffset + bytePos, bytePos, strVal);
                    }
                    bytePos = end + 1;
                }

                var attributeIndex = 0;

                var xmlDoc = new XmlDocument();

                Dictionary<Int32, XmlElement> xmlMap = new Dictionary<Int32, XmlElement>(nodeTableCount);

                foreach (var node in nodeTable)
                {
                    XmlElement element = xmlDoc.CreateElement(dataMap[node.NodeNameOffset]);

                    for (Int32 i = 0, j = node.AttributeCount; i < j; i++)
                    {
                        var attr = attributeTable[attributeIndex++];
                        element.SetAttribute(dataMap[attr.NameOffset], dataMap.TryGetValue(attr.ValueOffset, out var attrVal) ? attrVal : "BUGGED");
                    }

                    xmlMap[node.NodeID] = element;

                    if (dataMap.TryGetValue(node.ContentOffset, out var content))
                    {
                        if (!String.IsNullOrWhiteSpace(content))
                            element.AppendChild(xmlDoc.CreateCDataSection(content));
                    }
                    else
                    {
                        element.AppendChild(xmlDoc.CreateCDataSection("BUGGED"));
                    }

                    if (xmlMap.TryGetValue(node.ParentNodeID, out var parentElement))
                        parentElement.AppendChild(element);
                    else
                        xmlDoc.AppendChild(element);
                }
                
                return xmlDoc;
            }
        }

        public static TObject Deserialize<TObject>(String inFile, ByteOrderEnum byteOrder = ByteOrderEnum.BigEndian, Boolean writeLog = false) where TObject : class
        {
            using (MemoryStream ms = new MemoryStream())
            {
                var xmlDoc = CryXmlSerializer.ReadFile(inFile, byteOrder, writeLog);

                xmlDoc.Save(ms);

                ms.Seek(0, SeekOrigin.Begin);

                XmlSerializer xs = new XmlSerializer(typeof(TObject));

                return xs.Deserialize(ms) as TObject;
            }
        }
    }
}
