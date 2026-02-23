using System.Collections.Generic;

namespace CgfConverter.CryEngineCore
{
    /// <summary>
    /// Legacy class.  Not used
    /// </summary>
    public abstract class ChunkBoneNameList : Chunk
    {
        public int NumEntities;
        public List<string> BoneNames = new List<string>();

        public override string ToString()
        {
            return $@"Chunk Type: {ChunkType}, ID: {ID:X}, Number of Targets: {NumEntities}";
        }
    }
}
