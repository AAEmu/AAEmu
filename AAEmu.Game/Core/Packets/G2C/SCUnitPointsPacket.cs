using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnitPointsPacket : GamePacket
    {
        private readonly uint _id;
        private readonly int _preciseHealth;
        private readonly int _preciseMana;

        public SCUnitPointsPacket(uint id, int health, int mana) : base(SCOffsets.SCUnitPointsPacket, 5)
        {
            _id = id;
            _preciseHealth = health * 100;
            _preciseMana = mana * 100;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_id);
            stream.Write(_preciseHealth);
            stream.Write(_preciseMana);
            return stream;
        }
    }
}
