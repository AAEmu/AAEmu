using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.C2S
{
    public class CTUccCharacterNamePacket : StreamPacket
    {
        public CTUccCharacterNamePacket() : base(0x09)
        {
        }
        
        public override void Read(PacketStream stream)
        {
            var type = stream.ReadUInt32();
        }
    }
}