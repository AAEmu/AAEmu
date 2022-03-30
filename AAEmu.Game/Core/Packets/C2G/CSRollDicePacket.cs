using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRollDicePacket : GamePacket
    {
        public CSRollDicePacket() : base(CSOffsets.CSRollDicePacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var max = stream.ReadUInt32();
            
            _log.Warn("RollDice, Max: {0}", max);
        }
    }
}
