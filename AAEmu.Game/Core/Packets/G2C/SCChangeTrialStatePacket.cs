using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCChangeTrialStatePacket : GamePacket
    {
        private readonly uint _trialId;
        private readonly byte _state;
        private readonly int _curJury;
        private readonly uint _remainTime;

        public SCChangeTrialStatePacket(uint trialId, byte state, int curJury, uint remainTime) : 
            base(SCOffsets.SCChangeTrialStatePacket, 5)
        {
            _trialId = trialId;
            _state = state;
            _curJury = curJury;
            _remainTime = remainTime;

        }
        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_trialId);
            stream.Write(_state);
            stream.Write(_curJury);
            stream.Write(_remainTime);
            return stream;

        }
    }
}
