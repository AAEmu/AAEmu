using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSStartTradePacket : GamePacket
    {
        public CSStartTradePacket() : base(0x0ec, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var objId = stream.ReadBc();

            var owner = WorldManager.Instance.GetCharacterByObjId(objId);
            if (owner == null) return;
            var target = DbLoggerCategory.Database.Connection.ActiveChar;
            TradeManager.Instance.StartTrade(owner, target);
        }
    }
}
