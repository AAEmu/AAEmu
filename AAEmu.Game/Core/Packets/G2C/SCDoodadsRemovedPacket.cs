using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCDoodadsRemovedPacket : GamePacket
    {
        private readonly bool _last;
        private readonly uint[] _ids;
        public const int MaxCountPerPacket = 400; // Suggested Maximum Size

        public SCDoodadsRemovedPacket(bool last, uint[] ids) : base(SCOffsets.SCDoodadsRemovedPacket, 5)
        {
            _last = last;
            _ids = ids;
        }

        public override PacketStream Write(PacketStream stream)
        {
            var index = 0;
            var doodadsToRemove = _ids.Length;
            stream.Write((ushort)doodadsToRemove);
            stream.Write(_last);
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
                stream.Write((byte)es); // es - BitFlags of doodads that have been set for removal in this block
                doodadsToRemove -= doodadsToRemoveNow;
            }
            while (doodadsToRemove > 0);

            return stream;
        }
    }
}
