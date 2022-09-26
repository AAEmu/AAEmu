using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCCastingDelayedPacket : GamePacket
    {
        private readonly ushort _tlId;
        private readonly ushort _plotId;
        private readonly uint _delay;

        public SCCastingDelayedPacket(ushort tlId, ushort plotId, uint delay) : base(SCOffsets.SCCastingDelayedPacket, 5)
        {
            _tlId = tlId;
            _plotId = plotId;
            _delay = delay;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_tlId);   // skillId (tl)
            stream.Write(_plotId); // plotId (tl)
            stream.Write(_delay);  // delay

            return stream;
        }
    }
}
