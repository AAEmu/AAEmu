using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.Stream;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.C2S
{
    public class CTUccComplexPacket : StreamPacket
    {
        public CTUccComplexPacket() : base(CTOffsets.CTUccComplexPacket)
        {
        }

        public override void Read(PacketStream stream)
        {
            var type = stream.ReadUInt64();
            
            UccManager.Instance.UccComplex(Connection, type);
        }
    }
}
