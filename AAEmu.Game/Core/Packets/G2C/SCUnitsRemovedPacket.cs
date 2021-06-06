using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnitsRemovedPacket : GamePacket
    {
        private readonly uint[] _ids;
        public const int MaxCountPerPacket = 500 ; // Suggested Maximum Size (originally 300)

        public SCUnitsRemovedPacket(uint[] ids) : base(SCOffsets.SCUnitsRemovedPacket, 1)
        {
            _ids = ids;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((ushort) _ids.Length);
            foreach (var id in _ids)
                stream.WriteBc(id);

            return stream;
        }
    }
}
