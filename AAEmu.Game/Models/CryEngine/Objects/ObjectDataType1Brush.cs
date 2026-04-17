using System.Numerics;
using CgfConverter.Structs;

namespace AAEmu.Game.Models.CryEngine.Objects;

public class ObjectDataType1Brush() : ObjectDataBase(ObjectDataType.Brush)
{
    public Vector3 StartPos { get; set; } = Vector3.Zero;
    public Vector3 EndPos { get; set; } = Vector3.Zero;
    public int MaterialId { get; set; }
    public int PathId { get; set; }
    public Matrix3x4 Matrix3X4 { get; set; }

    public override int ReadData(byte[] blockData, int offset)
    {
        var totalObjectSize = 0x84; // 132
        StartPos = GetVector3(blockData, offset + 0x04);
        EndPos = GetVector3(blockData, offset + 0x10);
        Matrix3X4 = GetMatrix3X4(blockData, offset + 0x47);
        MaterialId = BitConverter.ToInt32(blockData, offset + 0x77);
        PathId = BitConverter.ToInt32(blockData, offset + 0x7F);
        Data = blockData.Skip(offset).Take(totalObjectSize).ToArray();
        return Data.Length;
    }
}
