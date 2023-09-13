using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.Stream;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.C2S
{
    public class CTRequestEmblemPacket : StreamPacket
    {
        public CTRequestEmblemPacket() : base(CTOffsets.CTRequestEmblemPacket)
        {
        }

        public override void Read(PacketStream stream)
        {
            var uccId = stream.ReadUInt64();
            UccManager.Instance.RequestUcc(Connection, uccId);
        }
    }
}
