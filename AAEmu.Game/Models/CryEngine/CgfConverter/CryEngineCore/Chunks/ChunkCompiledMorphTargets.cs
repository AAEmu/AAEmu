namespace CgfConverter.CryEngineCore;

public abstract class ChunkCompiledMorphTargets : Chunk
{
    public uint NumberOfMorphTargets;
    public MeshMorphTargetVertex[]? MorphTargetVertices;

    public override string ToString() => $@"Chunk Type: {ChunkType}, ID: {ID:X}, NUmber of Morph Targets: {NumberOfMorphTargets}";
}
