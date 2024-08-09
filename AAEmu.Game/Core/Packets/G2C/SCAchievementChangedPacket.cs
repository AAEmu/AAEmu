using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCAchievementChangedPacket : GamePacket
    {
        private readonly uint _id;
        private readonly int _amount;

        public SCAchievementChangedPacket(uint id, int amount) : base(SCOffsets.SCAchievementChangedPacket, 1)
        {
            _id = id;
            _amount = amount;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_id);     // type
            stream.Write(_amount); // amount

            return stream;
        }
    }
}
