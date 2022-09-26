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
            stream.ReadByte();
            stream.ReadString();
            var dir = stream.ReadString();
            _log.Info("CSHgResponsePacket, dir: {0}", dir);
        }
    }
}
