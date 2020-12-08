using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSChangeSlaveNamePacket : GamePacket
    {
        public CSChangeSlaveNamePacket() : base(CSOffsets.CSChangeSlaveNamePacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var tl = stream.ReadUInt16();
            var name = stream.ReadString();

            _log.Debug("ChangeSlaveName, Tl: {0}, Name: {1}", tl, name);
        }
    }
}
