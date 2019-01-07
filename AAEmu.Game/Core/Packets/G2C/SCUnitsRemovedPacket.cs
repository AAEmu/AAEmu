using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnitsRemovedPacket : GamePacket
    {
        private readonly uint[] _ids;

        public SCUnitsRemovedPacket(uint[] ids) : base(0x065, 1)
        {
            _ids = ids;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((short) _ids.Length); // TODO max 500 units
            foreach (var id in _ids)
                stream.WriteBc(id);

            return stream;
        }
    }
}