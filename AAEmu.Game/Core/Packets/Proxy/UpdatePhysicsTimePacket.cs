using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy
{
    public class UpdatePhysicsTimePacket : GamePacket
    {
        private long _tm;

        public UpdatePhysicsTimePacket() : base(PPOffsets.UpdatePhysicsTimePacket, 2)
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
