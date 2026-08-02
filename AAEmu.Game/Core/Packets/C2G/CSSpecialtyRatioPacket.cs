using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSpecialtyRatioPacket() : GamePacket(CSOffsets.CSSpecialtyRatioPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var zoneGroupId = stream.ReadUInt16();
        var npcTemplateId = stream.ReadUInt32();

        SpecialtyManager.Instance.SendBuyList(Connection.ActiveChar, zoneGroupId, npcTemplateId);
    }
}
