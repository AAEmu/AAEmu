using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSHgResponsePacket : GamePacket
    {
        public CSHgResponsePacket() : base(CSOffsets.CSHgResponsePacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            stream.ReadByte();   // cmd
            stream.ReadString(); // md5, len = 16
            var dir = stream.ReadString(); // dir, len = 63
            _log.Info("CSHgResponsePacket, dir: {0}", dir);
        }
    }
}
