using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.C2S
{
    public class CTCancelCellPacket : StreamPacket
    {
        public CTCancelCellPacket() : base(CTOffsets.CTCancelCellPacket)
        {
        }

        public override void Read(PacketStream stream)
        {
            var i = stream.ReadUInt32();
            var x = stream.ReadInt32();
            var y = stream.ReadInt32();

            _log.Warn("CTCancelCellPacket #.{0} ({1},{2})", i, x, y);
        }
    }
}
