using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// that unit and increments its heir-level byte; no level value follows on the wire.
/// </summary>
public class SCHeirLevelUpPacket(uint bc) : GamePacket(SCOffsets.SCHeirLevelUpPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(bc);
        return stream;
    }
}
