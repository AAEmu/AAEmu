using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTrialCancledPacket : GamePacket
    {
        private readonly uint _trialId;

        public SCTrialCancledPacket(uint trialId) : base(SCOffsets.SCTrialCancledPacket, 5)
        {
            _trialId = trialId;
        }
        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_trialId);
            return stream;
        }
    }
}
