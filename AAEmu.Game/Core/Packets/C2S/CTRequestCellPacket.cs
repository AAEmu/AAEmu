using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.C2S
{
    public class CTRequestCellPacket : StreamPacket
    {
        public CTRequestCellPacket() : base(CTOffsets.CTRequestCellPacket)
        {
        }

        public override void Read(PacketStream stream)
        {
            var instanceId = stream.ReadUInt32();
            var x = stream.ReadInt32();
            var y = stream.ReadInt32();

            _log.Warn("CTRequestCellPacket #.{0} ({1},{2})", instanceId, x, y);
            StreamManager.Instance.RequestCell(Connection, instanceId, x, y);
        }
    }
}
