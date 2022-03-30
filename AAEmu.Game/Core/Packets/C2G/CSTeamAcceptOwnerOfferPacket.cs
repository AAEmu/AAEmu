using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSTeamAcceptOwnerOfferPacket : GamePacket
    {
        public CSTeamAcceptOwnerOfferPacket() : base(CSOffsets.CSTeamAcceptOwnerOfferPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSTeamAcceptOwnerOfferPacket");
        }
    }
}
