using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCICSCheckTimePacket : GamePacket
    {
        public SCICSCheckTimePacket() : base(0x1d0, 1)
        {
        }
    }
}
