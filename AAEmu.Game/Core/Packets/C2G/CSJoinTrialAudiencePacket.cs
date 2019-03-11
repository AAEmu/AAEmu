using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSJoinTrialAudiencePacket : GamePacket
    {
        public CSJoinTrialAudiencePacket() : base(0x77, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var id = stream.ReadUInt32();

            _log.Warn("JoinTrialAudience, Id: {0}", id);
        }
    }
}
