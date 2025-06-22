using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.G2L;

public class GLRegisterGameServerPacket() : InternalPacket(TypeId), IInternalPacket
{
    public new static ushort TypeId => GLOffsets.GLRegisterGameServerPacket;
    
    public string? SecretKey { get; private set; }
    public GameServerId GsId { get; private set; }
    public List<GameServerId>? Mirrors { get; private set; }

    public override void Read(PacketStream stream)
    {
        SecretKey = stream.ReadString();
        GsId = new GameServerId(stream.ReadByte());
        var additionalesCount = stream.ReadInt32();
        var mirrors = new List<GameServerId>(additionalesCount);
        for (var i = 0; i < additionalesCount; i++)
            mirrors.Add(new GameServerId(stream.ReadByte()));

        Mirrors = mirrors;
    }
}
