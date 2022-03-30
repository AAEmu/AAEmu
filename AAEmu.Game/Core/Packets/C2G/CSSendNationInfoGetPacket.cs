using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSendNationInfoGetPacket : GamePacket
    {
        public CSSendNationInfoGetPacket() : base(CSOffsets.CSSendNationInfoGetPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSSendNationInfoGetPacket");
        }
    }
}
