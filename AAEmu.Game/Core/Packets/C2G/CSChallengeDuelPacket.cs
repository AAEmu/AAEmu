using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSChallengeDuelPacket : GamePacket
    {
        public CSChallengeDuelPacket() : base(CSOffsets.CSChallengeDuelPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var challengedId = stream.ReadUInt32(); // Id the one we challenged to a duel

            DuelManager.Instance.DuelRequest(Connection.ActiveChar, challengedId); // only to the enemy
        }
    }
}
