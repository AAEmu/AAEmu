using System.Numerics;

namespace AAEmu.Game.Models.CryEngine.Objects;

public class ObjectDataType11Water() : ObjectDataBase(11)
{
    public override bool IsGeneric { get; protected init; } = false;

    private const int StartOfVariableData = 0x7B; // Variable points data starts at this offset

    /// <summary>
    /// Likely river type
    /// </summary>
    public int Flags { get; private set; }
    public Vector3 StartPos { get; private set; } = Vector3.Zero;
    public Vector3 EndPos { get; private set; } = Vector3.Zero;
    private int SegmentPointsCount { get; set; }
    private int BorderPointsCount { get; set; }
    /// <summary>
    /// This is a segment inside an existing water body
    /// </summary>
    public List<Vector3> SegmentPointsList { get; private set; } = [];
    /// <summary>
    /// These points form the outer shape of the water body
    /// </summary>
    public List<Vector3> BorderPointsList { get; private set; } = [];
    public float Depth { get; private set; }
    public float Speed { get; private set; }
    
    public float SurfaceHeight { get; private set; }
    
    /// <summary>
    /// Read the water data from a byte array starting at offset
    /// </summary>
    /// <param name="blockData"></param>
    /// <param name="offset"></param>
    /// <returns>Number of bytes used</returns>
    public override int ReadData(byte[] blockData, int offset)
    {
        var objectType = BitConverter.ToInt32(blockData, offset + 0x00);
        if (objectType != PrefabType || (offset + StartOfVariableData > blockData.Length))
        {
            // Type mismatch or not enough bytes, return as error
            Data = [];
            return 0;
        }

        StartPos = GetVector3(blockData, offset + 0x04); // 3 x float @ 0x04 
        EndPos =  GetVector3(blockData, offset + 0x10);  //
        Flags = blockData.Skip(offset + 0x2B).FirstOrDefault();
        SegmentPointsCount = BitConverter.ToInt32(blockData, offset + 0x6B);
        BorderPointsCount = BitConverter.ToInt32(blockData, offset + 0x77);
        Depth = Math.Abs(EndPos.Z - StartPos.Z); // BitConverter.ToInt32(blockData, offset + 0x6F);
        Speed = BitConverter.ToInt32(blockData, offset + 0x73);

        SurfaceHeight = Math.Max(EndPos.Z, StartPos.Z);

        // TODO: Find out if there is a directional vector for water speed, or if uses the segment data for its direction

        var totalObjectSize = (SegmentPointsCount * 12) + (BorderPointsCount * 12) + StartOfVariableData;
        // Read points for inside data
        SegmentPointsList = [];
        BorderPointsList = [];
        for (var i = 0; i < SegmentPointsCount; i++)
            SegmentPointsList.Add(GetVector3(blockData, offset + StartOfVariableData + (i * 12)));

        // Read border data
        var entryStart2 = StartOfVariableData + (SegmentPointsCount * 12);
        for (var i = 0; i < BorderPointsCount; i++)
            BorderPointsList.Add(GetVector3(blockData, offset + entryStart2 + (i * 12)));

        Data = (SegmentPointsCount <= 0) && (BorderPointsCount <= 0)
            ? [] // Return as invalid if no points are defined
            : blockData.Skip(offset).Take(totalObjectSize).ToArray();

        return Data.Length;
    }
}
