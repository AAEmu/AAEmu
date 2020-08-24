using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCGimmicksRemovedPacket : GamePacket
    {
        private readonly uint[] _ids;

        public SCGimmicksRemovedPacket(uint[] ids) : base(SCOffsets.SCGimmicksRemovedPacket, 1)
        {
            _ids = ids;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((ushort) _ids.Length); // TODO max 500 elements
            foreach (var id in _ids)
            {
                stream.WriteBc(id);
            }

            return stream;
        }
    }
}
