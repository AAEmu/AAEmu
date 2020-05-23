using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Trading;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSellBackpackGoodsPacket : GamePacket
    {
        public CSSellBackpackGoodsPacket() : base(0x042, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var objId = stream.ReadBc();

            var price = SpecialtyManager.Instance.GetBasePriceForSpecialty(Connection.ActiveChar, objId);
            
            _log.Warn("CSSellBackpackGoods, ObjId: {0}. BasePrice: {1}", objId, price);
        }
    }
}
