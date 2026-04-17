using System.Diagnostics;
using System.Numerics;
using System.Text;
using AAEmu.Game.IO;
using NLog;

namespace AAEmu.Game.Models.CryEngine.Objects;

public class VisAreasFile(string fileName)
{
    private static Logger Logger = LogManager.GetCurrentClassLogger();
    private const uint NodeHeaderSize = 33;

    

    public string FileName { get; init; } = fileName;
    public List<AssetPath> AssetPathsList { get; set; } = [];
    public List<ObjectDataBase> PrefabsList { get; set; } = [];

    /// <summary>
    /// Top-level VisAreas
    /// </summary>
    public List<ObjectVisArea> VisAreas { get; set; } = [];

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

            var unknownInt = br.ReadInt32();
            var remainderDataSize = br.ReadInt32(); // this includes the size of these 5 ints
            var visAreasCount = br.ReadInt32();
            var portalCount = br.ReadInt32();
            var occluderCount = br.ReadInt32();

            var realRemainingSize = remainderDataSize - 20;

            ReadVisAreas(br, visAreasCount, portalCount, occluderCount);

            return true;
        }
        catch (Exception e)
        {
            Logger.Warn($"Error reading file {FileName}, exception: {e}");
        }
        return false;
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

     private void ReadVisAreas(BinaryReader br, int visAreasCount, int portalCount, int occluderCount)
    {
        while(br.BaseStream.Position + 4 < br.BaseStream.Length)
        {
            var startPosition = br.BaseStream.Position;
            var objectType = br.ReadUInt32();
            switch(objectType)
            {
                case 2:
                    br.BaseStream.Seek(-4, SeekOrigin.Current);
                    var (nextOffset, success) = TraverseVisAreaOctree(br);
                    if (!success)
                    {
                        // Error traversing octree
                        return;
                    }
                    br.BaseStream.Position = nextOffset;
                    break;
                case 3:
                case 4:
                    var visArea = new ObjectVisArea();
                    visArea.ObjectType = objectType;
                    visArea.StartPos = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                    visArea.EndPos = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                    br.BaseStream.Seek(276 - 4 - 24, SeekOrigin.Current);
                    visArea.VectorCount = br.ReadUInt32();
                    var totalObjectSize = 276 + (visArea.VectorCount * 12) + 4;
                    br.BaseStream.Position = startPosition;
                    visArea.VisData = br.ReadBytes((int)totalObjectSize);
                    VisAreas.Add(visArea);
                    break;
                default:
                    // Unknown type
                    return;
            }
        }
    }

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
            var totalObjectSize = prefab.ReadData(blockData, startOfObjectOffset);
            if (totalObjectSize > 0)
            {
                PrefabsList.Add(prefab);
            }
            else
            {
                Logger.Debug($"Error reading type {objectType} @ 0x{br.BaseStream.Position:X}, DataOffset: 0x{startOfObjectOffset:X} in {FileName}");
                return false;
            }
            offset += totalObjectSize;
        }
        return true;
    }

    private (long nextOffset, bool success) TraverseVisAreaOctree(BinaryReader br)
    {
        var offset = (int)br.BaseStream.Position;
        if (offset + NodeHeaderSize > br.BaseStream.Length)
            return (offset, true);

        br.BaseStream.Seek(28, SeekOrigin.Current);
        var objectDataBlockSize = br.ReadInt32();
        var childrenBitMask = br.ReadByte();

        var contentStartOffset = br.BaseStream.Position;
        var childStartOffset = contentStartOffset;
        
        if (objectDataBlockSize > 0)
        {
            if (contentStartOffset + objectDataBlockSize > br.BaseStream.Length)
                return (br.BaseStream.Length, false);

            var objectBlock =  br.ReadBytes(objectDataBlockSize);

            if (!ParseObjectBlockData(br, objectBlock))
                return (br.BaseStream.Length, false);

            childStartOffset += objectBlock.Length;
        }

        if (childrenBitMask > 0)
        {
            var currentChildOffset = childStartOffset;
            for(var bit = 0 ; bit < 8; bit++)
            {
                var mask = (1 << bit);
                if ((childrenBitMask & mask) != 0)
                {
                    br.BaseStream.Position = currentChildOffset;
                    var (nextOffset, success) = TraverseVisAreaOctree(br);
                    currentChildOffset = nextOffset;
                    if (!success)
                        return (br.BaseStream.Length, false);
                }
            }
            return (currentChildOffset, true);
        }

        return (childStartOffset, true);
    }

    
}
