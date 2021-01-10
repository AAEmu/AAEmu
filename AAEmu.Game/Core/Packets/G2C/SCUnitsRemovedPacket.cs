using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnitsRemovedPacket : GamePacket
    {
        private readonly uint[] _ids;

        public SCUnitsRemovedPacket(uint[] ids) : base(SCOffsets.SCUnitsRemovedPacket, 5)
        {
            _ids = ids;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((ushort) _ids.Length); // TODO max 300 units
            foreach (var id in _ids)
                stream.WriteBc(id);

            return stream;
        }
    }
}
