using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTrialAudienceJoinedPacket : GamePacket
    {
        private readonly uint _trialId;
        private readonly byte _bc;
        private readonly string _audienceName;

        public SCTrialAudienceJoinedPacket(uint trialId, byte bc, string audienceName) :
            base(SCOffsets.SCTrialAudienceJoinedPacket, 5)
        {
            _trialId = trialId;
            _bc = bc;
            _audienceName = audienceName;
        }
        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_trialId);
            stream.Write(_bc);
            stream.Write(_audienceName);
            return stream;
        }
    }
}
