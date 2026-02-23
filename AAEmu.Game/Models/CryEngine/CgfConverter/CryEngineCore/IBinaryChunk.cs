using System.IO;

namespace CgfConverter.CryEngineCore;

public interface IBinaryChunk
{
    void Read(BinaryReader reader);
    void Write(BinaryWriter writer);
}
