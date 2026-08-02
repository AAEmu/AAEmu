using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Raw SC body for World ZW→SC relay (movement/combat) without re-marshaling Game models.
/// </summary>
public class SCOpaquePacket(ushort typeId, byte[] body) : GamePacket(typeId, 1)
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Off;

    public override PacketStream Write(PacketStream stream)
    {
        if (body is { Length: > 0 })
            stream.Write(body, false);
        return stream;
    }
}
