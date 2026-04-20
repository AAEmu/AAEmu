using System.Numerics;

namespace AAEmu.Game.Models.CryEngine.Objects;

public class ObjectDataType13Road() : ObjectDataBase(ObjectDataType.Road)
{
    public override bool IsGeneric { get; protected init; } = false;
    private int ArrayCount { get; set; }
    public List<Vector3> PointsList { get; private set; } = [];

    public override int ReadData(byte[] blockData, int offset)
    {
        var objectType = (ObjectDataType)BitConverter.ToInt32(blockData, offset + 0x00);
        if (objectType != PrefabType || (offset + 67 > blockData.Length))
        {
            // Type mismatch or not enough bytes, return as error
            Data = [];
            return 0;
        }

        ArrayCount = blockData[offset + 43];
        var totalObjectSize = (ArrayCount * 12) + 67;
        if (offset + totalObjectSize > blockData.Length)
        {
            Data = blockData.Skip(offset).ToArray();
            return Data.Length;
        }

        PointsList = [];
        for (var i = 0; i < ArrayCount; i++)
        {
            PointsList.Add(GetVector3(blockData, offset + 67 + (i * 12)));
        }

        if (ArrayCount <= 0)
        {
            Data = blockData.Skip(offset).Take(67).ToArray();
            return 67;
        }

        Data = blockData.Skip(offset).Take(totalObjectSize).ToArray();
        return Data.Length;
    }
}
