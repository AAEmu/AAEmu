using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnitExpeditionChangedPacket(
    uint unitId,
    uint characterId,
    string kicker,
    string unitName,
    uint id,
    uint expeditionId,
    bool expel)
    : GamePacket(SCOffsets.SCUnitExpeditionChangedPacket, 5)
{
    // TODO nation? faction?

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(characterId);
        stream.Write(kicker);
        stream.Write(unitName);
        stream.Write(id);
        stream.Write(expeditionId);
        stream.Write(expel);
        return stream;
    }
}
