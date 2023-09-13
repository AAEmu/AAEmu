using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSLootDicePacket : GamePacket
    {
        public CSLootDicePacket() : base(CSOffsets.CSLootDicePacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var iid = stream.ReadUInt64();
            var roll = stream.ReadBoolean();

            _log.Warn("LootDice, IId: {0}, Roll: {1}", iid, roll);
        }
    }
}
