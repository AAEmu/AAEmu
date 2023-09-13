using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSListSpecialtyGoodsPacket : GamePacket
    {
        public CSListSpecialtyGoodsPacket() : base(CSOffsets.CSListSpecialtyGoodsPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var objId = stream.ReadBc();

            _log.Warn("ListSpecialtyGoods, ObjId: {0}", objId);
        }
    }
}
