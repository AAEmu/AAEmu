using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSLearnBuffPacket : GamePacket
    {
        public CSLearnBuffPacket() : base(CSOffsets.CSLearnBuffPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var buffId = stream.ReadUInt32();
            
            Connection.ActiveChar.Skills.AddBuff(buffId);
        }
    }
}
