using System.Linq;
using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSListSoldItemPacket : GamePacket
    {
        public CSListSoldItemPacket() : base(0x0b1, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var npcObjId = stream.ReadBc();
            var npc = WorldManager.Instance.GetNpc(npcObjId);
            if (npc == null || !npc.Template.Merchant)
                return;
            DbLoggerCategory.Database.Connection.ActiveChar.BuyBackItems.ReNumberSlots();
            DbLoggerCategory.Database.Connection.SendPacket(new SCSoldItemListPacket(DbLoggerCategory.Database.Connection.ActiveChar.BuyBackItems.Items));
        }
    }
}
