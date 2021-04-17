using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.C2S
{
    public class CTContinuePacket : StreamPacket
    {
        public CTContinuePacket() : base(CTOffsets.CTContinuePacket)
        {
        }

        public override void Read(PacketStream stream)
        {
            var id = stream.ReadInt32();
            var next = stream.ReadInt32();
            StreamManager.Instance.ContinueCell(Connection, id, next);
        }
    }
}
