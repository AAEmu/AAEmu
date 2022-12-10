using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCLootDiceNotifyPacket : GamePacket
    {
        private readonly string _charName;
        private readonly sbyte _dice;

        public SCLootDiceNotifyPacket(string charName, sbyte dice) : base(SCOffsets.SCLootDiceNotifyPacket,1)
        {
            _charName = charName;
            _dice = dice;
        }
        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_charName);
            stream.Write(_dice);
            return stream;
        }
    }
}
