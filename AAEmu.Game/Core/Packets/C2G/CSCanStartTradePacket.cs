using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSCanStartTradePacket : GamePacket
    {
        public CSCanStartTradePacket() : base(CSOffsets.CSCanStartTradePacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var objId = stream.ReadBc();

            var target = WorldManager.Instance.GetCharacterByObjId(objId);
            if (target == null) return;
            var owner = Connection.ActiveChar;
            
            TradeManager.Instance.CanStartTrade(owner, target);
        }
    }
}
