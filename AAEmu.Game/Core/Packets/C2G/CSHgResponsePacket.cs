using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSHGResponsePacket : GamePacket
    {
        public CSHGResponsePacket() : base(CSOffsets.CSHGResponsePacket, 1)
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
