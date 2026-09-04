using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// SC 0x317 broadcast: u8 openType — squad board visibility changed (leader action).
/// </summary>
public class SCChangeSquadOpenTypePacket(byte openType) : GamePacket(SCOffsets.SCChangeSquadOpenTypeBcast, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(openType);
        return stream;
    }
}
