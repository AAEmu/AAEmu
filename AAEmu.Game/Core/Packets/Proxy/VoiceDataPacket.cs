using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy
{
    public class VoiceDataPacket : GamePacket
    {
        // TODO Only command without body...
        public VoiceDataPacket() : base(PPOffsets.VoiceDataPacket, 2)
        {

        }
    }
}
