using CgfConverter.Models.Materials;
using System;

namespace CgfConverter.CryEngineCore;

public abstract class ChunkMtlName : Chunk  
{
    /// <summary> Type of Material associated with this name </summary>
    public MtlNameType MatType { get; internal set; }

    /// <summary> Name of the Material </summary>
    public string Name { get; set; } = string.Empty;
    
    public MtlNamePhysicsType[] PhysicsType { get; internal set; }
    
    /// <summary> Number of Materials in this MtlName (Max: 66) </summary>
    public uint NumChildren { get; internal set; }
    
    public uint[] ChildIDs { get; internal set; }
    
    public uint NFlags2 { get; internal set; }

    public Guid? AssetId { get; internal set; }

    /// <summary>The actual Material from the mtl file for this chunk.</summary>
    public Material? Material { get; set; }

    public override string ToString() =>
        $@"Chunk Type: {ChunkType}, ID: {ID:X}, Material Name: {Name}, Number of Children: {NumChildren}, Material Type: {MatType}";
}
