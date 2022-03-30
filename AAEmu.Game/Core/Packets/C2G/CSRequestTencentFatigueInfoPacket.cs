using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRequestTencentFatigueInfoPacket : GamePacket
    {
        public CSRequestTencentFatigueInfoPacket() : base(CSOffsets.CSRequestTencentFatigueInfoPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSRequestTencentFatigueInfoPacket");
        }
    }
}
