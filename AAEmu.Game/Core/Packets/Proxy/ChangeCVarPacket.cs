using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy
{
    public class ChangeCVarPacket : GamePacket
    {
        public ChangeCVarPacket() : base(PPOffsets.ChangeCVarPacket, 2)
        {
        }

        public override void Read(PacketStream stream)
        {
            var keyLength = stream.ReadUInt16();
            var key = stream.ReadBytes(keyLength); // old size 511
            var valueLength = stream.ReadUInt16();
            var value = stream.ReadBytes(valueLength); // old size 511
        }
    }
}
