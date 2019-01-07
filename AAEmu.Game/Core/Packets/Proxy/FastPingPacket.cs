using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy
{
    public class FastPingPacket : GamePacket
    {
        public FastPingPacket() : base(0x015, 2)
        {
            
        }
    }
}