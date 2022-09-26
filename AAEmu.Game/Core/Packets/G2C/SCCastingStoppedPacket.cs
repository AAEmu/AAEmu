using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCCastingStoppedPacket : GamePacket
    {
        private readonly ushort _tlId;
        private readonly uint _duration;

        public SCCastingStoppedPacket(ushort tlId, uint duration) : base(SCOffsets.SCCastingStoppedPacket, 5)
        {
            _tlId = tlId;
            _duration = duration;
        }

        public override PacketStream Write(PacketStream stream)
        {
            _log.Warn("SCCastingStoppedPacket: tl = {0}", _tlId);

            stream.Write(_tlId);      // skillId (tl)
            stream.Write(_duration);  // duration

            return stream;
        }
    }
}
