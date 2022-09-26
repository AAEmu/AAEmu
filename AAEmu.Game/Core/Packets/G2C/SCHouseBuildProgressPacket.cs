using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCHouseBuildProgressPacket : GamePacket
    {
        private readonly ushort _tl;
        private readonly uint _modelId;
        private readonly int _allStep;
        private readonly int _curStep;
        
        public SCHouseBuildProgressPacket(ushort tl, uint modelId, int allStep, int curStep) : base(SCOffsets.SCHouseBuildProgressPacket, 5)
        {
            _tl = tl;
            _modelId = modelId;
            _allStep = allStep;
            _curStep = curStep;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_tl);
            stream.Write(_modelId);
            stream.Write(_allStep);
            stream.Write(_curStep);
            return stream;
        }
    }
}
