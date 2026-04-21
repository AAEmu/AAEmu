using System.Numerics;
using System.Text;
using AAEmu.Game.IO;
using NLog;

namespace AAEmu.Game.Models.CryEngine.Objects;

public class ObjectsFile(string fileName)
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private const uint NodeHeaderSize = 33;

    public string FileName { get; init; } = fileName;
    public List<AssetPath> AssetPathsList { get; set; } = [];
    public List<ObjectDataBase> PrefabsList { get; set; } = [];

    public bool ReadFile()
    {
        AssetPathsList.Clear();
        PrefabsList.Clear();
        try
        {
            using var sourceFs = ClientFileManager.GetFileStream(FileName);
            using var fs = new MemoryStream();
            sourceFs.CopyTo(fs);
            if (fs.Length <= 8)
            {
                // File too small contain data
                return true;
            }
            fs.Seek(0, SeekOrigin.Begin);

            using var br = new BinaryReader(fs);
            
            /*
            // ImHex object.dat pattern file
            struct AssetPathStruct {
                u32 Unknown;
                char AssetPath[256];
            };

            struct PrefabPathStruct {
                u32 Unknown;
                char AssetPath[256];
            };

            struct Vector3 {
                float x;
                float y;
                float z;
            };

            u32 AssetPathCount @ 0x00;
            AssetPathStruct AssetPaths[AssetPathCount] @ 0x04;

            u32 prefabStart = 0x04 + (AssetPathCount * sizeof(AssetPathStruct));
            u32 PrefabCount @ prefabStart;
            PrefabPathStruct PrefabPaths[PrefabCount] @ prefabStart + 0x04;
            u32 prefabBlockStart = prefabStart + (PrefabCount * sizeof(PrefabPathStruct)) + 0x04;

            // Read PrefabNode
            u32 PrefabNodeStart = prefabBlockStart;
            u32 UnknownInt @ PrefabNodeStart;
            Vector3 StartPos @ PrefabNodeStart + 0x04;
            Vector3 EndPos @ PrefabNodeStart + 0x10;
            u32 ObjectDataBlockSize @ PrefabNodeStart + 0x1C;
            u8 ChildrenBitMask @ PrefabNodeStart + 0x20;
            */

            // Asset Paths
            var assetPathCount = br.ReadUInt32();
            for (var i = 0u; i < assetPathCount; i++)
            {
                var unknown = br.ReadUInt32();
                var chunkBytes = br.ReadBytes(256);
                var assetPath = new AssetPath
                {
                    Unknown = unknown,
                    Name = Encoding.UTF8.GetString(chunkBytes).Replace("\0", "").TrimEnd()
                };
                AssetPathsList.Add(assetPath);
            }

            // Prefabs
            var prefabCount = br.ReadUInt32();
            br.BaseStream.Seek(prefabCount * 260, SeekOrigin.Current);
            for (var i = 0u; i < prefabCount; i++)
            {
                if (br.BaseStream.Position >= br.BaseStream.Length)
                    return true;

                var (nextOffset, success) = ReadPrefabNode(br, (int)br.BaseStream.Position,">");
                if (!success)
                {
                    return false;
                }

                br.BaseStream.Position = nextOffset;
            }
            return true;
        }
        catch (Exception e)
        {
            Logger.Warn($"Error reading file {FileName}, exception: {e}");
        }
        return false;
    }

    private (long, bool) ReadPrefabNode(BinaryReader br, int readerStartOffset, string debugPrefix)
    {
        br.BaseStream.Position = readerStartOffset;
        if (br.BaseStream.Position + NodeHeaderSize > br.BaseStream.Length)
            return (br.BaseStream.Position, false);
        var unknownInt = br.ReadInt32();
        var startPos = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
        var endPos = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
        var objectDataBlockSize = br.ReadInt32();
        var childrenBitMask = br.ReadByte();
        var contentStartOffset = br.BaseStream.Position;
        var childStartOffset = contentStartOffset;

        // Logger.Debug($"{debugPrefix} Reading node at 0x{readerStartOffset:X}, Int: {unknownInt}, Pos: {startPos} -> {endPos}, Size: {objectDataBlockSize}, ChildBits: {childrenBitMask:b8}");

        if (objectDataBlockSize > 0)
        {
            if (contentStartOffset + objectDataBlockSize > br.BaseStream.Length)
                return (br.BaseStream.Length, false);
            var dataBlock = br.ReadBytes(objectDataBlockSize);
            if (!ParseObjectBlockData(br, dataBlock))
                return (br.BaseStream.Length, false);
            childStartOffset = br.BaseStream.Position;
        }

        var currentChildOffset = childStartOffset;
        if (childrenBitMask > 0)
        {
            for (int i = 0; i < 8; i++)
            {
                if ((childrenBitMask & (1 << i)) != 0)
                {
                    br.BaseStream.Position = currentChildOffset;
                    var (nextOffset, success) = ReadPrefabNode(br, (int)currentChildOffset, $"--{debugPrefix}" );
                    currentChildOffset = nextOffset;
                    if (!success)
                        return (br.BaseStream.Length, false);
                }
            }
        }

        // Logger.Debug($"{debugPrefix} Reading node at 0x{readerStartOffset:X} ended at 0x{br.BaseStream.Position:X}");
        return (currentChildOffset, true);
    }

    /// <summary>
    /// Returns a type specific class reader for used and known types or a generic reader otherwise
    /// </summary>
    /// <param name="objectType"></param>
    /// <returns></returns>
    private ObjectDataBase GetPrefabReader(ObjectDataType objectType)
    {
        switch (objectType)
        {
            case ObjectDataType.Brush: return new ObjectDataType1Brush();
            case ObjectDataType.Voxel: return new ObjectDataType6Voxel();
            case ObjectDataType.WaterVolume: return new ObjectDataType11Water();
            case ObjectDataType.Road: return new ObjectDataType13Road();
            default: return new ObjectDataBase(objectType);
        }
    }

    /// <summary>
    /// Parses block data into prefab objects that are added to PrefabsList
    /// </summary>
    /// <param name="br"></param>
    /// <param name="blockData"></param>
    /// <returns></returns>
    private bool ParseObjectBlockData(BinaryReader br, byte[] blockData)
    {
        // Logger.Debug($"Start parsing ObjectBlock @ 0x{br.BaseStream.Position:X}");
        var blockSize = blockData.Length;
        var offset = 0;
        while (offset < blockSize)
        {
            var startOfObjectOffset = offset;
            if (offset + 4 > blockSize)
                break;
            var objectType = (ObjectDataType)BitConverter.ToInt32(blockData, offset);
            var prefab = GetPrefabReader(objectType);
            prefab.Name = $"{objectType}-{PrefabsList.Count}@{FileName}";
            var totalObjectSize = prefab.ReadData(blockData, startOfObjectOffset);
            if (totalObjectSize > 0)
            {
                PrefabsList.Add(prefab);
            }
            else
            {
                Logger.Warn($"Unknown/unreadable type {objectType} @ 0x{br.BaseStream.Position:X}, DataOffset: 0x{startOfObjectOffset:X} in {FileName} — stopping block parse");
                break;
            }
            offset += totalObjectSize;
        }
        return true;
    }

    
}
