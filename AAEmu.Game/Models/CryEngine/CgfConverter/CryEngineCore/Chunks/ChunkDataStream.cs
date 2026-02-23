using System.Numerics;

namespace CgfConverter.CryEngineCore;

public abstract class ChunkDataStream : Chunk // Contains data such as vertices, normals, etc.
{
    public uint Flags { get; set; } // not used, but looks like the start of the Data Stream chunk
    public uint Flags1 { get; set; } // not used.  UInt32 after Flags that looks like offsets
    public uint Flags2 { get; set; } // not used, looks almost like a filler.
    public DatastreamType DataStreamType { get; set; } // type of data (vertices, normals, uv, etc)
    public uint NumElements { get; set; } // Number of data entries
    public uint BytesPerElement { get; set; } // Bytes per data entry
    public uint Reserved1 { get; set; }
    public uint Reserved2 { get; set; }
    
    public Vector3[] Vertices;
    public Vector3[] Normals;
    public UV[] UVs;
    public uint[] Indices;
    public IRGBA[] Colors;
    public IRGBA[] Colors2;

    // For Tangents on down, this may be a 2 element array.  See line 846+ in cgf.xml
    public Tangent[,] Tangents;  // for dataStreamType of 6, length is NumElements, 2.  
    public byte[,] ShCoeffs;     // for dataStreamType of 7, length is NumElement,BytesPerElements.
    public byte[,] ShapeDeformation; // for dataStreamType of 8, length is NumElements,BytesPerElement.
    public byte[,] BoneMap;      // for dataStreamType of 9, length is NumElements,BytesPerElement, 2.
    //public MeshBoneMapping[] BoneMap;      // for dataStreamType of 9, length is NumElements,BytesPerElement.
    public byte[,] FaceMap;      // for dataStreamType of 10, length is NumElements,BytesPerElement.
    public byte[,] VertMats;     // for dataStreamType of 11, length is NumElements,BytesPerElement.

    public override string ToString() => $@"Chunk Type: {ChunkType}, ID: {ID:X}, Ver: {Version}, Datastream Type: {DataStreamType}, Number of Elements: {NumElements}, Bytes per Element: {BytesPerElement}";
}
