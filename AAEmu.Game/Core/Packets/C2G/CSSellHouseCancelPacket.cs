using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSellHouseCancelPacket : GamePacket
    {
        public CSSellHouseCancelPacket() : base(CSOffsets.CSSellHouseCancelPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var tl = stream.ReadUInt16();

            _log.Debug("SellHouseCancel, Tl: {0}", tl);
        }
    }
}
