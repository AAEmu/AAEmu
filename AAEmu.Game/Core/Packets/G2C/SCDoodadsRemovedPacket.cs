using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCDoodadsRemovedPacket : GamePacket
    {
        private readonly bool _last;
        private readonly uint[] _ids;

        public SCDoodadsRemovedPacket(bool last, uint[] ids) : base(SCOffsets.SCDoodadsRemovedPacket, 1)
        {
            _last = last;
            _ids = ids;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((ushort) _ids.Length); // TODO max 400 elements
            stream.Write(_last);
            foreach (var id in _ids)
            {
                stream.WriteBc(id);
                stream.Write(false); // e  if false then the doodad will be deleted
            }

            return stream;
        }
    }
}
