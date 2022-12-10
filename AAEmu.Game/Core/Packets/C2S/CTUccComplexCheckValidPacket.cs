using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.Stream;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.C2S
{
    public class CTUccComplexCheckValidPacket : StreamPacket
    {
        public CTUccComplexCheckValidPacket() : base(CTOffsets.CTUccComplexCheckValidPacket)
        {
        }

        public override void Read(PacketStream stream)
        {
            var type = stream.ReadUInt64();
            
            UccManager.Instance.CheckUccIsValid(Connection, type);
        }
    }
}
