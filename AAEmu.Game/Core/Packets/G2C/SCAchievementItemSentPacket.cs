using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCAchievementItemSentPacket : GamePacket
    {
        private readonly uint _id;
        private readonly bool _byMail;

        public SCAchievementItemSentPacket(uint id, bool byMail) : base(SCOffsets.SCAchievementItemSentPacket, 5)
        {
            _id = id;
            _byMail = byMail;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_id);     // type
            stream.Write(_byMail); // byMail

            return stream;
        }
    }
}
