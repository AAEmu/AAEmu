using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRequestHousingTradeListPacket : GamePacket
    {
        public CSRequestHousingTradeListPacket() : base(CSOffsets.CSRequestHousingTradeListPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var tl = stream.ReadUInt16();
            _log.Debug("CSRequestHousingTradeList, Tl: {0}", tl);
        }
    }
}
