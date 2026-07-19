using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCDoodadOriginatorPacket(uint objId, uint newOwnerId, FactionsEnum newFaction)
    : GamePacket(SCOffsets.SCDoodadOriginatorPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write(newOwnerId);
        stream.Write((uint)newFaction);

        return stream;
    }
}
