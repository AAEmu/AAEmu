using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy
{
    public class SetAspectProfilePacket : GamePacket
    {
        // TODO Only command without body...
        public SetAspectProfilePacket() : base(0x00d, 2)
        {
        }
    }
}