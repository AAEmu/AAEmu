using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// </summary>
public class SCUnitOpenEquipInfoPacket(uint unitObjId, bool open) : GamePacket(SCOffsets.SCUnitOpenEquipInfoPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(unitObjId);
        stream.Write(open);
        return stream;
    }
}
