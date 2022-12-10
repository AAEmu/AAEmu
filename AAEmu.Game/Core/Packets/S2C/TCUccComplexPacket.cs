using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;
using AAEmu.Game.Models.Stream;

namespace AAEmu.Game.Core.Packets.S2C
{
    public class TCUccComplexPacket : StreamPacket
    {
        private Ucc _ucc;
        public TCUccComplexPacket(Ucc ucc) : base(TCOffsets.TCUccComplexPacket)
        {
            _ucc = ucc;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((ulong) _ucc.Id); // type
            stream.Write((ulong) 0); // type unk
            stream.Write((ulong) 0); // type unk
            stream.Write((ulong) _ucc.Id); // type
            stream.Write(_ucc.Modified); // modified

            return stream;
        }
    }
}
