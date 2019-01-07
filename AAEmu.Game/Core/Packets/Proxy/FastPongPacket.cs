using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy
{
    public class FastPongPacket : GamePacket
    {
        public FastPongPacket() : base(0x016, 2)
        {
            
        }
    }
}