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
            string md5;
            string dir = "";
            var cmd = stream.ReadByte();
            if (cmd == 0)
            {
                md5 = stream.ReadString();
            }
            if (cmd == 1)
            {
                md5 = stream.ReadString();
                dir = stream.ReadString();
            }
            if (cmd == 3)
            {
                var count = stream.ReadInt32();
                var size = stream.ReadInt32();
                for (var i = 0; i < size; i++)
                {
                    var id = stream.ReadInt32();
                }
            }
            if (cmd == 4)
            {
                var unk = stream.ReadInt32();
            }
            if (cmd == 4)
            {
                dir = stream.ReadString();
            }
            _log.Info("CSHgResponsePacket, dir: {0}", dir);
        }
    }
}
