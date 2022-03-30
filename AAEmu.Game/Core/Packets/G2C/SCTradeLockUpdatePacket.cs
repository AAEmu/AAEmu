using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTradeLockUpdatePacket : GamePacket
    {
        private readonly bool _myLock;
        private readonly bool _otherLock;

        public SCTradeLockUpdatePacket(bool myLock, bool otherLock) : base(SCOffsets.SCTradeLockUpdatePacket, 5)
        {
            _myLock = myLock;
            _otherLock = otherLock;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_myLock);
            stream.Write(_otherLock);
            return stream;
        }
    }
}
