using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCHouseBuildProgressPacket : GamePacket
    {
        private readonly ushort _tl;
        private readonly uint _unkId;
        private readonly int _allStep;
        private readonly int _curStep;
        
        public SCHouseBuildProgressPacket(ushort tl, uint unkId, int allStep, int curStep) : base(0x0b5, 1)
        {
            _tl = tl;
            _unkId = unkId;
            _allStep = allStep;
            _curStep = curStep;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_tl);
            stream.Write(_unkId);
            stream.Write(_allStep);
            stream.Write(_curStep);
            return stream;
        }
    }
}
