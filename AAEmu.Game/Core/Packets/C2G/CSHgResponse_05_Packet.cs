using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSHgResponse_05_Packet : GamePacket
    {
        public CSHgResponse_05_Packet() : base(CSOffsets.CSHgResponse_05_Packet, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            stream.ReadByte();
            stream.ReadString();
            var dir = stream.ReadString();
            _log.Info("CSHgResponsePacket, dir: {0}", dir);
        }
    }
}
