using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy
{
    public class UpdatePhysICSTimePacket : GamePacket
    {
        private long _tm;

        public UpdatePhysICSTimePacket() : base(PPOffsets.UpdatePhysICSTimePacket, 2)
        {
        }

        public override void Read(PacketStream stream)
        {
            _tm = stream.ReadInt64(); // unixtime or seconds...
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_tm);

            return stream;
        }
    }
}
