using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy
{
    public class EntityClassRegistrationPacket : GamePacket
    {
        public EntityClassRegistrationPacket() : base(PPOffsets.EntityClassRegistrationPacket, 2)
        {
        }

        public override void Read(PacketStream stream)
        {
            var i = stream.ReadUInt16();
            var name = stream.ReadString(); // old size 511
        }
    }
}
