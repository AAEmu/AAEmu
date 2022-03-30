using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRequestResidentBalanceInfoPacket : GamePacket
    {
        public CSRequestResidentBalanceInfoPacket() : base(CSOffsets.CSRequestResidentBalanceInfoPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSRequestResidentBalanceInfo");
        }
    }
}
