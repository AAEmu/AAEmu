using System.Numerics;

namespace AAEmu.Game.Models.CryEngine.Objects;

public class ObjectDataType11Water() : ObjectDataBase(ObjectDataType.WaterVolume)
{
    public override bool IsGeneric { get; protected init; } = false;

    private const int StartOfVariableData = 0x7B; // Variable points data starts at this offset

    /// <summary>
    /// Likely river type
    /// </summary>
    public WaterObjectVolumeType VolumeType { get; private set; }
    public ulong VolumeId { get; set; }
    public Vector3 StartPos { get; private set; } = Vector3.Zero;
    public Vector3 EndPos { get; private set; } = Vector3.Zero;
    private int ShapePointsCount { get; set; }
    private int PhysicsContourPointsCount { get; set; }
    /// <summary>
    /// This is a segment inside an existing water body
    /// </summary>
    public List<Vector3> ShapePointsList { get; private set; } = [];
    /// <summary>
    /// These points form the outer shape of the water body
    /// </summary>
    public List<Vector3> PhysicsContourPointsList { get; private set; } = [];
    public float Depth { get; private set; }
    public float Speed { get; private set; }
    public float SurfaceHeight { get; private set; }
    public float SurfVScale { get; set; }
    public float SurfUScale { get; set; }
    public float UTexEnd { get; set; }
    public float UTexBegin { get; set; }
    public float FogPlaneD { get; set; }
    public Vector3 FogPlanePos { get; set; }
    public float FogColorB { get; set; }
    public float FogColorG { get; set; }
    public float FogColorR { get; set; }
    public float FogDensity { get; set; }
    public int MaterialId { get; set; }
    
    /// <summary>
    /// Read the water data from a byte array starting at offset
    /// </summary>
    /// <param name="blockData"></param>
    /// <param name="offset"></param>
    /// <returns>Number of bytes used</returns>
    public override int ReadData(byte[] blockData, int offset)
    {
        var objectType = (ObjectDataType)BitConverter.ToInt32(blockData, offset + 0x00);
        if (objectType != PrefabType || (offset + StartOfVariableData > blockData.Length))
        {
            // Type mismatch or not enough bytes, return as error
            Data = [];
            return 0;
        }

        StartPos = GetVector3(blockData, offset + 0x04); // 3 x float @ 0x04 
        EndPos =  GetVector3(blockData, offset + 0x10);  //

        VolumeType = (WaterObjectVolumeType)blockData.Skip(offset + 0x2B).FirstOrDefault();
        VolumeId = BitConverter.ToUInt64(blockData, offset + 0x2F);
        MaterialId = BitConverter.ToInt32(blockData, offset + 0x37);
        FogDensity = BitConverter.ToSingle(blockData, offset + 0x3B);
        FogColorR = BitConverter.ToSingle(blockData, offset + 0x3F);
        FogColorG = BitConverter.ToSingle(blockData, offset + 0x43);
        FogColorB = BitConverter.ToSingle(blockData, offset + 0x47);
        FogPlanePos = GetVector3(blockData, offset + 0x4B);
        FogPlaneD = BitConverter.ToSingle(blockData, offset + 0x57);
        UTexBegin = BitConverter.ToSingle(blockData, offset + 0x5B);
        UTexEnd = BitConverter.ToSingle(blockData, offset + 0x5F);
        SurfUScale = BitConverter.ToSingle(blockData, offset + 0x63);
        SurfVScale = BitConverter.ToSingle(blockData, offset + 0x67);
        ShapePointsCount = BitConverter.ToInt32(blockData, offset + 0x6B);
        Depth = BitConverter.ToSingle(blockData, offset + 0x6F); // Math.Abs(EndPos.Z - StartPos.Z);
        Speed = BitConverter.ToSingle(blockData, offset + 0x73);
        PhysicsContourPointsCount = BitConverter.ToInt32(blockData, offset + 0x77);

        SurfaceHeight = Math.Max(EndPos.Z, StartPos.Z);

        // TODO: Find out if there is a directional vector for water speed, or if uses the segment data for its direction

        var totalObjectSize = (ShapePointsCount * 12) + (PhysicsContourPointsCount * 12) + StartOfVariableData;
        if (offset + totalObjectSize > blockData.Length)
        {
            Data = blockData.Skip(offset).ToArray();
            return Data.Length;
        }

        // Read points for inside data
        ShapePointsList = [];
        PhysicsContourPointsList = [];
        for (var i = 0; i < ShapePointsCount; i++)
            ShapePointsList.Add(GetVector3(blockData, offset + StartOfVariableData + (i * 12)));

        // Read border data
        var entryStart2 = StartOfVariableData + (ShapePointsCount * 12);
        for (var i = 0; i < PhysicsContourPointsCount; i++)
            PhysicsContourPointsList.Add(GetVector3(blockData, offset + entryStart2 + (i * 12)));

        // Zero-point volumes (e.g. bare Ocean marker): still consume the fixed header so ParseObjectBlockData advances.
        if (ShapePointsCount <= 0 && PhysicsContourPointsCount <= 0)
        {
            Data = blockData.Skip(offset).Take(StartOfVariableData).ToArray();
            return StartOfVariableData;
        }

        Data = blockData.Skip(offset).Take(totalObjectSize).ToArray();
        return Data.Length;
    }

}
