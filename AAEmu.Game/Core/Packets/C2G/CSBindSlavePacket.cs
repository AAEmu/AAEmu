using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSBindSlavePacket : GamePacket
    {
        public CSBindSlavePacket() : base(0x031, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var tl = stream.ReadUInt16();

            _log.Debug("BindSlave, Tl: {0}", tl);
        }
    }
}
