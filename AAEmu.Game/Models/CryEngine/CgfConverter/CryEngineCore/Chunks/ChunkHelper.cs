using System.Numerics;

namespace CgfConverter.CryEngineCore
{
    /// <summary>
    /// Helper chunk.  This is the top level, then nodes, then mesh, then mesh subsets. CCCC0001  
    /// </summary>
    public abstract class ChunkHelper : Chunk
    {
        public string Name;
        public HelperType HelperType;
        public Vector3 Pos;
        public Matrix4x4 Transform;

        public override string ToString()
        {
            return $@"Chunk Type: {ChunkType}, ID: {ID:X}, Version: {Version}";
        }

        public void WriteChunk()
        {
            Utilities.Log(LogLevelEnum.Verbose, "*** START Helper Chunk ***");
            Utilities.Log(LogLevelEnum.Verbose, "    ChunkType:   {0}", ChunkType);
            Utilities.Log(LogLevelEnum.Verbose, "    Version:     {0:X}", Version);
            Utilities.Log(LogLevelEnum.Verbose, "    ID:          {0:X}", ID);
            Utilities.Log(LogLevelEnum.Verbose, "    HelperType:  {0}", HelperType);
            Utilities.Log(LogLevelEnum.Verbose, "    Position:    {0}, {1}, {2}", Pos.X, Pos.Y, Pos.Z);
            Utilities.Log(LogLevelEnum.Verbose, "*** END Helper Chunk ***");
        }
    }
}
