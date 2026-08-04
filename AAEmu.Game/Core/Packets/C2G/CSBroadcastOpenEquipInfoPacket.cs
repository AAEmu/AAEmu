using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// </summary>
public class CSBroadcastOpenEquipInfoPacket() : GamePacket(CSOffsets.CSBroadcastOpenEquipInfoPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var open = stream.ReadBoolean();
        var character = Connection.ActiveChar;
        if (character == null)
            return;

        Logger.Debug("CSBroadcastOpenEquipInfo: {0} open={1}", character.Name, open);
        character.IsEquipmentPublic = open;
        character.BroadcastPacket(new SCUnitOpenEquipInfoPacket(character.ObjId, open), true);
    }
}
