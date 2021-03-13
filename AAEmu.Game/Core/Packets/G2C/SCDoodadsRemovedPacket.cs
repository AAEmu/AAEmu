using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCDoodadsRemovedPacket : GamePacket
    {
        private readonly bool _last;
        private readonly uint[] _ids;

        public SCDoodadsRemovedPacket(bool last, uint[] ids) : base(SCOffsets.SCDoodadsRemovedPacket, 5)
        {
            _last = last;
            _ids = ids;
        }

        public override PacketStream Write(PacketStream stream)
        {
            //stream.Write((ushort) _ids.Length); // TODO max 400 elements
            //stream.Write(_last);
            //foreach (var id in _ids)
            //{
            //    stream.WriteBc(id);
            //    stream.Write(false); // e
            //}

            var index = 0;
            var doodadsToRemove = _ids.Length; // The calling code sends no more than 400 elements
            stream.Write((ushort)doodadsToRemove); // count max 400
            stream.Write(_last);                   // last
            do
            {
                var es = 0;
                var jndex = 0;
                var doodadsToRemoveNow = doodadsToRemove >= 8 ? 8 : doodadsToRemove;
                do
                {
                    stream.WriteBc(_ids[index]);
                    index++;

                    es |= 1 << jndex;
                    jndex++;
                }
                while (jndex < doodadsToRemoveNow);
                //stream.Write((byte)es); // es - BitFlags of doodads that have been set for removal in this block
                stream.Write(false);
                doodadsToRemove -= doodadsToRemoveNow;
            }
            while (doodadsToRemove > 0);

            return stream;
        }
    }
}
