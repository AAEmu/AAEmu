using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Faction;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCFactionCreatedPacket(
    SystemFaction faction,
    uint ownerObjId,
    (uint memberObjId, uint memberId, string name)[] members)
    : GamePacket(SCOffsets.SCFactionCreatedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(faction);

        stream.WriteBc(ownerObjId);
        stream.Write((byte)members.Length); // TODO max length 4
        foreach (var (objId, id, name) in members)
        {
            stream.WriteBc(objId);
            stream.Write(id);
            stream.Write(name);
        }

        return stream;
    }
}
