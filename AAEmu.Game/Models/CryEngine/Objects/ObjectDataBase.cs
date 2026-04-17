using System.Numerics;
using CgfConverter.Structs;

namespace AAEmu.Game.Models.CryEngine.Objects;

public class ObjectDataBase(ObjectDataType prefabType)
{
    private static Dictionary<ObjectDataType, int> ObjectTotalSizesByType { get; } = new()
    {
        {ObjectDataType.Brush, 132},  // 1, Brushes
        {ObjectDataType.Vegetation, 68},   // 2, Vegetation
        {(ObjectDataType)4, 100},  // 
        {(ObjectDataType)5, 124},  //
                   // Voxel (type 6) is variable size
                   // 7?
        {(ObjectDataType)8, 196},  //
        {ObjectDataType.Decal, 115},  // Decal
                   // 10?
                   // Water (type 11) is variable size
                   // 12?
                   // Roads (type 13) is variable size
        {ObjectDataType.AutoCubeMap, 83},  // Distance Clouds
                   // 15 ... 26?
        {ObjectDataType.Type27, 652}, // 
    };

    public string Name { get; set; }
    public ObjectDataType PrefabType { get; init; } = prefabType;
    public virtual bool IsGeneric { get; protected init; } = true;
    public byte[] Data { get; set; } = [];

    /// <summary>
    /// Read the data from a byte array starting at offset
    /// </summary>
    /// <param name="blockData"></param>
    /// <param name="offset"></param>
    /// <returns>Number of bytes used</returns>
    public virtual int ReadData(byte[] blockData, int offset)
    {
        var bytesToRead = ObjectTotalSizesByType.GetValueOrDefault(PrefabType);
        if (bytesToRead + offset > blockData.Length)
        {
            Data = [];
            return 0;
        }
        Data = blockData.Skip(offset).Take(bytesToRead).ToArray();
        return Data.Length;
    }
    
    protected Vector3 GetVector3(byte[] blockData, int offset)
    {
        return new Vector3(
            BitConverter.ToSingle(blockData, offset + 0),
            BitConverter.ToSingle(blockData, offset + 4),
            BitConverter.ToSingle(blockData, offset + 8)
        );
    }

    public static Matrix3x4 GetMatrix3X4(byte[] blockData, int offset)
    {
        var res = new Matrix3x4();
        res.M11 = BitConverter.ToSingle(blockData, offset + 0);
        res.M12 = BitConverter.ToSingle(blockData, offset + 4);
        res.M13 = BitConverter.ToSingle(blockData, offset + 8);
        res.M14 = BitConverter.ToSingle(blockData, offset + 12);

        res.M21 = BitConverter.ToSingle(blockData, offset + 16);
        res.M22 = BitConverter.ToSingle(blockData, offset + 20);
        res.M23 = BitConverter.ToSingle(blockData, offset + 24);
        res.M24 = BitConverter.ToSingle(blockData, offset + 28);

        res.M31 = BitConverter.ToSingle(blockData, offset + 32);
        res.M32 = BitConverter.ToSingle(blockData, offset + 36);
        res.M33 = BitConverter.ToSingle(blockData, offset + 40);
        res.M34 = BitConverter.ToSingle(blockData, offset + 44);
        return res;
    }
}
