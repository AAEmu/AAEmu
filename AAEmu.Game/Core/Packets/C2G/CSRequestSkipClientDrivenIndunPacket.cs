using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRequestSkipClientDrivenIndunPacket : GamePacket
    {
        public CSRequestSkipClientDrivenIndunPacket() : base(CSOffsets.CSRequestSkipClientDrivenIndunPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSRequestSkipClientDrivenIndunPacket");
        }
    }
}
