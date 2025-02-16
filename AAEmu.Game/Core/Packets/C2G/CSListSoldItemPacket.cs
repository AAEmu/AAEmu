using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSListSoldItemPacket : GamePacket
{
    public CSListSoldItemPacket() : base(CSOffsets.CSListSoldItemPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var npcObjId = stream.ReadBc();
        var doodadObjId = stream.ReadBc();

        var npc = WorldManager.Instance.GetNpc(npcObjId);
        if (npc == null || !npc.Template.Merchant)
            return;
        Connection.ActiveChar.BuyBackItems.ReNumberSlots();
        Connection.SendPacket(new SCSoldItemListPacket(Connection.ActiveChar.BuyBackItems.Items));
    }
}
