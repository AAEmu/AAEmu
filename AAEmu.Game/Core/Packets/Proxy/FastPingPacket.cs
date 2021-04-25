using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy
{
    public class FastPingPacket : GamePacket
    {
        public FastPingPacket() : base(PPOffsets.FastPingPacket, 2)
        {
        }

        public override void Read(PacketStream stream)
        {
            var sent = stream.ReadUInt32();
            Connection.SendPacket(new FastPongPacket(sent));
        }
    }
}
